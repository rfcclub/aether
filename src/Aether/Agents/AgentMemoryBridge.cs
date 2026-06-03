namespace Aether.Agents;

/// <summary>
/// Cầu nối ký ức của Agent (AgentMemoryBridge).
/// Đóng vai trò liên kết và quản lý cấu trúc tệp tin ký ức định dạng OC (daily transcripts, MEMORY.md, task inbox/report).
/// Hỗ trợ cho FileMemory (sử dụng SQLite/FTS5) bằng cơ chế lưu trữ dạng tệp tin Markdown dễ đọc, dễ kiểm soát.
/// </summary>
public sealed class AgentMemoryBridge
{
    private readonly string _agentDir;
    private readonly AgentConfig _config;

    /// <summary>
    /// Khởi tạo cầu nối ký ức với thư mục Agent và cấu hình tương ứng.
    /// </summary>
    /// <param name="agentDir">Thư mục làm việc và lưu trữ của Agent.</param>
    /// <param name="config">Cấu hình định nghĩa đường dẫn tệp tin ký ức.</param>
    public AgentMemoryBridge(string agentDir, AgentConfig config)
    {
        _agentDir = agentDir;
        _config = config;
    }

    /// <summary>
    /// Ghi thêm nội dung hội thoại/trải nghiệm mới vào tệp nhật ký ký ức hàng ngày (daily memory).
    /// </summary>
    /// <param name="content">Nội dung tóm tắt ký ức cần lưu trữ.</param>
    /// <param name="sessionId">Định danh phiên làm việc (Session ID) tương ứng.</param>
    /// <returns>Một tác vụ đại diện cho quá trình append tệp tin.</returns>
    public async Task AppendDailyMemoryAsync(string content, string sessionId)
    {
        var dailyDir = Path.Combine(_agentDir, _config.DailyMemoryDirectory);
        Directory.CreateDirectory(dailyDir);

        var filename = $"{DateTime.UtcNow:yyyy-MM-dd}.md";
        var filePath = Path.Combine(dailyDir, filename);

        var entry = $"\n## {DateTime.UtcNow:HH:mm:ss} UTC | session: {sessionId}\n\n{content}\n";
        await File.AppendAllTextAsync(filePath, entry);
    }

    /// <summary>
    /// Đọc nội dung tệp tin ký ức dài hạn (MEMORY.md).
    /// </summary>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Nội dung ký ức dài hạn dưới dạng chuỗi, hoặc chuỗi rỗng nếu tệp tin không tồn tại.</returns>
    public async Task<string> ReadLongTermMemoryAsync(CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, _config.LongTermMemoryFile);
        if (!File.Exists(filePath))
            return string.Empty;
        return await File.ReadAllTextAsync(filePath, ct);
    }

    /// <summary>
    /// Ghi đè nội dung mới lên tệp tin ký ức dài hạn (MEMORY.md).
    /// </summary>
    /// <param name="content">Nội dung ký ức dài hạn mới cần lưu trữ.</param>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Một tác vụ đại diện cho quá trình ghi file.</returns>
    public async Task WriteLongTermMemoryAsync(string content, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_agentDir, _config.LongTermMemoryFile);
        await File.WriteAllTextAsync(filePath, content, ct);
    }

    /// <summary>
    /// Đọc danh sách tác vụ từ tệp Hộp thư công việc (TASK_INBOX.md).
    /// </summary>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Danh sách tác vụ dạng văn bản Markdown.</returns>
    public async Task<string> ReadTaskInboxAsync(CancellationToken ct = default)
    {
        if (_config.TaskInboxFile is null)
            return string.Empty;
        var filePath = Path.Combine(_agentDir, _config.TaskInboxFile);
        if (!File.Exists(filePath))
            return string.Empty;
        return await File.ReadAllTextAsync(filePath, ct);
    }

    /// <summary>
    /// Ghi báo cáo kết quả thực thi các tác vụ lên tệp tin TASK_REPORT.md.
    /// </summary>
    /// <param name="content">Nội dung báo cáo chi tiết.</param>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Một tác vụ đại diện cho quá trình ghi file.</returns>
    public async Task WriteTaskReportAsync(string content, CancellationToken ct = default)
    {
        if (_config.TaskReportFile is null)
            return;
        var filePath = Path.Combine(_agentDir, _config.TaskReportFile);
        await File.WriteAllTextAsync(filePath, content, ct);
    }

    /// <summary>
    /// Đọc tệp tin ghi nhận các ý tưởng/giấc mơ tự phát triển (DREAMS.md) của Agent.
    /// </summary>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Nội dung giấc mơ dạng văn bản.</returns>
    public async Task<string> ReadDreamsAsync(CancellationToken ct = default)
    {
        if (_config.Boot is null) return string.Empty;
        var filePath = Path.Combine(_agentDir, _config.Boot.DreamsFile);
        if (!File.Exists(filePath)) return string.Empty;
        return await File.ReadAllTextAsync(filePath, ct);
    }

    /// <summary>
    /// Ghi thêm một giấc mơ hoặc bài học tự đúc kết mới vào tệp DREAMS.md.
    /// </summary>
    /// <param name="content">Nội dung ý tưởng/giấc mơ cần lưu trữ.</param>
    /// <returns>Một tác vụ đại diện cho quá trình append tệp tin.</returns>
    public async Task AppendDreamAsync(string content)
    {
        if (_config.Boot is null) return;
        var filePath = Path.Combine(_agentDir, _config.Boot.DreamsFile);
        var entry = $"\n---\n\n*{DateTime.UtcNow:MMMM dd, yyyy 'at' h:mm tt}*\n\n{content}\n";
        await File.AppendAllTextAsync(filePath, entry);
    }

    /// <summary>
    /// Lấy danh sách các tệp tin ký ức hàng ngày trong thư mục lưu trữ của Agent.
    /// </summary>
    /// <param name="since">Khoảng thời gian bắt đầu lọc tệp tin dựa trên thời gian chỉnh sửa cuối.</param>
    /// <returns>Danh sách các đường dẫn tệp tin Markdown lưu trữ nhật ký ký ức, sắp xếp giảm dần theo thời gian.</returns>
    public IReadOnlyList<string> GetMemoryFiles(DateTime? since = null)
    {
        var dailyDir = Path.Combine(_agentDir, _config.DailyMemoryDirectory);
        if (!Directory.Exists(dailyDir)) return Array.Empty<string>();
        return Directory.GetFiles(dailyDir, "*.md")
            .Where(f => !since.HasValue || File.GetLastWriteTimeUtc(f) >= since.Value)
            .OrderByDescending(f => f)
            .ToList();
    }
}
