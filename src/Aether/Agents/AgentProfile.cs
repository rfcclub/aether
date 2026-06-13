using Aether.Agent;
using Aether.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Agents;

/// <summary>
/// Đại diện cho Hồ sơ thông tin danh tính và phân vùng dữ liệu hoạt động của một Agent (AgentProfile).
/// Chịu trách nhiệm xác định tên, đường dẫn thư mục gốc, cấu hình mô hình LLM và nạp các ngữ cảnh nhận thức.
/// </summary>
public class AgentProfile
{
    private readonly AgentConfig _config;
    private ContextAssembler? _contextAssembler;

    /// <summary>
    /// Tên duy nhất định danh Agent (ví dụ: maria, vesta, coda).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Đường dẫn thư mục tuyệt đối chứa các tệp tin làm việc của Agent.
    /// </summary>
    public string AgentDirectory { get; }

    /// <summary>
    /// Cấu hình chi tiết cho LLM sử dụng riêng cho Agent này.
    /// </summary>
    public AgentModelConfig Model { get; }

    /// <summary>
    /// Tên hiển thị thân thiện của Agent (ví dụ: Maria, Aura, Vesta).
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Khởi tạo một thực thể mới của AgentProfile.
    /// </summary>
    /// <param name="name">Tên Agent.</param>
    /// <param name="agentDirectory">Đường dẫn thư mục làm việc tuyệt đối.</param>
    /// <param name="config">Cấu hình chứa danh sách startup files.</param>
    /// <param name="model">Cấu hình LLM.</param>
    /// <param name="displayName">Tên hiển thị thân thiện.</param>
    public AgentProfile(string name, string agentDirectory, AgentConfig config, AgentModelConfig model, string? displayName = null)
    {
        Name = name;
        AgentDirectory = agentDirectory;
        _config = config;
        Model = model;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    }

    /// <summary>
    /// Tạo thực thể AgentProfile từ trình nạp cấu hình hệ thống (ConfigLoader).
    /// Hỗ trợ tự động phân giải workspace động và tương thích ngược với layout thư mục legacy.
    /// </summary>
    /// <param name="name">Tên Agent cần nạp.</param>
    /// <param name="configLoader">Trình nạp cấu hình hệ thống.</param>
    /// <param name="config">Cấu hình nền.</param>
    /// <param name="logger">Trình ghi log.</param>
    /// <returns>Hồ sơ AgentProfile hoàn chỉnh.</returns>
    /// <exception cref="DirectoryNotFoundException">Ném ra nếu không tìm thấy thư mục làm việc hợp lệ.</exception>
    public static AgentProfile FromConfigLoader(
        string name,
        ConfigLoader configLoader,
        AgentConfig config,
        ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        var agentConfig = configLoader.GetAgentConfig(name);
        var newPath = agentConfig?.Workspace;
        var model = agentConfig?.Model ?? new AgentModelConfig();
        var displayName = agentConfig?.DisplayName;

        if (!string.IsNullOrEmpty(newPath) && Directory.Exists(newPath))
        {
            return new AgentProfile(name, newPath, config, model, displayName);
        }

        // Tương thích ngược: <cwd>/agents/<name>/ (legacy layout)
        var legacyPath = Path.Combine(Environment.CurrentDirectory, "agents", name);
        if (Directory.Exists(legacyPath))
        {
            logger.LogWarning("Agent '{Name}' using legacy path {Path}. Migrate to ~/.aether/workspaces/{Name}/",
                name, legacyPath, name);
            return new AgentProfile(name, legacyPath, config, model, displayName);
        }

        throw new DirectoryNotFoundException(
            $"Agent directory not found for '{name}'. " +
            $"Tried: {newPath ?? "<no workspace in config>"} and {legacyPath}");
    }

    /// <summary>
    /// Nạp và lắp ráp toàn bộ ngữ cảnh danh tính động (Identity Context) của Agent từ thư mục làm việc.
    /// </summary>
    /// <returns>Chuỗi ngữ cảnh danh tính hoàn chỉnh để làm System Prompt cho LLM.</returns>
    public virtual string LoadIdentityContext()
    {
        _contextAssembler ??= new ContextAssembler();
        return _contextAssembler.AssembleIdentityContext(AgentDirectory);
    }

    /// <summary>
    /// Nạp Persona khởi đầu của Agent (Đã lỗi thời, hãy sử dụng LoadIdentityContext thay thế).
    /// </summary>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Nội dung Persona ghép lại từ các startup files.</returns>
    [Obsolete("Use LoadIdentityContext() instead.")]
    public virtual Task<string> LoadPersonaAsync(CancellationToken ct = default)
    {
        var parts = new List<string>();
        foreach (var file in _config.StartupFiles)
        {
            var content = LoadFile(file);
            if (content is not null) parts.Add(content);
        }
        return Task.FromResult(string.Join("\n\n", parts));
    }

    /// <summary>
    /// Đọc đồng bộ nội dung của một tệp tin cục bộ nằm trong thư mục làm việc của Agent.
    /// </summary>
    /// <param name="relativePath">Đường dẫn tệp tin tương đối.</param>
    /// <returns>Nội dung tệp tin dưới dạng chuỗi, hoặc <c>null</c> nếu tệp tin không tồn tại.</returns>
    public virtual string? LoadFile(string relativePath)
    {
        var fullPath = Path.Combine(AgentDirectory, relativePath);
        if (!File.Exists(fullPath)) return null;
        return File.ReadAllText(fullPath);
    }

    /// <summary>
    /// Đọc bất đồng bộ nội dung của một tệp tin cục bộ nằm trong thư mục làm việc của Agent.
    /// </summary>
    /// <param name="relativePath">Đường dẫn tệp tin tương đối.</param>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Nội dung tệp tin dưới dạng chuỗi, hoặc <c>null</c> nếu tệp tin không tồn tại.</returns>
    public virtual async Task<string?> LoadFileAsync(string relativePath, CancellationToken ct = default)
        => LoadFile(relativePath);

    /// <summary>
    /// Nạp đồng bộ các ký ức sinh hoạt hàng ngày của Agent trong hôm nay và hôm qua để làm giàu ngữ cảnh.
    /// </summary>
    /// <returns>Nội dung ký ức hàng ngày ghép lại từ các tệp nhật ký Markdown.</returns>
    public virtual string LoadDailyMemory()
    {
        var parts = new List<string>();
        var dates = new[] { DateTime.UtcNow, DateTime.UtcNow.AddDays(-1) };
        foreach (var date in dates)
        {
            var filename = $"{date:yyyy-MM-dd}.md";
            var content = LoadFile(Path.Combine(_config.DailyMemoryDirectory, filename));
            if (content is not null) parts.Add(content);
        }
        return string.Join("\n\n", parts);
    }

    /// <summary>
    /// Nạp bất đồng bộ các ký ức sinh hoạt hàng ngày của Agent trong hôm nay và hôm qua để làm giàu ngữ cảnh.
    /// </summary>
    /// <param name="ct">Token hủy bỏ tác vụ.</param>
    /// <returns>Nội dung ký ức hàng ngày ghép lại.</returns>
    public virtual async Task<string> LoadDailyMemoryAsync(CancellationToken ct = default)
        => LoadDailyMemory();
}
