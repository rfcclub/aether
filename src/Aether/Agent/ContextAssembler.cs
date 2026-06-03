using System.Text;

namespace Aether.Agent;

/// <summary>
/// Bộ lắp ráp ngữ cảnh nhận thức (ContextAssembler).
/// Chịu trách nhiệm phát hiện, nạp, và hợp nhất các tệp tin cấu hình cốt lõi của Agent (như AGENTS.md, SOUL.md, IDENTITY.md, MEMORY.md)
/// cùng danh mục tệp tin trong workspace để tạo nên một System Prompt và Working Context đồng bộ, gọn gàng cho mô hình LLM.
/// </summary>
public sealed class ContextAssembler
{
    private static readonly Dictionary<string, int> BootstrapFileOrder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AGENTS.md"] = 10,
        ["SOUL.md"] = 20,
        ["IDENTITY.md"] = 30,
        ["USER.md"] = 40,
        ["MEMORY.md"] = 50,
        ["HEARTBEAT.md"] = 60,
    };

    private static readonly HashSet<string> BootstrapFiles =
        new(BootstrapFileOrder.Keys, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Chỉ các thư mục này mới được quét và hiển thị nội dung tệp tin trong danh mục Workspace.
    /// </summary>
    private static readonly HashSet<string> AllowedDirectories =
        new(StringComparer.OrdinalIgnoreCase) { "memory", "skills" };

    /// <summary>
    /// Giới hạn số ngày lùi lại khi quét tệp tin nhật ký ký ức hàng ngày (daily memory) để tránh làm loãng ngữ cảnh.
    /// </summary>
    private const int MemoryLookbackDays = 2;

    private readonly int _dynamicTokenBudget;

    /// <summary>
    /// Khởi tạo một thực thể của bộ lắp ráp ngữ cảnh ContextAssembler.
    /// </summary>
    /// <param name="dynamicTokenBudget">Ngân sách token tối đa cho phép của ngữ cảnh động (mặc định: 4000).</param>
    public ContextAssembler(int dynamicTokenBudget = 4000)
    {
        _dynamicTokenBudget = dynamicTokenBudget;
    }

    /// <summary>
    /// Lắp ráp ngữ cảnh danh tính tĩnh và toàn diện của Agent (Identity Context).
    /// Quét các tệp tin danh tính khởi động mặc định, ghép nội dung của chúng và đính kèm sơ đồ tệp tin hiện có trong workspace.
    /// </summary>
    /// <param name="agentDir">Thư mục làm việc tuyệt đối của Agent.</param>
    /// <returns>Chuỗi ngữ cảnh danh tính hoàn chỉnh để tiêm vào System Prompt.</returns>
    public string AssembleIdentityContext(string agentDir)
    {
        var files = DiscoverBootstrapFiles(agentDir);
        if (files.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## Project Context");
        sb.AppendLine();
        sb.AppendLine("The following files define your identity, behavior, and memory.");
        sb.AppendLine("They are loaded by Aether and included below. Embody them fully.");
        sb.AppendLine();

        foreach (var (path, content) in files)
        {
            sb.AppendLine($"### {path}");
            sb.AppendLine(content);
            sb.AppendLine();
        }

        // Bổ sung sơ đồ tệp tin của Workspace để LLM biết những tệp tin phụ trợ nào đang tồn tại và có thể chủ động đọc
        var listing = DiscoverWorkspaceContents(agentDir);
        if (!string.IsNullOrEmpty(listing))
        {
            sb.AppendLine(listing);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Phát hiện các thư mục và tệp tin .md trong workspace (loại trừ các tệp khởi động cốt lõi)
    /// để lập sơ đồ mục lục ngắn gọn hiển thị cho Agent.
    /// </summary>
    /// <param name="agentDir">Thư mục làm việc của Agent.</param>
    /// <returns>Sơ đồ mục lục dạng văn bản Markdown.</returns>
    private static string DiscoverWorkspaceContents(string agentDir)
    {
        var sb = new StringBuilder();
        var hasContent = false;

        try
        {
            // Duyệt qua các thư mục con chứa các tệp tin *.md
            foreach (var dir in Directory.GetDirectories(agentDir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith(".")) continue; // Bỏ qua thư mục ẩn
                if (!AllowedDirectories.Contains(dirName)) continue;

                var mdFiles = Directory.GetFiles(dir, "*.md", SearchOption.TopDirectoryOnly)
                    .Where(f => !Path.GetFileName(f).StartsWith("."))
                    .Where(f => IsRecentEnough(f, dirName))
                    .Select(Path.GetFileName)
                    .Where(f => f != null)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (mdFiles.Count == 0) continue;

                if (!hasContent)
                {
                    sb.AppendLine("## Workspace Contents");
                    sb.AppendLine();
                    sb.AppendLine("The following directories and files exist in your workspace.");
                    sb.AppendLine("Files listed above are already loaded. For everything below, use the `read` tool to access them.");
                    sb.AppendLine();
                    hasContent = true;
                }

                sb.AppendLine($"### {dirName}/ ({mdFiles.Count} files)");
                sb.AppendLine(string.Join(", ", mdFiles!));
                sb.AppendLine();
            }
        }
        catch (Exception)
        {
            // Bỏ qua thầm lặng nếu thư mục không thể truy cập
        }

        return hasContent ? sb.ToString() : string.Empty;
    }

    /// <summary>
    /// Quét và nạp nội dung của các tệp tin khởi động cốt lõi tồn tại trong thư mục của Agent,
    /// tự động sắp xếp theo thứ tự ưu tiên tối ưu.
    /// </summary>
    private static List<(string Path, string Content)> DiscoverBootstrapFiles(string agentDir)
    {
        var found = new List<(string Path, string Content)>();

        foreach (var fileName in BootstrapFiles)
        {
            var fullPath = Path.Combine(agentDir, fileName);
            if (!File.Exists(fullPath)) continue;

            var content = File.ReadAllText(fullPath);
            if (string.IsNullOrWhiteSpace(content)) continue;

            found.Add((fileName, content));
        }

        // Sắp xếp các tệp tin theo thứ tự ưu tiên
        found.Sort((a, b) =>
        {
            var orderA = BootstrapFileOrder.GetValueOrDefault(a.Path, 99);
            var orderB = BootstrapFileOrder.GetValueOrDefault(b.Path, 99);
            return orderA.CompareTo(orderB);
        });

        return found;
    }

    /// <summary>
    /// Lắp ráp ngữ cảnh động thay đổi liên tục theo lượt hội thoại (Working State, Recent Memory, Group Context).
    /// Tự động thực hiện thu gọn và cắt tỉa (Context Compaction) để đảm bảo không vượt quá ngân sách Token của mô hình.
    /// </summary>
    /// <param name="workingState">Trạng thái công việc hiện tại (ví dụ: TASK_INBOX.md và HEARTBEAT.md).</param>
    /// <param name="recentMemory">Nhật ký ký ức ngắn hạn hoặc các thông tin tham chiếu gần đây.</param>
    /// <param name="groupContext">Ngữ cảnh trao đổi nhóm hoặc định tuyến agent.</param>
    /// <returns>Ngữ cảnh động hoàn chỉnh đã được kiểm soát Token chặt chẽ.</returns>
    public string AssembleDynamicContext(
        string? workingState = null,
        string? recentMemory = null,
        string? groupContext = null)
    {
        var hasInputs = !string.IsNullOrWhiteSpace(workingState) ||
                         !string.IsNullOrWhiteSpace(recentMemory) ||
                         !string.IsNullOrWhiteSpace(groupContext);

        if (!hasInputs)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(workingState))
        {
            sb.AppendLine("## Working State");
            sb.AppendLine(workingState);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(recentMemory))
        {
            sb.AppendLine("## Recent Memory");
            sb.AppendLine(recentMemory);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(groupContext))
        {
            sb.AppendLine("## Group Context");
            sb.AppendLine(groupContext);
            sb.AppendLine();
        }

        sb.AppendLine("## State Injection (Context Compaction)");
        sb.AppendLine("When you finish a major task, resolve a deep discussion, or reach a narrative closure, you MUST extract the core outcome or 'Tension' and use a tool to write it directly to `MEMORY.md` or your substrate state (e.g. `2B/MEMBRANE_STATE.md`). The session history is ephemeral and will be compacted. Only data in static files will survive.");
        sb.AppendLine();

        var result = sb.ToString();

        // Kiểm soát ngân sách token động để tránh tràn bộ nhớ đệm
        if (_dynamicTokenBudget > 0 && EstimateTokens(result) > _dynamicTokenBudget)
            result = TruncateToTokenBudget(result, _dynamicTokenBudget);

        return result;
    }

    /// <summary>
    /// Đối với các tệp tin trong thư mục memory/, chỉ nạp các tệp được chỉnh sửa trong vòng N ngày gần đây.
    /// Đối với các thư mục kỹ năng/skills khác, luôn luôn chấp nhận.
    /// </summary>
    private static bool IsRecentEnough(string filePath, string dirName)
    {
        if (!string.Equals(dirName, "memory", StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = Path.GetFileName(filePath);
        // Tệp tin memory hàng ngày được đặt tiền tố YYYY-MM-DD
        if (fileName.Length >= 10 && fileName[4] == '-' && fileName[7] == '-')
        {
            var datePrefix = fileName[..10];
            if (DateTime.TryParseExact(datePrefix, "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var fileDate))
            {
                return fileDate >= DateTime.Today.AddDays(-MemoryLookbackDays);
            }
        }

        // Nếu không thể phân tích ngày, chấp nhận để đảm bảo an toàn
        return true;
    }

    /// <summary>
    /// Ước tính sơ bộ số lượng Token của văn bản dựa trên quy tắc tỷ lệ 1 token ≈ 4 ký tự.
    /// </summary>
    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length / 4;
    }

    /// <summary>
    /// Thực hiện cắt ngắn văn bản một cách an toàn để phù hợp với ngân sách token được chỉ định,
    /// ưu tiên cắt tại vị trí xuống dòng gần nhất để bảo toàn cấu trúc văn bản.
    /// </summary>
    private static string TruncateToTokenBudget(string text, int tokenBudget)
    {
        var charBudget = tokenBudget * 4;
        if (text.Length <= charBudget) return text;

        var cutoff = charBudget;
        var lastNewline = text.LastIndexOf('\n', cutoff);
        if (lastNewline > 0)
            cutoff = lastNewline;

        return text[..cutoff] + "\n\n[Content truncated to fit token budget]";
    }
}
