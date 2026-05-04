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
    private readonly FileMemory _memory;
    private readonly ToolExecutor _tools;
    private readonly SessionManager _sessions;
    private readonly SkillRegistry _skills;
    private readonly SkillTrigger _skillTrigger;
    private readonly AgentProfile _profile;
    private readonly BootContract? _bootContract;

    // Cache persona + boot content to avoid re-reading files on every call
    private (string Persona, string DailyMemory, string Constitution, string Identity, string Memory, string WorkingState) _cache;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public AetherSoul(
        ILLMProvider llm,
        FileMemory memory,
        ToolExecutor tools,
        SessionManager sessions,
        SkillRegistry skills,
        SkillTrigger skillTrigger,
        AgentProfile profile,
        BootContract? bootContract = null)
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

    /// <summary>
    /// Process a task prompt without persona loading — for heartbeat, cron, and scheduled tasks.
    /// </summary>
    public async Task<AgentResponse> ProcessTaskAsync(string groupFolder, string prompt, CancellationToken ct = default)
    {
        var session = await _sessions.GetOrCreateSessionAsync(groupFolder, ct);
        var memoryContext = await _memory.LoadContextAsync(groupFolder, ct);
        var history = await _sessions.GetHistoryAsync(session.Id, maxMessages: 40, ct);

        var persona = await _profile.LoadPersonaAsync(ct);
        var dailyMemory = await _profile.LoadDailyMemoryAsync(ct);
        string? constitution = null, identity = null, memory = null, workingState = null;
        if (_bootContract is not null)
        {
            constitution = await _bootContract.LoadConstitutionAsync(ct);
            identity = await _bootContract.LoadIdentityAsync(ct);
            memory = await _bootContract.LoadCognitiveAsync(ct);
            workingState = await _bootContract.LoadWorkingStateAsync(ct);
        }

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(BuildSystemPrompt(persona, dailyMemory, memoryContext,
                constitution, identity, memory, workingState, null))
        };
        messages.AddRange(history.Select(m => new LlmMessage(m.Role, m.Content)));
        messages.Add(LlmMessage.User(prompt));
        TruncateHistory(messages);

        await _sessions.AppendMessageAsync(session.Id, new SessionMessage("user", prompt, DateTimeOffset.UtcNow), ct);
        var response = await RunLlmToolLoopAsync(messages, ct);
        await _sessions.AppendMessageAsync(session.Id, new SessionMessage("assistant", response.Content, DateTimeOffset.UtcNow), ct);

        return new AgentResponse(response.Content, session.Id);
    }

    public async Task<AgentResponse> ProcessAsync(string groupFolder, string prompt, CancellationToken ct = default)
    {
        var session = await _sessions.GetOrCreateSessionAsync(groupFolder, ct);
        var memoryContext = await _memory.LoadContextAsync(groupFolder, ct);

        // Cache persona + boot content — avoids re-reading files on every heartbeat/cron tick
        string persona, dailyMemory, constitution, identity, memory, workingState;
        if (DateTime.UtcNow < _cacheExpiry)
        {
            (persona, dailyMemory, constitution, identity, memory, workingState) = _cache;
        }
        else
        {
            persona = await _profile.LoadPersonaAsync(ct);
            dailyMemory = await _profile.LoadDailyMemoryAsync(ct);
            constitution = null!; identity = null!; memory = null!; workingState = null!;
            if (_bootContract is not null)
            {
                constitution = await _bootContract.LoadConstitutionAsync(ct);
                identity = await _bootContract.LoadIdentityAsync(ct);
                memory = await _bootContract.LoadCognitiveAsync(ct);
                workingState = await _bootContract.LoadWorkingStateAsync(ct);
            }
            _cache = (persona, dailyMemory, constitution, identity, memory, workingState);
            _cacheExpiry = DateTime.UtcNow + CacheTtl;
        }
        var history = await _sessions.GetHistoryAsync(session.Id, maxMessages: 40, ct);

        // Detect skill trigger before building messages
        var skillContext = _skillTrigger.DetectTrigger(prompt, _skills.List().ToList());

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(BuildSystemPrompt(persona, dailyMemory, memoryContext,
                constitution, identity, memory, workingState, skillContext))
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

        // Boot contract
        string? constitution = null, identity = null, memory = null, workingState = null;
        if (_bootContract is not null)
        {
            constitution = await _bootContract.LoadConstitutionAsync(ct);
            identity = await _bootContract.LoadIdentityAsync(ct);
            memory = await _bootContract.LoadCognitiveAsync(ct);
            workingState = await _bootContract.LoadWorkingStateAsync(ct);
        }

        var skillContext = _skillTrigger.DetectTrigger(prompt, _skills.List().ToList());

        var messages = new List<LlmMessage>
        {
            LlmMessage.System(BuildSystemPrompt(persona, dailyMemory, memoryContext,
                constitution, identity, memory, workingState, skillContext))
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
        string? identity,
        string? memory,
        string? workingState,
        SkillContext? skillContext)
    {
        var sb = new StringBuilder();

        // Layer 1: Identity (Mandatory) — persona embodiment
        sb.AppendLine("## Identity (Mandatory)");
        sb.AppendLine("You ARE this agent. The following files define your identity, voice,");
        sb.AppendLine("tone, and behavioral rules. Embody them — this is who you are,");
        sb.AppendLine("not reference material to consult. Every reply must reflect this persona.");
        sb.AppendLine();
        sb.AppendLine("If SOUL.md is present, its voice and rules are your voice and rules.");
        sb.AppendLine("If IDENTITY.md is present, its self-model is your self-model.");
        sb.AppendLine("Follow AGENTS.md operating rules at all times.");
        sb.AppendLine("The user's request is your priority. Act on it directly.");
        sb.AppendLine();
        sb.AppendLine("## CRITICAL — You MUST Use Tools To Read Files");
        sb.AppendLine("When any file is mentioned (SOUL.md, USER.md, TASK_INBOX.md, MEMORY.md,");
        sb.AppendLine("or any other filename), you MUST call the `read` tool to read it.");
        sb.AppendLine("This is NOT optional. You are FORBIDDEN from describing, assuming,");
        sb.AppendLine("or fabricating file contents without first calling the `read` tool.");
        sb.AppendLine("If you have not called `read` on a file, you DO NOT know what is in it.");
        sb.AppendLine("If `read` returns empty or fails, say so — do not invent content.");
        sb.AppendLine("Each file = one `read` tool call. No exceptions.");
        sb.AppendLine();
        sb.AppendLine("**Before replying, verify: Does this response reflect my persona per SOUL.md?**");
        sb.AppendLine();

        // Current date — prevents model from guessing wrong date
        sb.AppendLine("## Current Date");
        sb.AppendLine($"Today is {DateTime.UtcNow:yyyy-MM-dd} (UTC).");
        sb.AppendLine("Use this date for all date-dependent operations.");
        sb.AppendLine();

        // AGENTS.md — Operating Rules
        if (!string.IsNullOrWhiteSpace(persona))
        {
            sb.AppendLine("## AGENTS.md — Your Operating Rules");
            sb.AppendLine(persona);
            sb.AppendLine();
        }

        // SOUL.md, IDENTITY.md, USER.md — identity files
        if (!string.IsNullOrWhiteSpace(identity))
        {
            sb.AppendLine("## SOUL.md, IDENTITY.md, USER.md — Your Voice, Self-Model, and Relationship");
            sb.AppendLine(identity);
            sb.AppendLine();
        }

        // Layer 2: Constitution (Non-Negotiable Red Lines)
        if (!string.IsNullOrWhiteSpace(constitution))
        {
            sb.AppendLine("## Constitution (Non-Negotiable Red Lines)");
            sb.AppendLine("Instruction priority: Constitution > Persona > User request > Tool feedback.");
            sb.AppendLine("These rules CANNOT be violated under any circumstance.");
            sb.AppendLine("They override persona, user requests, and any other instruction.");
            sb.AppendLine();
            sb.AppendLine(constitution);
            sb.AppendLine();
        }

        // Conflict resolution: persona voice vs execution discipline
        sb.AppendLine("## Conflict Resolution");
        sb.AppendLine("When SOUL.md voice conflicts with Execution Bias:");
        sb.AppendLine("- For actions (code edits, tool use, verification) → follow Execution Bias");
        sb.AppendLine("- For communication (tone, warmth, style, personality) → follow SOUL.md");
        sb.AppendLine();

        // Layer 3: Execution Bias
        sb.AppendLine("## Execution Bias");
        sb.AppendLine();
        sb.AppendLine("### Behavioral Defaults (applies always — chat and code)");
        sb.AppendLine("- Clear request → act immediately in this turn. Don't describe — do.");
        sb.AppendLine("- Continue until done or genuinely blocked (blocked = needs user decision, external dependency, or explicit permission). No deferred promises.");
        sb.AppendLine("- Weak/empty result → vary approach before concluding. Don't retry blindly.");
        sb.AppendLine("- Mutable facts (files, git, state) → check live, don't assume.");
        sb.AppendLine("- If blocked, propose smallest viable workaround and continue.");
        sb.AppendLine();
        sb.AppendLine("### Code Style (when editing code)");
        sb.AppendLine("- Read before write/edit. Never suggest changes to code you haven't inspected.");
        sb.AppendLine("- Minimal scope — only what was requested. No adjacent refactoring.");
        sb.AppendLine("- Don't add error handling for conditions that can't happen.");
        sb.AppendLine("- Prefer editing existing files over creating new ones.");
        sb.AppendLine("- Comments only for non-obvious reasoning. No narrative comments.");
        sb.AppendLine();
        sb.AppendLine("### Verification");
        sb.AppendLine("- Deliver evidence, not promises: test output, build logs, inspection.");
        sb.AppendLine("- Run checks after changes. Show command output.");
        sb.AppendLine("- Don't claim PASS without supporting evidence.");
        sb.AppendLine("- If a check fails, diagnose before retrying.");
        sb.AppendLine();

        // Layer 4: Memory — Long-Term
        if (!string.IsNullOrWhiteSpace(memory))
        {
            sb.AppendLine("## Memory — Long-Term");
            sb.AppendLine(memory);
            sb.AppendLine();
        }

        // Layer 5: Working State
        if (!string.IsNullOrWhiteSpace(workingState))
        {
            sb.AppendLine("## Working State");
            sb.AppendLine(workingState);
            sb.AppendLine();
        }

        // Layer 6: Recent Memory
        if (!string.IsNullOrWhiteSpace(dailyMemory))
        {
            sb.AppendLine("## Recent Memory");
            sb.AppendLine(dailyMemory);
            sb.AppendLine();
        }

        // Layer 7: Group Context
        if (!string.IsNullOrWhiteSpace(memoryContext))
        {
            sb.AppendLine("## Group Context");
            sb.AppendLine(memoryContext);
            sb.AppendLine();
        }

        // Layer 8: Skill
        if (skillContext != null)
        {
            sb.AppendLine($"## Skill: {skillContext.Skill.Name}");
            if (!string.IsNullOrWhiteSpace(skillContext.Skill.Description))
                sb.AppendLine($"Description: {skillContext.Skill.Description}");
            if (!string.IsNullOrWhiteSpace(skillContext.Skill.Body))
                sb.AppendLine().AppendLine(skillContext.Skill.Body);
            if (skillContext.Skill.AutoApply)
                sb.AppendLine().AppendLine("(Auto-apply mode — follow skill steps)");
            sb.AppendLine();
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
