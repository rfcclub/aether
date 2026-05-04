using Microsoft.Extensions.Logging;

namespace Aether.Workspace;

public sealed class AgentWorkspaceScaffolder
{
    private readonly ILogger<AgentWorkspaceScaffolder> _logger;

    public AgentWorkspaceScaffolder(ILogger<AgentWorkspaceScaffolder> logger)
    {
        _logger = logger;
    }

    public async Task ScaffoldAsync(string name, string workspacePath, bool interactive)
    {
        Directory.CreateDirectory(workspacePath);

        await WriteIfMissingAsync(Path.Combine(workspacePath, "SOUL.md"), SoulTemplate(name));
        await WriteIfMissingAsync(Path.Combine(workspacePath, "USER.md"), UserTemplate(name));
        await WriteIfMissingAsync(Path.Combine(workspacePath, "IDENTITY.md"), IdentityTemplate(name));
        await WriteIfMissingAsync(Path.Combine(workspacePath, "MEMORY.md"), MemoryTemplate());
        await WriteIfMissingAsync(Path.Combine(workspacePath, "HEARTBEAT.md"), HeartbeatTemplate());
        await WriteIfMissingAsync(Path.Combine(workspacePath, "AGENTS_GUARD.md"), AgentsGuardTemplate());
        await WriteIfMissingAsync(Path.Combine(workspacePath, "DREAMS.md"), DreamsTemplate());
        await WriteIfMissingAsync(Path.Combine(workspacePath, "INTROSPECTION.md"), IntrospectionTemplate());
        await WriteIfMissingAsync(Path.Combine(workspacePath, "TASK_INBOX.md"), TaskInboxTemplate());
        await WriteIfMissingAsync(Path.Combine(workspacePath, "TASK_REPORT.md"), TaskReportTemplate());
        await WriteIfMissingAsync(Path.Combine(workspacePath, ".aether.json"), AetherConfigTemplate());

        var memoryDir = Path.Combine(workspacePath, "memory");
        Directory.CreateDirectory(memoryDir);
    }

    private async Task WriteIfMissingAsync(string path, string content)
    {
        if (File.Exists(path))
        {
            _logger.LogDebug("Skipping {Path}, already exists", path);
            return;
        }

        await File.WriteAllTextAsync(path, content);
    }

    private static string SoulTemplate(string name) =>
        $"# SOUL — {name}\n\n" +
        "## Tone\n\n" +
        "Define the agent's voice and tone here.\n\n" +
        "## Address\n\n" +
        "How the agent addresses the user and others.\n\n" +
        "## Rules\n\n" +
        "Core behavioral rules and constraints.\n\n" +
        "## Memory\n\n" +
        "What the agent remembers and how it recalls.\n";

    private static string UserTemplate(string name) =>
        $"# USER — {name}\n\n" +
        "## Name\n\n" +
        "## What to call them\n\n" +
        "## Timezone\n\n" +
        "UTC\n\n" +
        "## Notes\n\n";

    private static string IdentityTemplate(string name) =>
        $"# IDENTITY — {name}\n\n" +
        "## Name\n\n{name}\n\n" +
        "## Creature\n\n" +
        "What kind of entity is this agent?\n\n" +
        "## Vibe\n\n" +
        "## Emoji\n\n" +
        "## Exposure Classification\n\n" +
        "Internal / Restricted / Public\n\n" +
        "## Conflict Engine\n\n" +
        "How this agent handles conflicts.\n";

    private static string MemoryTemplate() =>
        "# MEMORY\n\n" +
        "## User\n\n" +
        "## Agent Context\n\n" +
        "## Multi-Agent Ecosystem\n\n";

    private static string HeartbeatTemplate() =>
        "# HEARTBEAT\n\n" +
        "- [ ] Check TASK_INBOX.md\n\n" +
        "HEARTBEAT_OK\n";

    private static string AgentsGuardTemplate() =>
        "# AGENT'S GUARD\n\n" +
        "## Configuration Isolation\n\n" +
        "## Red Lines\n\n" +
        "## Anti-Hang\n\n" +
        "## State Recovery\n\n";

    private static string DreamsTemplate() =>
        "# DREAMS\n\n";

    private static string IntrospectionTemplate() =>
        "# INTROSPECTION\n\n";

    private static string TaskInboxTemplate() =>
        "# TASK INBOX\n\n";

    private static string TaskReportTemplate() =>
        "# TASK REPORT\n\n";

    private static string AetherConfigTemplate() =>
        "{\n" +
        "  \"model\": {\n" +
        "    \"primary\": null,\n" +
        "    \"fallbacks\": []\n" +
        "  },\n" +
        "  \"heartbeat\": {\n" +
        "    \"intervalMinutes\": 60\n" +
        "  },\n" +
        "  \"kairos\": {\n" +
        "    \"enabled\": false,\n" +
        "    \"rules\": [\n" +
        "      { \"watch\": \"research/*.md\", \"channel\": \"telegram\", \"cooldownSeconds\": 300 },\n" +
        "      { \"watch\": \"TASK_REPORT.md\", \"channel\": \"telegram\", \"cooldownSeconds\": 120 }\n" +
        "    ]\n" +
        "  }\n" +
        "}\n";
}
