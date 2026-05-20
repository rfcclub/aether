using System.Text.Json.Serialization;

namespace Aether.Plugins.MariaMemory.Models;

public sealed record MemoryNode
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString();

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    [JsonPropertyName("role")]
    public string Role { get; init; } = ""; // "user", "assistant", "insight"

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";

    [JsonPropertyName("tags")]
    public List<string> Tags { get; init; } = new();

    [JsonPropertyName("source")]
    public string Source { get; init; } = "aether";

    [JsonPropertyName("day_key")]
    public string DayKey { get; init; } = DateTime.UtcNow.ToString("yyyy-MM-dd");

    [JsonPropertyName("thread_id")]
    public string ThreadId { get; init; } = "";

    [JsonPropertyName("weight")]
    public float Weight { get; init; } = 0.5f;

    [JsonPropertyName("score")]
    public float Score { get; set; } = 0.0f;

    [JsonPropertyName("recall_count")]
    public int RecallCount { get; set; } = 0;

    [JsonPropertyName("is_promoted")]
    public bool IsPromoted { get; set; } = false;
}
