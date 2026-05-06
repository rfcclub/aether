using System.Text;
using Aether.Providers;

namespace Aether.Agent;

/// <summary>
/// Minimal agent context — one system prompt built once, message history in memory.
/// Pattern: NanoClaw/OpenClaw — the conversation IS the context.
/// </summary>
public sealed class WorkingContext
{
    private readonly List<LlmMessage> _messages = new();
    private string _systemPrompt;

    public string SessionId { get; private set; }
    public string WorkspacePath { get; }
    public IReadOnlyList<LlmMessage> Messages => _messages;
    public IReadOnlyList<LlmTool> Tools { get; }

    public WorkingContext(string workspacePath, IReadOnlyList<LlmTool> tools)
    {
        SessionId = Guid.NewGuid().ToString("N");
        WorkspacePath = workspacePath;
        Tools = tools;
        _systemPrompt = BuildDefaultSystemPrompt(workspacePath);
        _messages.Add(LlmMessage.System(_systemPrompt));
    }

    public void SetSystemPrompt(string prompt)
    {
        _systemPrompt = prompt;
        if (_messages.Count > 0 && _messages[0].Role == "system")
            _messages[0] = LlmMessage.System(prompt);
        else
            _messages.Insert(0, LlmMessage.System(prompt));
    }

    public void AddUser(string content)
    {
        if (_messages.Count == 0 || _messages[0].Role != "system")
            _messages.Insert(0, LlmMessage.System(_systemPrompt));
        _messages.Add(LlmMessage.User(content));
    }

    public void AddAssistant(string content)
    {
        _messages.Add(new LlmMessage("assistant", content));
    }

    public void AddAssistantToolCalls(string content, IReadOnlyList<LlmToolCall> toolCalls)
    {
        _messages.Add(LlmMessage.AssistantToolCalls(content, toolCalls));
    }

    public void AddToolResult(string toolCallId, string toolName, string content)
    {
        _messages.Add(LlmMessage.ToolResult(toolCallId, toolName, content));
    }

    public void Reset()
    {
        SessionId = Guid.NewGuid().ToString("N");
        _messages.Clear();
    }

    public void Compact(int maxTokens)
    {
        while (_messages.Count > 2 && EstimateTokens(_messages) > maxTokens)
            _messages.RemoveAt(1);
    }

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
