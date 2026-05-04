namespace Aether.Scheduling;

public record KairosRule(
    string Watch,
    string Channel,
    int CooldownSeconds = 300
);

public record KairosConfig(
    bool Enabled = false,
    List<KairosRule>? Rules = null
);
