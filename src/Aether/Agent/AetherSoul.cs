using Aether.Memory;
using Aether.Providers;
using Aether.Sessions;

namespace Aether.Agent;

public sealed class AetherSoul
{
    private const int MaxToolIterations = 8;
    private readonly ILLMProvider _llm;
    private readonly IMemorySystem _memory;
    private readonly IToolExecutor _tools;
    private readonly ISessionManager _sessions;

    public AetherSoul(ILLMProvider llm, IMemorySystem memory, IToolExecutor tools, ISessionManager sessions)
    {
        _llm = llm;
        _memory = memory;
        _tools = tools;
        _sessions = sessions;
    }

    public async Task<AgentResponse> ProcessAsync(string groupFolder, string prompt, CancellationToken ct)
    {
        var session = await _sessions.GetOrCreateSessionAsync(groupFolder, ct);
        var memoryContext = await _memory.LoadContextAsync(groupFolder, ct);
        var history = await _sessions.GetHistoryAsync(session.Id, maxMessages: 40, ct);

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(BuildSystemPrompt(memoryContext))
        };

        messages.AddRange(history.Select(message => new LlmMessage(message.Role, message.Content)));
        messages.Add(LlmMessage.User(prompt));

        await _sessions.AppendMessageAsync(session.Id, new SessionMessage("user", prompt, DateTimeOffset.UtcNow), ct);

        var response = await RunLlmToolLoopAsync(messages, ct);
        await _sessions.AppendMessageAsync(session.Id, new SessionMessage("assistant", response.Content, DateTimeOffset.UtcNow), ct);

        return new AgentResponse(response.Content, session.Id);
    }

    private async Task<LlmResponse> RunLlmToolLoopAsync(List<LlmMessage> messages, CancellationToken ct)
    {
        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            var response = await _llm.CompleteAsync(new LlmRequest(messages, BuiltInTools), ct);
            if (response.ToolCalls is not { Count: > 0 })
            {
                return response;
            }

            messages.Add(LlmMessage.AssistantToolCalls(response.Content, response.ToolCalls));
            foreach (var toolCall in response.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();
                var result = await _tools.ExecuteAsync(
                    new ToolCall(toolCall.Name, toolCall.Arguments),
                    ct);
                messages.Add(LlmMessage.ToolResult(toolCall.Id, toolCall.Name, FormatToolResult(result)));
            }
        }

        throw new InvalidOperationException($"AetherSoul exceeded {MaxToolIterations} tool iterations.");
    }

    private static string FormatToolResult(ToolResult result)
    {
        if (result.Succeeded)
        {
            return result.Output;
        }

        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            return $"Tool failed: {result.Error}{Environment.NewLine}{result.Output}";
        }

        return $"Tool failed: {result.Error}";
    }

    private static string BuildSystemPrompt(string memoryContext)
    {
        if (string.IsNullOrWhiteSpace(memoryContext))
        {
            return "You are Aether, a lightweight personal AI agent.";
        }

        return $"You are Aether, a lightweight personal AI agent.{Environment.NewLine}{Environment.NewLine}{memoryContext}";
    }

    private static readonly IReadOnlyList<LlmTool> BuiltInTools = new[]
    {
        new LlmTool(
            Name: "read",
            Description: "Read a UTF-8 text file from an allowed path.",
            ParametersJson: """
                {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string" }
                  },
                  "required": ["path"]
                }
                """),
        new LlmTool(
            Name: "glob",
            Description: "Find files under an allowed root using a glob pattern.",
            ParametersJson: """
                {
                  "type": "object",
                  "properties": {
                    "root": { "type": "string" },
                    "pattern": { "type": "string" }
                  },
                  "required": ["pattern"]
                }
                """),
        new LlmTool(
            Name: "grep",
            Description: "Search files under an allowed path using a regular expression.",
            ParametersJson: """
                {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string" },
                    "pattern": { "type": "string" },
                    "context_lines": { "type": "string" }
                  },
                  "required": ["path", "pattern"]
                }
                """),
        new LlmTool(
            Name: "bash",
            Description: "Run a shell command in an allowed working directory.",
            ParametersJson: """
                {
                  "type": "object",
                  "properties": {
                    "command": { "type": "string" },
                    "cwd": { "type": "string" }
                  },
                  "required": ["command"]
                }
                """),
        new LlmTool(
            Name: "write",
            Description: "Write UTF-8 text content to a file inside an allowed path.",
            ParametersJson: """
                {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string" },
                    "content": { "type": "string" }
                  },
                  "required": ["path", "content"]
                }
                """),
        new LlmTool(
            Name: "edit",
            Description: "Replace exact text in a file inside an allowed path.",
            ParametersJson: """
                {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string" },
                    "old": { "type": "string" },
                    "new": { "type": "string" }
                  },
                  "required": ["path", "old", "new"]
                }
                """)
    };
}

public sealed record AgentResponse(string Content, string SessionId);
