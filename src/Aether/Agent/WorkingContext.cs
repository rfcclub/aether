using System.Text;
using Aether.Providers;

namespace Aether.Agent;

/// <summary>
/// Ngữ cảnh làm việc tối giản của Agent (WorkingContext).
/// Lớp này quản lý trực tiếp luồng hội thoại động, chứa System Prompt và danh sách toàn bộ lịch sử tin nhắn trong bộ nhớ tạm thời.
/// Triển khai triết lý thiết kế NanoClaw/OpenClaw: Cuộc hội thoại CHÍNH LÀ ngữ cảnh (the conversation IS the context).
/// </summary>
public sealed class WorkingContext
{
    private readonly List<LlmMessage> _messages = new();
    private string _systemPrompt;

    /// <summary>
    /// Định danh phiên làm việc hiện tại (Session ID) được sinh ngẫu nhiên hoặc gán từ SessionManager.
    /// </summary>
    public string SessionId { get; private set; }

    /// <summary>
    /// Đường dẫn thư mục làm việc của Agent.
    /// </summary>
    public string WorkspacePath { get; }

    /// <summary>
    /// Danh sách toàn bộ lịch sử các tin nhắn hội thoại (LlmMessage) đang lưu trong bộ nhớ.
    /// </summary>
    public IReadOnlyList<LlmMessage> Messages => _messages;

    /// <summary>
    /// Danh sách các công cụ (Tools) được trang bị cho Agent trong ngữ cảnh làm việc này.
    /// </summary>
    public IReadOnlyList<LlmTool> Tools { get; }

    /// <summary>
    /// Khởi tạo một thực thể WorkingContext mới cho Agent.
    /// </summary>
    /// <param name="workspacePath">Thư mục làm việc tuyệt đối của Agent.</param>
    /// <param name="tools">Danh sách các công cụ được trang bị.</param>
    public WorkingContext(string workspacePath, IReadOnlyList<LlmTool> tools)
    {
        SessionId = Guid.NewGuid().ToString("N");
        WorkspacePath = workspacePath;
        Tools = tools;
        _systemPrompt = BuildDefaultSystemPrompt(workspacePath);
        _messages.Add(LlmMessage.System(_systemPrompt));
    }

    /// <summary>
    /// Gán định danh phiên làm việc cụ thể.
    /// </summary>
    /// <param name="sessionId">Mã định danh phiên làm việc.</param>
    public void SetSessionId(string sessionId)
    {
        SessionId = sessionId;
    }

    /// <summary>
    /// Thiết lập lại System Prompt cốt lõi của Agent và cập nhật hoặc chèn nó lên đầu danh sách tin nhắn.
    /// </summary>
    /// <param name="prompt">Nội dung System Prompt mới.</param>
    public void SetSystemPrompt(string prompt)
    {
        _systemPrompt = prompt;
        if (_messages.Count > 0 && _messages[0].Role == "system")
            _messages[0] = LlmMessage.System(prompt);
        else
            _messages.Insert(0, LlmMessage.System(prompt));
    }

    /// <summary>
    /// Ghi nhận tin nhắn mới của người dùng (User Message) vào lịch sử hội thoại.
    /// </summary>
    /// <param name="content">Nội dung tin nhắn người dùng.</param>
    public void AddUser(string content)
    {
        if (_messages.Count == 0 || _messages[0].Role != "system")
            _messages.Insert(0, LlmMessage.System(_systemPrompt));
        _messages.Add(LlmMessage.User(content));
    }

    /// <summary>
    /// Ghi nhận phản hồi mới của Agent (Assistant Message) vào lịch sử hội thoại.
    /// </summary>
    /// <param name="content">Nội dung phản hồi của Agent.</param>
    public void AddAssistant(string content)
    {
        _messages.Add(new LlmMessage("assistant", content));
    }

    /// <summary>
    /// Ghi nhận yêu cầu gọi công cụ (Tool Calls) từ LLM vào lịch sử hội thoại.
    /// </summary>
    /// <param name="content">Nội dung văn bản giải thích đi kèm nếu có.</param>
    /// <param name="toolCalls">Danh sách thông tin các công cụ được yêu cầu gọi.</param>
    public void AddAssistantToolCalls(string content, IReadOnlyList<LlmToolCall> toolCalls)
    {
        _messages.Add(LlmMessage.AssistantToolCalls(content, toolCalls));
    }

    /// <summary>
    /// Ghi nhận kết quả phản hồi của một công cụ (Tool Result) sau khi được thực thi thành công/thất bại.
    /// </summary>
    /// <param name="toolCallId">Định danh cuộc gọi công cụ tương ứng.</param>
    /// <param name="toolName">Tên công cụ vừa thực thi.</param>
    /// <param name="content">Nội dung kết quả đầu ra của công cụ.</param>
    public void AddToolResult(string toolCallId, string toolName, string content)
    {
        _messages.Add(LlmMessage.ToolResult(toolCallId, toolName, content));
    }

    /// <summary>
    /// Làm sạch toàn bộ lịch sử tin nhắn trong bộ nhớ và sinh ngẫu nhiên một Session ID mới tinh.
    /// </summary>
    public void Reset()
    {
        SessionId = Guid.NewGuid().ToString("N");
        _messages.Clear();
    }

    /// <summary>
    /// Thu gọn cuộc hội thoại (Context Compaction) để chống tràn token của mô hình LLM.
    /// Tự động xóa các tin nhắn cũ hơn nằm ở giữa cuộc hội thoại, giữ nguyên System Prompt ở đầu và các tin nhắn mới ở cuối.
    /// </summary>
    /// <param name="maxTokens">Số lượng Token giới hạn tối đa.</param>
    public void Compact(int maxTokens)
    {
        while (_messages.Count > 2 && EstimateTokens(_messages) > maxTokens)
            _messages.RemoveAt(1);
    }

    /// <summary>
    /// Ước tính tổng lượng Token đã tiêu thụ bởi toàn bộ lịch sử tin nhắn trong bộ nhớ.
    /// </summary>
    private static int EstimateTokens(IReadOnlyList<LlmMessage> messages)
    {
        var chars = 0;
        foreach (var m in messages)
        {
            chars += m.Content?.Length ?? 0;
            if (m.ToolCalls is not null)
                foreach (var tc in m.ToolCalls)
                {
                    chars += tc.Name.Length + tc.Id.Length;
                    foreach (var (k, v) in tc.Arguments)
                        chars += k.Length + v.Length;
                }
        }
        return Math.Max(1, chars / 4);
    }

    /// <summary>
    /// Xây dựng System Prompt mặc định tinh giản làm kim chỉ nam hoạt động cho Agent.
    /// </summary>
    private static string BuildDefaultSystemPrompt(string workspacePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are Aether, a working agent.");
        sb.AppendLine($"Workspace: {workspacePath}");
        sb.AppendLine();
        sb.AppendLine("## Rules");
        sb.AppendLine("- Act immediately. Don't describe — do.");
        sb.AppendLine("- Read before write/edit. Minimal scope.");
        sb.AppendLine("- Deliver evidence, not promises.");
        sb.AppendLine();
        sb.AppendLine("## Safety");
        sb.AppendLine("Refuse: self-harm, illegal activity, data exfiltration, destructive commands without confirmation.");
        return sb.ToString();
    }
}
