namespace Aether.Ui;

public sealed record UiDocument
{
    public string Text { get; init; } = "";
    public List<UiSection> Sections { get; init; } = new();
    public UiLayout Layout { get; init; } = UiLayout.List;
    public string CallbackNamespace { get; init; } = "";
    /// <summary>Opaque context string passed through to page callbacks (e.g., provider name).</summary>
    public string PageContext { get; init; } = "";
    public int PageIndex { get; init; }
    public int TotalPages { get; init; } = 1;
}

public sealed record UiSection
{
    public string Title { get; init; } = "";
    public string? Subtitle { get; init; }
    public List<UiItem> Items { get; init; } = new();
}

public sealed record UiItem
{
    public string Id { get; init; } = "";
    public string Label { get; init; } = "";
    public string? Emoji { get; init; }
    public bool Selected { get; init; }
    public string? StatusBadge { get; init; }
}

public enum UiLayout { List, Grid, Paged }
