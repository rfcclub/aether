namespace Aether.Tooling;

public interface IWebSearchProvider
{
    string Name { get; }
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int limit, CancellationToken ct);
}

public record WebSearchResult(string Title, string Url, string Snippet);
