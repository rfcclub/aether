namespace Aether.Plugins;

public class AgentPluginConfig
{
    public List<string> Enabled { get; set; } = new();
    public List<string> Disabled { get; set; } = new();
    public Dictionary<string, Dictionary<string, object?>> Config { get; set; } = new();
    public Dictionary<string, int> HookOverrides { get; set; } = new();
}
