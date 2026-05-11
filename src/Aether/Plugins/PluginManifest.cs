using System.Text.Json.Serialization;

namespace Aether.Plugins;

public sealed record PluginManifest
{
    // Identity
    public string Name { get; init; } = "";
    public string Version { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DisplayName { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Author { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? License { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Homepage { get; init; }

    // Assembly
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Assembly { get; init; }

    // Components
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PluginHookDeclaration>? Hooks { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PluginToolDeclaration>? Tools { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PluginSkillDeclaration>? Skills { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PluginChannelDeclaration>? Channels { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<PluginCronDeclaration>? Cron { get; init; }

    // Metadata
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? Dependencies { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PluginPermissions? Permissions { get; init; }
}

public sealed record PluginHookDeclaration
{
    public string Class { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Points { get; init; }

    public int Priority { get; init; } = 50;
}

public sealed record PluginToolDeclaration
{
    public string Name { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Definition { get; init; }
}

public sealed record PluginSkillDeclaration
{
    public string Name { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
}

public sealed record PluginChannelDeclaration
{
    public string Class { get; init; } = "";
}

public sealed record PluginCronDeclaration
{
    public string Name { get; init; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Schedule { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Task { get; init; }
}

public sealed record PluginPermissions
{
    public bool Network { get; init; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Filesystem { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Tools { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Channels { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Services { get; init; }

    public static PluginPermissions Default => new()
    {
        Network = false,
        Filesystem = new() { "plugins/{self}/**" },
        Tools = new(),
        Channels = new(),
        Services = new()
    };
}
