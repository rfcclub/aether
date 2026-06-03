namespace Aether.Agents;

/// <summary>
/// Máy ghi nhật ký tập phim trải nghiệm (EpisodicLogger).
/// Chịu trách nhiệm ghi chép các tập phim sự kiện, sai lầm, và bài học đúc kết được trong phiên làm việc của Agent
/// vào phân vùng lưu trữ lâu dài của tầng 3_LEARNING theo cấu trúc dữ liệu chuẩn hóa (Canonical schema).
/// </summary>
public sealed class EpisodicLogger
{
    private readonly string _agentDir;
    private readonly BootConfig _config;
    private int _sequence;

    /// <summary>
    /// Khởi tạo EpisodicLogger cho Agent cụ thể.
    /// </summary>
    /// <param name="agentDir">Thư mục gốc làm việc của Agent.</param>
    /// <param name="config">Cấu hình chứa các đường dẫn lưu nhật ký sai lầm và tập phim.</param>
    public EpisodicLogger(string agentDir, BootConfig config)
    {
        _agentDir = agentDir;
        _config = config;
    }

    /// <summary>
    /// Ghi nhận và lưu trữ bất đồng bộ một sự kiện/tập phim trải nghiệm (Episode) mới của Agent.
    /// </summary>
    /// <param name="sessionId">Định danh phiên làm việc xảy ra sự kiện.</param>
    /// <param name="actor">Đối tượng thực hiện hành động chính.</param>
    /// <param name="summary">Bản tóm tắt mô tả diễn biến sự kiện.</param>
    /// <param name="tags">Các từ khóa/nhãn hỗ trợ phân loại và tìm kiếm.</param>
    /// <returns>Mã định danh duy nhất (Memory ID) được sinh cho tập phim vừa lưu.</returns>
    public Task<string> AppendEpisodeAsync(string sessionId, string actor, string summary,
        Dictionary<string, string>? tags = null)
    {
        return AppendEntryAsync(_config.EpisodicLogFile, "episode", sessionId, summary, tags);
    }

    /// <summary>
    /// Ghi nhận bất đồng bộ một sai sót, lỗi lầm (Mistake) hoặc hành vi sai lệch để Agent ghi nhớ và cải tiến bản thân.
    /// </summary>
    /// <param name="sessionId">Định danh phiên làm việc xảy ra sai lầm.</param>
    /// <param name="summary">Mô tả chi tiết sai sót và bài học đúc kết để không tái phạm.</param>
    /// <param name="tags">Các nhãn phân loại lỗi.</param>
    /// <returns>Mã định danh duy nhất (Memory ID) được sinh cho sai lầm vừa lưu.</returns>
    public Task<string> AppendMistakeAsync(string sessionId, string summary,
        Dictionary<string, string>? tags = null)
    {
        return AppendEntryAsync(_config.MistakesFile, "mistake", sessionId, summary, tags);
    }

    /// <summary>
    /// Phương thức cốt lõi nội bộ để định dạng và ghi thêm dữ liệu có cấu trúc YAML Frontmatter vào tệp tin chỉ định.
    /// </summary>
    /// <param name="relativePath">Đường dẫn tệp tin tương đối cần ghi.</param>
    /// <param name="type">Kiểu thực thể ký ức (episode hoặc mistake).</param>
    /// <param name="sessionId">Phiên làm việc.</param>
    /// <param name="summary">Nội dung chi tiết.</param>
    /// <param name="tags">Các thẻ tag phân loại.</param>
    /// <returns>Mã định danh ký ức duy nhất được sinh tự động.</returns>
    private async Task<string> AppendEntryAsync(string relativePath, string type, string sessionId,
        string summary, Dictionary<string, string>? tags)
    {
        var date = DateTime.UtcNow;
        var seq = Interlocked.Increment(ref _sequence);
        var id = $"mem_{date:yyyyMMdd}_{seq:D3}";

        var tagStr = tags is { Count: > 0 }
            ? string.Join(", ", tags.Select(kv => kv.Key))
            : "";

        var entry = $"""

---
id: {id}
type: {type}
source: session
session: {sessionId}
timestamp: {date:O}
confidence: 0.50
evidence_count: 1
tags: [{tagStr}]
links: []
status: candidate
---
{summary}

""";

        var fullPath = Path.Combine(_agentDir, relativePath);
        await File.AppendAllTextAsync(fullPath, entry);
        return id;
    }
}
