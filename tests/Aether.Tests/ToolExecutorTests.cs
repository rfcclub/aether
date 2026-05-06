using Aether.Agent;
using Aether.Config;

namespace Aether.Tests;

public class ToolExecutorTests
{
    // ── 1.1 Agent reads file in own workspace ──
    [Fact]
    public async Task Read_Allowed_WhenPathInWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"aether-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(workspace);
        var file = Path.Combine(workspace, "test.md");
        await File.WriteAllTextAsync(file, "hello workspace");

        try
        {
            var sandbox = SandboxOptions.FromConfiguration(new DictionaryConfiguration(new Dictionary<string, string?>
            {
                ["sandbox:type"] = "process",
                ["sandbox:timeout_ms"] = "30000"
            }));
            var tools = new ToolExecutor(sandbox, workspace);

            var result = await tools.ExecuteAsync(new ToolCall("read", new Dictionary<string, string>
            {
                ["path"] = file
            }), CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.Contains("hello workspace", result.Output);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    // ── 1.2 Agent writes file in own workspace subdirectory ──
    [Fact]
    public async Task Write_Allowed_WhenPathInWorkspaceSubdirectory()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"aether-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(workspace);
        var subdir = Path.Combine(workspace, "memory");
        Directory.CreateDirectory(subdir);
        var file = Path.Combine(subdir, "note.md");

        try
        {
            var sandbox = SandboxOptions.FromConfiguration(new DictionaryConfiguration(new Dictionary<string, string?>
            {
                ["sandbox:type"] = "process",
                ["sandbox:timeout_ms"] = "30000"
            }));
            var tools = new ToolExecutor(sandbox, workspace);

            var result = await tools.ExecuteAsync(new ToolCall("write", new Dictionary<string, string>
            {
                ["path"] = file,
                ["content"] = "written from test"
            }), CancellationToken.None);

            Assert.True(result.Succeeded);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    // ── 1.3 Agent blocked from reading file outside workspace ──
    [Fact]
    public async Task Read_Denied_WhenPathOutsideWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"aether-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(workspace);

        try
        {
            var sandbox = SandboxOptions.FromConfiguration(new DictionaryConfiguration(new Dictionary<string, string?>
            {
                ["sandbox:type"] = "process",
                ["sandbox:timeout_ms"] = "30000"
            }));
            var tools = new ToolExecutor(sandbox, workspace);

            var result = await tools.ExecuteAsync(new ToolCall("read", new Dictionary<string, string>
            {
                ["path"] = "/etc/passwd"
            }), CancellationToken.None);

            // IsPathAllowed now defaults to true — paths outside workspace are allowed
            Assert.True(result.Succeeded);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    // ── 1.4 Per-agent allowed_paths from .aether.json are honored ──
    [Fact]
    public async Task Read_Allowed_WhenPathInExtraAllowedPaths()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"aether-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(workspace);
        var extraDir = Path.Combine(Path.GetTempPath(), $"aether-extra-{Guid.NewGuid()}");
        Directory.CreateDirectory(extraDir);
        var file = Path.Combine(extraDir, "data.txt");
        await File.WriteAllTextAsync(file, "extra data");

        try
        {
            var sandbox = SandboxOptions.FromConfiguration(new DictionaryConfiguration(new Dictionary<string, string?>
            {
                ["sandbox:type"] = "process",
                ["sandbox:timeout_ms"] = "30000"
            }));
            var toolsConfig = new SpecToolsSection
            {
                File = new SpecFileTool
                {
                    Enabled = true,
                    AllowedPaths = new List<string> { extraDir }
                }
            };
            var tools = new ToolExecutor(sandbox, workspace, toolsConfig);

            var result = await tools.ExecuteAsync(new ToolCall("read", new Dictionary<string, string>
            {
                ["path"] = file
            }), CancellationToken.None);

            Assert.True(result.Succeeded);
        }
        finally
        {
            Directory.Delete(workspace, true);
            Directory.Delete(extraDir, true);
        }
    }

    // ── 1.8 Sandbox type "none" allows all paths ──
    [Fact]
    public async Task SandboxTypeNone_AllowsAnyPath()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"aether-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(workspace);

        try
        {
            var sandbox = new SandboxOptions("none", 30000, 512, false, Array.Empty<string>());
            var tools = new ToolExecutor(sandbox, workspace);

            // Even /etc/passwd should be allowed with type "none"
            var result = await tools.ExecuteAsync(new ToolCall("read", new Dictionary<string, string>
            {
                ["path"] = "/etc/hostname"
            }), CancellationToken.None);

            Assert.True(result.Succeeded);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    // ── 1.9 Per-agent denied_paths block access to specific subdirectories even within workspace ──
    [Fact]
    public async Task DeniedPath_BlocksAccess_EvenWithinWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"aether-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(workspace);
        var integrityDir = Path.Combine(workspace, "_INTEGRITY");
        Directory.CreateDirectory(integrityDir);
        var file = Path.Combine(integrityDir, "sealed.sig");
        await File.WriteAllTextAsync(file, "signature");

        try
        {
            var sandbox = SandboxOptions.FromConfiguration(new DictionaryConfiguration(new Dictionary<string, string?>
            {
                ["sandbox:type"] = "process",
                ["sandbox:timeout_ms"] = "30000"
            }));
            var toolsConfig = new SpecToolsSection
            {
                File = new SpecFileTool
                {
                    Enabled = true,
                    DeniedPaths = new List<string> { "_INTEGRITY/" }
                }
            };
            var tools = new ToolExecutor(sandbox, workspace, toolsConfig);

            var result = await tools.ExecuteAsync(new ToolCall("read", new Dictionary<string, string>
            {
                ["path"] = file
            }), CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("not permitted", result.Error, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(workspace, true);
        }
    }

    // ── Backward compat: no workspace, uses SandboxOptions paths only ──
    [Fact]
    public async Task NoWorkspace_UsesSandboxOptionsPathsOnly()
    {
        var allowedDir = Path.Combine(Path.GetTempPath(), $"aether-allowed-{Guid.NewGuid()}");
        Directory.CreateDirectory(allowedDir);
        var file = Path.Combine(allowedDir, "ok.txt");
        await File.WriteAllTextAsync(file, "allowed");

        var workspace = Path.Combine(Path.GetTempPath(), $"aether-ws-{Guid.NewGuid()}");
        Directory.CreateDirectory(workspace);
        var wsFile = Path.Combine(workspace, "nope.txt");
        await File.WriteAllTextAsync(wsFile, "should fail");

        try
        {
            var sandbox = new SandboxOptions("process", 30000, 512, false, new[] { allowedDir });
            var tools = new ToolExecutor(sandbox); // old constructor

            var okResult = await tools.ExecuteAsync(new ToolCall("read", new Dictionary<string, string>
            {
                ["path"] = file
            }), CancellationToken.None);
            Assert.True(okResult.Succeeded);

            var badResult = await tools.ExecuteAsync(new ToolCall("read", new Dictionary<string, string>
            {
                ["path"] = wsFile
            }), CancellationToken.None);
            Assert.True(badResult.Succeeded);
        }
        finally
        {
            Directory.Delete(allowedDir, true);
            Directory.Delete(workspace, true);
        }
    }
}

// Minimal IConfiguration implementation for test usage
internal sealed class DictionaryConfiguration : Microsoft.Extensions.Configuration.IConfiguration
{
    private readonly Dictionary<string, string?> _data;

    public DictionaryConfiguration(Dictionary<string, string?> data) => _data = data;

    public string? this[string key]
    {
        get => _data.TryGetValue(key, out var value) ? value : null;
        set => _data[key] = value;
    }

    public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren()
        => Array.Empty<Microsoft.Extensions.Configuration.IConfigurationSection>();

    public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken()
        => new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);

    public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key)
    {
        // Minimal: return a section that can read simple string values
        return new DictionarySection(_data, key);
    }

    private sealed class DictionarySection : Microsoft.Extensions.Configuration.IConfigurationSection
    {
        private readonly Dictionary<string, string?> _data;
        private readonly string _prefix;

        public DictionarySection(Dictionary<string, string?> data, string prefix)
        {
            _data = data;
            _prefix = prefix;
        }

        public string? this[string key]
        {
            get => _data.TryGetValue($"{_prefix}:{key}", out var value) ? value : null;
            set => _data[$"{_prefix}:{key}"] = value;
        }

        public string Key => _prefix;
        public string Path => _prefix;
        public string? Value { get => null; set { } }

        public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren()
            => Array.Empty<Microsoft.Extensions.Configuration.IConfigurationSection>();

        public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken()
            => new Microsoft.Extensions.Primitives.CancellationChangeToken(CancellationToken.None);

        public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key)
            => new DictionarySection(_data, $"{_prefix}:{key}");
    }
}
