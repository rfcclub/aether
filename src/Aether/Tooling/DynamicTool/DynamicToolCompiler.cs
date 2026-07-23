using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace Aether.Tooling.DynamicTool;

public static class DynamicToolCompiler
{
    private static readonly List<PortableExecutableReference> _references = new();

    static DynamicToolCompiler()
    {
        var refAssemblies = new HashSet<string>();

        void AddAssembly(Assembly asm)
        {
            if (asm == null || asm.IsDynamic) return;
            try
            {
                if (!string.IsNullOrEmpty(asm.Location) && refAssemblies.Add(asm.Location))
                    _references.Add(MetadataReference.CreateFromFile(asm.Location));
            }
            catch { }
        }

        AddAssembly(typeof(object).Assembly);
        AddAssembly(typeof(Console).Assembly);
        AddAssembly(typeof(System.Text.Json.JsonSerializer).Assembly);
        AddAssembly(typeof(IDynamicTool).Assembly);
        AddAssembly(typeof(System.Linq.Enumerable).Assembly);
        AddAssembly(typeof(System.Collections.Generic.List<>).Assembly);
        AddAssembly(typeof(System.Threading.Tasks.Task<>).Assembly);
        AddAssembly(typeof(System.ComponentModel.INotifyPropertyChanged).Assembly);

        // Load System.Runtime
        try { AddAssembly(Assembly.Load("System.Runtime")); } catch { }
        try { AddAssembly(Assembly.Load("System.Collections")); } catch { }
        try { AddAssembly(Assembly.Load("System.Threading.Tasks")); } catch { }
        try { AddAssembly(Assembly.Load("System.Linq")); } catch { }
        try { AddAssembly(Assembly.Load("System.Console")); } catch { }
        try { AddAssembly(Assembly.Load("System.Text.Json")); } catch { }
        try { AddAssembly(Assembly.Load("netstandard")); } catch { }
    }

    public static (Assembly? Assembly, IReadOnlyList<string> Errors) Compile(
        string csFilePath, ILogger? logger = null)
    {
        if (!File.Exists(csFilePath))
        {
            var err = $"File not found: {csFilePath}";
            logger?.LogError(err);
            return (null, new[] { err });
        }

        var sourceText = File.ReadAllText(csFilePath);
        var assemblyName = $"DynamicTool.{Path.GetFileNameWithoutExtension(csFilePath)}.{Guid.NewGuid():N}";

        var syntaxTree = CSharpSyntaxTree.ParseText(
            sourceText,
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest));

        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { syntaxTree },
            _references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var result = compilation.Emit(ms);

        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => $"{d.Id}: {d.GetMessage()} at {d.Location.GetLineSpan()}")
                .ToList();

            logger?.LogError("Compilation failed for {File}: {ErrorCount} errors",
                Path.GetFileName(csFilePath), errors.Count);

            return (null, errors);
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());

        logger?.LogInformation("Successfully compiled {File} -> {AssemblyName}",
            Path.GetFileName(csFilePath), assemblyName);

        return (assembly, Array.Empty<string>());
    }

    public static IEnumerable<Type> FindDynamicTools(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IDynamicTool).IsAssignableFrom(t));
    }

    public static IDynamicTool? CreateInstance(Type toolType)
    {
        try
        {
            return Activator.CreateInstance(toolType) as IDynamicTool;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Failed to create instance of {toolType.FullName}: {ex.Message}");
            return null;
        }
    }
}
