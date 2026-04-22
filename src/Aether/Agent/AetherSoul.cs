using Aether.Memory;
using Aether.Providers;
using Aether.Sessions;

namespace Aether.Agent;

public sealed class AetherSoul
{
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
            new("system", BuildSystemPrompt(memoryContext))
        };

        messages.AddRange(history.Select(message => new LlmMessage(message.Role, message.Content)));
        messages.Add(new LlmMessage("user", prompt));

        await _sessions.AppendMessageAsync(session.Id, new SessionMessage("user", prompt, DateTimeOffset.UtcNow), ct);
        var response = await _llm.CompleteAsync(new LlmRequest(messages), ct);
        await _sessions.AppendMessageAsync(session.Id, new SessionMessage("assistant", response.Content, DateTimeOffset.UtcNow), ct);

        _ = _tools;
        return new AgentResponse(response.Content, session.Id);
    }

    private static string BuildSystemPrompt(string memoryContext)
    {
        if (string.IsNullOrWhiteSpace(memoryContext))
        {
            return "You are Aether, a lightweight personal AI agent.";
        }

        return $"You are Aether, a lightweight personal AI agent.{Environment.NewLine}{Environment.NewLine}{memoryContext}";
    }
}

public sealed record AgentResponse(string Content, string SessionId);
