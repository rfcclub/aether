namespace Aether.Ui;

public sealed record UiCallback
{
    public string Namespace { get; init; } = "";
    public string Action { get; init; } = "";
    public string Data { get; init; } = "";
}
