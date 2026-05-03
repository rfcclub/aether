using System.Text;
using Aether.Agents;
using Aether.Memory;
using Aether.Providers;
using Aether.Sessions;
using Aether.Skills;
using Aether.Tooling;

namespace Aether.Agent;

public sealed class AetherSoul
{
    private const int MaxToolIterations = 8;
    private const int MaxContextTokens = 120000;
    private readonly ILLMProvider _llm;
    private readonly IMemorySystem _memory;
    private readonly IToolExecutor _tools;
    private readonly ISessionManager _sessions;
    private readonly ISkillRegistry _skills;
    private readonly ISkillTrigger _skillTrigger;
    private readonly IAgentProfile _profile;
    private readonly IBootContract? _bootContract;

    public AetherSoul(
        ILLMProvider llm,
        IMemorySystem memory,
        IToolExecutor tools,
        ISessionManager sessions,
        ISkillRegistry skills,
        ISkillTrigger skillTrigger,
        IAgentProfile profile,
        IBootContract? bootContract = null)
    {
        _llm = llm;
        _memory = memory;
        _tools = tools;
        _sessions = sessions;
        _skills = skills;
        _skillTrigger = skillTrigger;
        _profile = profile;
        _bootContract = bootContract;
    }

    public async Task<AgentResponse> ProcessAsync(string groupFolder, string prompt, CancellationToken ct = default)
    {
        var session = await _sessions.GetOrCreateSessionAsync(groupFolder, ct);
        var memoryContext = await _memory.LoadContextAsync(groupFolder, ct);
        var persona = await _profile.LoadPersonaAsync(ct);
        var dailyMemory = await _profile.LoadDailyMemoryAsync(ct);
        var history = await _sessions.GetHistoryAsync(session.Id, maxMessages: 40, ct);

        // FEOFALLS boot contract — constitution + cognitive + working state
        string? constitution = null, cognitive = null, workingState = null;
        if (_bootContract is not null)
        {
            constitution = await _bootContract.LoadConstitutionAsync(ct);
            cognitive = await _bootContract.LoadCognitiveAsync(ct);
            workingState = await _bootContract.LoadWorkingStateAsync(ct);
        }

        // Detect skill trigger before building messages
        var skillContext = _skillTrigger.DetectTrigger(prompt, _skills.List().ToList());

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(BuildSystemPrompt(persona, dailyMemory, memoryContext,
                constitution, cognitive, workingState, skillContext))
        };

        messages.AddRange(history.Select(message => new LlmMessage(message.Role, message.Content)));
        messages.Add(LlmMessage.User(prompt));

        TruncateHistory(messages);

        await _sessions.AppendMessageAsync(session.Id, new SessionMessage("user", prompt, DateTimeOffset.UtcNow), ct);

        var response = await RunLlmToolLoopAsync(messages, ct);
        await _sessions.AppendMessageAsync(session.Id, new SessionMessage("assistant", response.Content, DateTimeOffset.UtcNow), ct);

        return new AgentResponse(response.Content, session.Id);
    }

    /// <summary>
    /// Process a prompt and stream text tokens back through the returned async enumerable.
    /// Handles tool calls by buffering the streaming response (text tokens are yielded
    /// via <see cref="CompleteStreamingEventsAsync"/>), executing any detected tool calls,
    /// and continuing the loop until a final text-only response is received.
    /// </summary>
    public async IAsyncEnumerable<string> ProcessStreamingAsync(
        string groupFolder,
        string prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var session = await _sessions.GetOrCreateSessionAsync(groupFolder, ct);
        var memoryContext = await _memory.LoadContextAsync(groupFolder, ct);
        var persona = await _profile.LoadPersonaAsync(ct);
        var dailyMemory = await _profile.LoadDailyMemoryAsync(ct);
        var history = await _sessions.GetHistoryAsync(session.Id, maxMessages: 40, ct);

        // FEOFALLS boot contract
        string? constitution = null, cognitive = null, workingState = null;
        if (_bootContract is not null)
        {
            constitution = await _bootContract.LoadConstitutionAsync(ct);
            cognitive = await _bootContract.LoadCognitiveAsync(ct);
            workingState = await _bootContract.LoadWorkingStateAsync(ct);
        }

        var skillContext = _skillTrigger.DetectTrigger(prompt, _skills.List().ToList());

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(BuildSystemPrompt(persona, dailyMemory, memoryContext,
                constitution, cognitive, workingState, skillContext))
        };

        messages.AddRange(history.Select(message => new LlmMessage(message.Role, message.Content)));
        messages.Add(LlmMessage.User(prompt));

        TruncateHistory(messages);

        await _sessions.AppendMessageAsync(session.Id, new SessionMessage("user", prompt, DateTimeOffset.UtcNow), ct);

        var fullContent = new StringBuilder();

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            // --- Streaming phase ---
            // Events are buffered because C# does not allow yield return inside try+catch blocks.
            var tokenBuffer = new List<string>();
            IReadOnlyList<LlmToolCall>? toolCalls = null;
            var sawToolCallOrFallback = false;
            var isFallback = false;
            var textContent = new StringBuilder();

            var toolsToUse = BuiltInTools;

            while (true)
            {
                tokenBuffer.Clear();
                toolCalls = null;
                sawToolCallOrFallback = false;
                isFallback = false;
                textContent.Clear();

                try
                {
                    await foreach (var evt in _llm.CompleteStreamingEventsAsync(
                        new LlmRequest(messages, toolsToUse), ct))
                    {
                        switch (evt)
                        {
                            case StreamEvent.TextToken tt:
                                textContent.Append(tt.Token);
                                fullContent.Append(tt.Token);
                                tokenBuffer.Add(tt.Token);
                                break;

                            case StreamEvent.Response responseEvent:
                                toolCalls = responseEvent.LlmResponse.ToolCalls;
                                sawToolCallOrFallback = toolCalls is { Count: > 0 };
                                break;
                        }
                    }
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("tool use") || ex.Message.Contains("tool"))
                {
                    // Model does not support tools -- retry without them
                    if (toolsToUse is not null)
                    {
                        toolsToUse = null;
                        sawToolCallOrFallback = true;
                        isFallback = true;
                        continue; // retry with tools disabled
                    }

                    throw;
                }

                break; // success -- exit the retry loop
            }

            // Yield all buffered tokens now that we are outside the try/catch scope
            foreach (var token in tokenBuffer)
            {
                yield return token;
            }

            if (isFallback || !sawToolCallOrFallback)
            {
                break; // No tool calls -- we are done
            }

            // --- Tool execution phase ---
            messages.Add(LlmMessage.AssistantToolCalls(textContent.ToString(), toolCalls!));

            foreach (var toolCall in toolCalls!)
            {
                ct.ThrowIfCancellationRequested();

                var toolDef = BuiltInTools.FirstOrDefault(t => t.Name == toolCall.Name);
                if (toolDef is not null)
                {
                    var errors = ParameterValidator.Validate(toolCall, toolDef);
                    if (errors.Count > 0)
                    {
                        messages.Add(LlmMessage.ToolResult(toolCall.Id, toolCall.Name,
                            ParameterValidator.FormatErrors(errors)));
                        continue;
                    }
                }

                var result = await _tools.ExecuteAsync(
                    new ToolCall(toolCall.Name, toolCall.Arguments),
                    ct);
                messages.Add(LlmMessage.ToolResult(toolCall.Id, toolCall.Name, FormatToolResult(result)));
            }
            // Continue loop to stream the LLM's response to tool results
        }

        // Save final accumulated response to session history
        await _sessions.AppendMessageAsync(session.Id,
            new SessionMessage("assistant", fullContent.ToString(), DateTimeOffset.UtcNow), ct);
    }

    private static void TruncateHistory(List<LlmMessage> messages)
    {
        // Keep system message (index 0) and last user message. Remove oldest history first.
        while (messages.Count > 2 && EstimateTokens(messages) > MaxContextTokens)
        {
            messages.RemoveAt(1);
        }
    }

    private static int EstimateTokens(IReadOnlyList<LlmMessage> messages)
    {
        var chars = 0;
        foreach (var message in messages)
        {
            chars += message.Content?.Length ?? 0;
            if (message.ToolCalls is not null)
            {
                foreach (var tc in message.ToolCalls)
                {
                    chars += tc.Name.Length + tc.Id.Length;
                    foreach (var (k, v) in tc.Arguments)
                        chars += k.Length + v.Length;
                }
            }
        }
        return chars / 4;
    }

    private async Task<LlmResponse> RunLlmToolLoopAsync(List<LlmMessage> messages, CancellationToken ct)
    {
        var tools = BuiltInTools;
        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            LlmResponse response;
            try
            {
                response = await _llm.CompleteAsync(new LlmRequest(messages, tools), ct);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("tool use") || ex.Message.Contains("tool"))
            {
                // Model doesn't support tools — retry without them
                if (tools is not null)
                {
                    tools = null;
                    response = await _llm.CompleteAsync(new LlmRequest(messages, null), ct);
                }
                else throw;
            }

            if (response.ToolCalls is not { Count: > 0 })
            {
                return response;
            }

            messages.Add(LlmMessage.AssistantToolCalls(response.Content, response.ToolCalls));
            foreach (var toolCall in response.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();

                var toolDef = BuiltInTools.FirstOrDefault(t => t.Name == toolCall.Name);
                if (toolDef is not null)
                {
                    var errors = ParameterValidator.Validate(toolCall, toolDef);
                    if (errors.Count > 0)
                    {
                        messages.Add(LlmMessage.ToolResult(toolCall.Id, toolCall.Name,
                            ParameterValidator.FormatErrors(errors)));
                        continue;
                    }
                }

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

    private static string BuildSystemPrompt(
        string persona,
        string dailyMemory,
        string memoryContext,
        string? constitution,
        string? cognitive,
        string? workingState,
        SkillContext? skillContext)
    {
        var sb = new StringBuilder();

        // 0. Constitution — red lines, must always be in context
        if (!string.IsNullOrWhiteSpace(constitution))
        {
            sb.AppendLine("## Constitution (Non-Negotiable)");
            sb.AppendLine(constitution);
            sb.AppendLine();
        }

        // 1. Bootstrap — AGENTS.md contains instructions to read other files via tools
        sb.AppendLine(persona);
        sb.AppendLine();

        // 1.5 Cognitive context — SOUL.md, USER.md, IDENTITY.md (agent identity)
        if (!string.IsNullOrWhiteSpace(cognitive))
        {
            sb.AppendLine("## Cognitive Context");
            sb.AppendLine(cognitive);
            sb.AppendLine();
        }

        // 2. Working state — current tasks, heartbeat
        if (!string.IsNullOrWhiteSpace(workingState))
        {
            sb.AppendLine("## Working State");
            sb.AppendLine(workingState);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(dailyMemory))
        {
            sb.AppendLine("## Recent Memory");
            sb.AppendLine(dailyMemory);
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(memoryContext))
        {
            sb.AppendLine("## Group Context");
            sb.AppendLine(memoryContext);
            sb.AppendLine();
        }

        if (skillContext != null)
        {
            sb.AppendLine($"[Skill: {skillContext.Skill.Name}]");
            if (!string.IsNullOrWhiteSpace(skillContext.Skill.Description))
                sb.AppendLine($"Description: {skillContext.Skill.Description}");
            if (!string.IsNullOrWhiteSpace(skillContext.Skill.Body))
                sb.AppendLine().AppendLine(skillContext.Skill.Body);
            if (skillContext.Skill.AutoApply)
                sb.AppendLine().AppendLine("(Auto-apply mode — follow skill steps)");
        }

        return sb.ToString();
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
                """,
            SchemaJson: """
                {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string" }
                  },
                  "required": ["path"],
                  "additionalProperties": false
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
                """,
            SchemaJson: """
                {
                  "type": "object",
                  "properties": {
                    "root": { "type": "string" },
                    "pattern": { "type": "string" }
                  },
                  "required": ["pattern"],
                  "additionalProperties": false
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
                """,
            SchemaJson: """
                {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string" },
                    "pattern": { "type": "string" },
                    "context_lines": { "type": "string" }
                  },
                  "required": ["path", "pattern"],
                  "additionalProperties": false
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
                """,
            SchemaJson: """
                {
                  "type": "object",
                  "properties": {
                    "command": { "type": "string" },
                    "cwd": { "type": "string" }
                  },
                  "required": ["command"],
                  "additionalProperties": false
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
                """,
            SchemaJson: """
                {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string" },
                    "content": { "type": "string" }
                  },
                  "required": ["path", "content"],
                  "additionalProperties": false
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
                """,
            SchemaJson: """
                {
                  "type": "object",
                  "properties": {
                    "path": { "type": "string" },
                    "old": { "type": "string" },
                    "new": { "type": "string" }
                  },
                  "required": ["path", "old", "new"],
                  "additionalProperties": false
                }
                """)
    };
}

public sealed record AgentResponse(string Content, string SessionId);
