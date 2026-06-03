namespace Aether.Agents;

/// <summary>
/// Đại diện cho cấu hình khởi động và vận hành của một Agent cụ thể trong hệ thống Aether.
/// Định nghĩa các đường dẫn tệp tin và thư mục lưu trữ ngữ cảnh cơ bản.
/// </summary>
public record AgentConfig
{
    /// <summary>
    /// Danh sách các tệp tin cấu hình khởi động mặc định (ví dụ: AGENTS.md).
    /// </summary>
    public List<string> StartupFiles { get; init; } = new() { "AGENTS.md" };

    /// <summary>
    /// Tên tệp tin lưu trữ ký ức dài hạn (mặc định: MEMORY.md).
    /// </summary>
    public string LongTermMemoryFile { get; init; } = "MEMORY.md";

    /// <summary>
    /// Đường dẫn tệp tin ghi nhận nhịp tim sinh học và trạng thái hoạt động (mặc định: HEARTBEAT.md).
    /// </summary>
    public string? HeartbeatFile { get; init; } = "HEARTBEAT.md";

    /// <summary>
    /// Thư mục lưu trữ ký ức hàng ngày của Agent (mặc định: memory).
    /// </summary>
    public string DailyMemoryDirectory { get; init; } = "memory";

    /// <summary>
    /// Đường dẫn tệp tin Hộp thư chứa danh sách các tác vụ cần thực hiện (mặc định: TASK_INBOX.md).
    /// </summary>
    public string? TaskInboxFile { get; init; } = "TASK_INBOX.md";

    /// <summary>
    /// Đường dẫn tệp tin báo cáo kết quả thực thi các tác vụ (mặc định: TASK_REPORT.md).
    /// </summary>
    public string? TaskReportFile { get; init; } = "TASK_REPORT.md";

    /// <summary>
    /// Cấu hình chi tiết cho quá trình Boot (khởi động) kiến trúc nhận thức FEOFALLS.
    /// </summary>
    public BootConfig? Boot { get; init; }
}

/// <summary>
/// Đại diện cho cấu hình chi tiết phục vụ quá trình khởi chạy nhận thức và dọn dẹp ký ức của Agent.
/// </summary>
public record BootConfig
{
    /// <summary>
    /// Danh sách các tệp tin bảo vệ hiến pháp của Agent (Đã lỗi thời, sử dụng ContextAssembler thay thế).
    /// </summary>
    [Obsolete("ConstitutionFiles is deprecated. Use ContextAssembler for dynamic file discovery instead.")]
    public List<string> ConstitutionFiles { get; init; } = new() { "AGENTS_GUARD.md" };

    /// <summary>
    /// Danh sách các tệp tin danh tính cốt lõi (Đã lỗi thời, sử dụng ContextAssembler thay thế).
    /// </summary>
    [Obsolete("IdentityFiles is deprecated. Use ContextAssembler for dynamic file discovery instead.")]
    public List<string> IdentityFiles { get; init; } = new() { "SOUL.md", "USER.md", "IDENTITY.md" };

    /// <summary>
    /// Danh sách các tệp tin nhận thức hỗ trợ (Đã lỗi thời, sử dụng ContextAssembler thay thế).
    /// </summary>
    [Obsolete("CognitiveFiles is deprecated. Use ContextAssembler for dynamic file discovery instead.")]
    public List<string> CognitiveFiles { get; init; } = new() { "MEMORY.md" };

    /// <summary>
    /// Tệp tin nhật ký ghi chép quá trình tự suy ngẫm nội tâm của Agent (mặc định: INTROSPECTION.md).
    /// </summary>
    public string EpisodicLogFile { get; init; } = "INTROSPECTION.md";

    /// <summary>
    /// Tệp tin ghi nhận các sai lầm và bài học kinh nghiệm để Agent sửa đổi hành vi (mặc định: MEMORY.md).
    /// </summary>
    public string MistakesFile { get; init; } = "MEMORY.md";

    /// <summary>
    /// Tệp tin lưu trữ các ý tưởng, giấc mơ phát triển sáng tạo của Agent (mặc định: DREAMS.md).
    /// </summary>
    public string DreamsFile { get; init; } = "DREAMS.md";

    /// <summary>
    /// Thư mục chứa các ý tưởng và giấc mơ ứng viên đang trong quá trình ủ mầm (mặc định: memory/dreaming).
    /// </summary>
    public string CandidatesDirectory { get; init; } = "memory/dreaming";

    /// <summary>
    /// Tệp tin hộp thư tác vụ (mặc định: TASK_INBOX.md).
    /// </summary>
    public string? TaskInboxFile { get; init; } = "TASK_INBOX.md";

    /// <summary>
    /// Tệp tin báo cáo kết quả thực thi tác vụ (mặc định: TASK_REPORT.md).
    /// </summary>
    public string? TaskReportFile { get; init; } = "TASK_REPORT.md";

    /// <summary>
    /// Tệp nhịp tim sinh học (mặc định: HEARTBEAT.md).
    /// </summary>
    public string? HeartbeatFile { get; init; } = "HEARTBEAT.md";

    /// <summary>
    /// Số ngày để một ký ức chuyển từ trạng thái Hoạt động (Active) sang Phai màu (Decaying) (mặc định: 60 ngày).
    /// </summary>
    public int ActiveToDecayingDays { get; init; } = 60;

    /// <summary>
    /// Số ngày để một ký ức chuyển từ trạng thái Phai màu (Decaying) sang Lưu trữ dài hạn (Archived) (mặc định: 90 ngày).
    /// </summary>
    public int DecayingToArchivedDays { get; init; } = 90;

    /// <summary>
    /// Hợp nhất các danh sách tệp tin cũ đã lỗi thời thành một tập hợp duy nhất để duy trì tính tương thích ngược.
    /// </summary>
    /// <returns>Một HashSet chứa các tên file không phân biệt chữ hoa chữ thường.</returns>
#pragma warning disable CS0618
    public HashSet<string> GetLegacyFileSet()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in ConstitutionFiles) files.Add(f);
        foreach (var f in IdentityFiles) files.Add(f);
        foreach (var f in CognitiveFiles) files.Add(f);
        return files;
    }
#pragma warning restore CS0618
}
