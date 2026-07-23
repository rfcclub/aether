using System.Text.Json;

namespace Aether.Channels;

/// <summary>
/// Static bridge giving WebSocketChannel HTTP routes access to MariaMemory store
/// without tight coupling to the plugin assembly. Search/getall/append delegates
/// are registered here by the plugin lifecycle (wired in Program.cs after plugin load).
/// </summary>
public static class MariaMemoryHost
{
    public static Func<string, int, CancellationToken, Task<JsonElement>>? SearchHandler { get; set; }
    public static Func<int, CancellationToken, Task<JsonElement>>? GetAllHandler { get; set; }
    public static Func<string, CancellationToken, Task<JsonElement>>? AppendHandler { get; set; }

    public static async Task<JsonElement> SearchAsync(string query, int limit, CancellationToken ct)
    {
        if (SearchHandler is null) return JsonSerializer.SerializeToElement(new { success = true, nodes = Array.Empty<object>() });
        return await SearchHandler(query, limit, ct);
    }

    public static async Task<JsonElement> GetAllNodesAsync(int limit, CancellationToken ct)
    {
        if (GetAllHandler is null) return JsonSerializer.SerializeToElement(new { success = true, nodes = Array.Empty<object>() });
        return await GetAllHandler(limit, ct);
    }

    public static async Task<JsonElement> AppendNodeAsync(string body, CancellationToken ct)
    {
        if (AppendHandler is null) return JsonSerializer.SerializeToElement(new { success = false, error = "MariaMemory plugin not loaded" });
        return await AppendHandler(body, ct);
    }
}
