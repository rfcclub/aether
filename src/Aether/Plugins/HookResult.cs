namespace Aether.Plugins;

public readonly struct HookResult
{
    public bool Success { get; init; }
    public string? StopReason { get; init; }

    public static HookResult Continue => new() { Success = true };
    public static HookResult Stop(string reason) => new() { Success = false, StopReason = reason };
}
