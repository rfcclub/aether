using System.Text.Json;
using Aether.Scheduling;
using Aether.Skills;
using Aether.Tooling;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins;

public class PluginAssetRegistrar
{
    private readonly ToolRegistry _tools;
    private readonly SkillRegistry _skills;
    private readonly CronSchedulerService? _cron;
    private readonly ILogger<PluginAssetRegistrar> _logger;

    public PluginAssetRegistrar(
        ToolRegistry tools,
        SkillRegistry skills,
        CronSchedulerService? cron,
        ILogger<PluginAssetRegistrar> logger)
    {
        _tools = tools;
        _skills = skills;
        _cron = cron;
        _logger = logger;
    }

    public async Task RegisterAsync(
        PluginLoadResult result,
        IReadOnlyList<(PluginManifest Manifest, string Dir)> manifestPairs,
        CancellationToken ct)
    {
        // 1. IToolImplementation instances from assembly
        foreach (var impl in result.Tools)
        {
            var sandbox = new SandboxContext(Environment.CurrentDirectory);
            var def = new ToolDefinition(
                Name: impl.Name,
                Description: impl.Description,
                ParametersSchema: impl.ParametersSchema,
                Execute: (args, ct) => impl.ExecuteAsync(args, sandbox, ct),
                Risk: ToolRisk.Read,
                Enabled: true);
            _tools.Register(impl.Name, def);
            _logger.LogInformation("Plugin tool registered: {Name}", impl.Name);
        }

        // 2. Manifest-declared tools (JSON files)
        foreach (var (manifest, dir) in manifestPairs)
        {
            if (manifest.Tools is not null)
            {
                foreach (var toolDecl in manifest.Tools)
                {
                    await RegisterManifestToolAsync(manifest.Name, dir, toolDecl, ct);
                }
            }

            // 3. Manifest-declared SKILL.md files
            if (manifest.Skills is not null)
            {
                foreach (var skillDecl in manifest.Skills)
                {
                    RegisterManifestSkill(manifest.Name, dir, skillDecl);
                }
            }

            // 4. Manifest-declared cron tasks
            if (manifest.Cron is not null && _cron is not null)
            {
                foreach (var cronDecl in manifest.Cron)
                {
                    RegisterManifestCron(manifest.Name, cronDecl);
                }
            }
        }

        // 5. ISkillProvider instances from assembly
        foreach (var provider in result.SkillProviders)
        {
            foreach (var skill in provider.GetSkills())
            {
                if (!provider.ValidateSkill(skill, out var error))
                {
                    _logger.LogWarning("Skill '{Name}' from plugin provider failed validation: {Error}", skill.Name, error);
                    continue;
                }
                _skills.Register(skill);
                _logger.LogInformation("Plugin skill registered: {Name}", skill.Name);
            }
        }

        // 6. ICronTaskProvider instances from assembly
        if (_cron is not null)
        {
            foreach (var provider in result.CronProviders)
            {
                foreach (var task in provider.GetTasks())
                {
                    _cron.AddTask($"plugin:{task.FilePath}", task);
                    _logger.LogInformation("Plugin cron task registered: {Name}", task.FilePath);
                }
            }
        }
    }

    private async Task RegisterManifestToolAsync(
        string pluginName, string pluginDir, PluginToolDeclaration decl, CancellationToken ct)
    {
        var path = Path.Combine(pluginDir, decl.Definition ?? $"{decl.Name}.json");
        if (!File.Exists(path))
        {
            _logger.LogWarning("Plugin tool JSON not found: {Path}", path);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path, ct);
            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            var name = GetStringProp(root, "name") ?? decl.Name;
            var desc = GetStringProp(root, "description") ?? name;
            var schema = root.TryGetProperty("parameters", out var p) ? p : default;

            var def = new ToolDefinition(
                Name: name,
                Description: desc,
                ParametersSchema: schema.ValueKind == JsonValueKind.Object ? schema : JsonDocument.Parse("{}").RootElement,
                Execute: (_, _) => Task.FromResult<object>("Tool not implemented"),
                Risk: ToolRisk.Read,
                Enabled: true);

            _tools.Register(name, def);
            _logger.LogInformation("Plugin manifest tool registered: {Name} (from {Plugin})", name, pluginName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin tool JSON: {Path}", path);
        }
    }

    private void RegisterManifestSkill(string pluginName, string pluginDir, PluginSkillDeclaration decl)
    {
        var path = Path.Combine(pluginDir, decl.Path ?? $"{decl.Name}.md");
        if (!File.Exists(path))
        {
            _logger.LogWarning("Plugin SKILL.md not found: {Path}", path);
            return;
        }

        try
        {
            var text = File.ReadAllText(path);
            var (name, desc, whenToUse, body) = ParseSkillMarkdown(text);

            var skill = new SkillDefinition(
                Name: name ?? decl.Name,
                Description: desc ?? "",
                WhenToUse: whenToUse ?? "",
                Tools: Array.Empty<string>(),
                AutoApply: false,
                Body: body ?? text,
                TriggerMode: SkillTriggerMode.Both);

            _skills.Register(skill);
            _logger.LogInformation("Plugin manifest skill registered: {Name} (from {Plugin})", skill.Name, pluginName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin SKILL.md: {Path}", path);
        }
    }

    private void RegisterManifestCron(string pluginName, PluginCronDeclaration decl)
    {
        if (_cron is null) return;

        var task = new CronTaskDefinition(
            Schedule: decl.Schedule ?? "0 0 * * *",
            Agent: "",
            Channel: "",
            Enabled: true,
            Body: decl.Task ?? "",
            FilePath: $"{pluginName}/{decl.Name}");

        _cron.AddTask($"plugin:{pluginName}/{decl.Name}", task);
        _logger.LogInformation("Plugin manifest cron registered: {Name} (schedule: {Schedule})",
            decl.Name, task.Schedule);
    }

    // ── Helpers ──

    private static string? GetStringProp(JsonElement el, string key)
        => el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString() : null;

    private static (string? name, string? desc, string? whenToUse, string? body) ParseSkillMarkdown(string markdown)
    {
        string? name = null, desc = null, whenToUse = null, body = null;

        // Parse YAML-like frontmatter between --- delimiters
        if (markdown.StartsWith("---"))
        {
            var endIdx = markdown.IndexOf("---", 3);
            if (endIdx > 0)
            {
                var frontmatter = markdown[3..endIdx];
                foreach (var line in frontmatter.Split('\n'))
                {
                    var colon = line.IndexOf(':');
                    if (colon < 0) continue;

                    var key = line[..colon].Trim().ToLowerInvariant();
                    var value = line[(colon + 1)..].Trim();

                    switch (key)
                    {
                        case "name": name = value; break;
                        case "description": desc = value; break;
                        case "whentouse": whenToUse = value; break;
                    }
                }
                body = markdown[(endIdx + 3)..].Trim();
            }
        }

        return (name, desc, whenToUse, body ?? markdown);
    }
}
