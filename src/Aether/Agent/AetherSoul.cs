using System.Text;
using System.Text.Json;
using Aether.Agents;
using Aether.Memory;
using Aether.Plugins;
using Aether.Providers;
using Aether.Sessions;
using Aether.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RegistryToolExecutor = Aether.Tooling.ToolExecutor;
using ToolRegistry = Aether.Tooling.ToolRegistry;

namespace Aether.Agent;

public sealed class AetherSoul
{
    private const int MaxToolIterations = 16;
    private readonly ILLMProvider _llm;
    private readonly ToolExecutor? _legacyTools;
    private readonly RegistryToolExecutor? _registryTools;
    private readonly ToolRegistry? _toolRegistry;
    private readonly SqliteMemorySystem? _sqliteMemory;
    private readonly SessionManager? _sessionManager;
    private readonly AxiomValidator? _axiomValidator;
    private readonly HookEngine? _hooks;
    private readonly string _agentName;
    private readonly string? _reasoningEffort;
    private readonly int? _thinkingBudgetTokens;
    private readonly ILogger<AetherSoul> _logger;

    private WorkingContext _ctx;

    public IReadOnlyList<LlmMessage> Messages => _ctx.Messages;
    public string SessionId => _ctx.SessionId;

    public AetherSoul(
        ILLMProvider llm,
        ToolExecutor tools,
        AgentProfile profile,
        ILogger<AetherSoul>? logger = null,
        HookEngine? hooks = null,
        SqliteMemorySystem? sqliteMemory = null,
        SessionManager? sessionManager = null)
    {
        _llm = llm;
        _legacyTools = tools;
        _sqliteMemory = sqliteMemory;
        _sessionManager = sessionManager;
        _hooks = hooks;
        _agentName = profile.Name;
        _reasoningEffort = profile.Model?.ReasoningEffort;
        _thinkingBudgetTokens = profile.Model?.ThinkingBudgetTokens;
        _logger = logger ?? NullLogger<AetherSoul>.Instance;
        _ctx = new WorkingContext(profile.AgentDirectory, BuiltInTools);

        var identity = profile.LoadIdentityContext();
        var dailyMemory = profile.LoadDailyMemory();
        if (!string.IsNullOrWhiteSpace(identity) || !string.IsNullOrWhiteSpace(dailyMemory))
            _ctx.SetSystemPrompt(BuildSystemPrompt(identity, dailyMemory));

        var soulPath = Path.Combine(profile.AgentDirectory, "SOUL.md");
        _axiomValidator = new AxiomValidator(soulPath, NullLogger<AxiomValidator>.Instance);
    }

    public AetherSoul(
        ILLMProvider llm,
        RegistryToolExecutor tools,
        ToolRegistry toolRegistry,
        AgentProfile profile,
        ILogger<AetherSoul>? logger = null,
        HookEngine? hooks = null,
        SqliteMemorySystem? sqliteMemory = null,
        SessionManager? sessionManager = null)
    {
        _llm = llm;
        _registryTools = tools;
        _toolRegistry = toolRegistry;
        _sqliteMemory = sqliteMemory;
        _sessionManager = sessionManager;
        _hooks = hooks;
        _agentName = profile.Name;
        _reasoningEffort = profile.Model?.ReasoningEffort;
        _thinkingBudgetTokens = profile.Model?.ThinkingBudgetTokens;
        _logger = logger ?? NullLogger<AetherSoul>.Instance;
        _ctx = new WorkingContext(profile.AgentDirectory, GetToolDefinitions());

        var identity = profile.LoadIdentityContext();
        var dailyMemory = profile.LoadDailyMemory();
        if (!string.IsNullOrWhiteSpace(identity) || !string.IsNullOrWhiteSpace(dailyMemory))
            _ctx.SetSystemPrompt(BuildSystemPrompt(identity, dailyMemory));

        var soulPath = Path.Combine(profile.AgentDirectory, "SOUL.md");
        _axiomValidator = new AxiomValidator(soulPath, NullLogger<AxiomValidator>.Instance);
    }

    public void Reset() => _ctx.Reset();

    /// <summary>
    /// Process a task prompt — minimal path, no persona loading.
    /// </summary>
    public Task<AgentResponse> ProcessTaskAsync(string groupFolder, string prompt, CancellationToken ct = default)
        => ProcessIsolatedAsync(prompt, ct);

    public Task<AgentResponse> ProcessTaskAsync(string groupFolder, string prompt, string? workingStateOverride, CancellationToken ct = default)
    {
        var fullPrompt = prompt;
        if (!string.IsNullOrWhiteSpace(workingStateOverride))
            fullPrompt = $"{prompt}\n\n## Working State\n{workingStateOverride}";
        return ProcessIsolatedAsync(fullPrompt, ct);
    }

    public async Task<AgentResponse> ProcessAsync(string groupFolder, string prompt, CancellationToken ct = default)
    {
        await EnsureSessionLoadedAsync(groupFolder, ct);

        // ── Token Budgeting ──
        _ctx.Compact(10000);

        await InjectRelevantMemoriesAsync(prompt, ct);

        _ctx.AddUser(prompt);
        if (_sessionManager is not null)
            await _sessionManager.AppendMessageAsync(_ctx.SessionId, new SessionMessage("user", prompt, DateTimeOffset.UtcNow), ct);

        var response = await RunLlmToolLoopAsync(_ctx.Messages, _ctx, ct);

        _ctx.AddAssistant(response.Content);
        if (_sessionManager is not null)
            await _sessionManager.AppendMessageAsync(_ctx.SessionId, new SessionMessage("assistant", response.Content, DateTimeOffset.UtcNow), ct);

        return new AgentResponse(response.Content, _ctx.SessionId);
    }

    private async Task EnsureSessionLoadedAsync(string groupFolder, CancellationToken ct)
    {
        if (_sessionManager is null) return;

        // Try to find existing session for this group
        var session = await _sessionManager.GetOrCreateSessionAsync(groupFolder, ct);
        _ctx.SetSessionId(session.Id);

        // If context is empty (only system prompt), load history
        if (_ctx.Messages.Count <= 1)
        {
            var history = await _sessionManager.GetHistoryAsync(session.Id, 16, ct);
            foreach (var msg in history)
            {
                if (msg.Role == "user") _ctx.AddUser(msg.Content);
                else if (msg.Role == "assistant") _ctx.AddAssistant(msg.Content);
            }
        }
    }

    private async Task<LlmResponse> CompleteWithRetryAsync(LlmRequest request, CancellationToken ct)
    {
        const int maxRetries = 3;
        var delay = TimeSpan.FromSeconds(1);

        for (int i = 0; i <= maxRetries; i++)
        {
            try
            {
                return await _llm.CompleteAsync(request, ct);
            }
            catch (Exception ex) when (i < maxRetries && IsRetryable(ex))
            {
                _logger.LogWarning(ex, "LLM call failed (attempt {Attempt}/{Max}). Retrying in {Delay}s...", 
                    i + 1, maxRetries + 1, delay.TotalSeconds);
                await Task.Delay(delay, ct);
                delay *= 2;
            }
        }

        return await _llm.CompleteAsync(request, ct); // Final attempt
    }

    private static bool IsRetryable(Exception ex)
    {
        return ex is HttpRequestException or IOException || 
               (ex is TaskCanceledException && ex.InnerException is TimeoutException);
    }

    private async Task InjectRelevantMemoriesAsync(string prompt, CancellationToken ct)
    {
        if (_sqliteMemory is null) return;

        try
        {
            var searchResults = await _sqliteMemory.SearchAsync(prompt, limit: 3, ct: ct);
            if (searchResults.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("## Relevant Memories");
                foreach (var res in searchResults)
                {
                    sb.AppendLine($"- [{res.Timestamp:yyyy-MM-dd}] {res.Snippet}");
                }
                _ctx.AddUser($"[System: Relevant context found in memory]\n{sb}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Memory search failed during context injection");
        }
    }

    private async Task<AgentResponse> ProcessIsolatedAsync(string prompt, CancellationToken ct)
    {
        var originalCtx = _ctx;
        var isolatedCtx = new WorkingContext(originalCtx.WorkspacePath, BuiltInTools);
        if (originalCtx.Messages.Count > 0 && originalCtx.Messages[0].Role == "system")
            isolatedCtx.SetSystemPrompt(originalCtx.Messages[0].Content);

        _ctx = isolatedCtx;
        try
        {
            _ctx.AddUser(prompt);
            var response = await RunLlmToolLoopAsync(_ctx.Messages, _ctx, ct);
            _ctx.AddAssistant(response.Content);
            return new AgentResponse(response.Content, _ctx.SessionId);
        }
        finally
        {
            _ctx = originalCtx;
        }
    }

    /// <summary>
    /// Process a prompt and stream text tokens back through the returned async enumerable.
    /// </summary>
    public async IAsyncEnumerable<string> ProcessStreamingAsync(
        string groupFolder,
        string prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        await EnsureSessionLoadedAsync(groupFolder, ct);

        // ── Token Budgeting ──
        _ctx.Compact(10000);

        await InjectRelevantMemoriesAsync(prompt, ct);

        _ctx.AddUser(prompt);
        if (_sessionManager is not null)
            await _sessionManager.AppendMessageAsync(_ctx.SessionId, new SessionMessage("user", prompt, DateTimeOffset.UtcNow), ct);

        var messages = new List<LlmMessage>(_ctx.Messages);

        // ── PreLlmCall hook ──
        if (_hooks is not null)
        {
            var sysPrompt = _ctx.Messages.FirstOrDefault(m => m.Role == "system")?.Content ?? "";
            var preCtx = new PreLlmCallContext
            {
                AgentName = _agentName,
                WorkspacePath = _ctx.WorkspacePath,
                SessionId = _ctx.SessionId,
                SystemPrompt = sysPrompt,
                Messages = messages,
                ModelName = _llm.Model,
                ProviderName = _llm.Name
            };
            var preResult = await _hooks.RunAsync(HookPoint.PreLlmCall, preCtx, ct);
            if (!preResult.Success)
            {
                var blockMsg = $"[Hook blocked: {preResult.StopReason}]";
                _ctx.AddAssistant(blockMsg);
                yield return blockMsg;
                yield break;
            }
            if (preCtx.SystemPrompt != sysPrompt)
                _ctx.SetSystemPrompt(preCtx.SystemPrompt);
            messages = new List<LlmMessage>(_ctx.Messages);
        }

        var fullContent = new StringBuilder();

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            // --- Streaming phase ---
            var tokenBuffer = new List<string>();
            IReadOnlyList<LlmToolCall>? toolCalls = null;
            var sawToolCallOrFallback = false;
            var isFallback = false;
            var textContent = new StringBuilder();

            var toolsToUse = GetToolDefinitions();

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
                        new LlmRequest(messages, toolsToUse, _reasoningEffort, _thinkingBudgetTokens), ct))
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
                                if (!string.IsNullOrEmpty(responseEvent.LlmResponse.Reasoning))
                                {
                                    _logger.LogInformation("LLM reasoning trace ({Length} chars): {Reasoning}",
                                        responseEvent.LlmResponse.Reasoning.Length, responseEvent.LlmResponse.Reasoning);
                                }
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
            if (_axiomValidator is not null)
                await _axiomValidator.LoadAxiomsAsync(ct);

            _ctx.AddAssistantToolCalls(textContent.ToString(), toolCalls!);
            messages = new List<LlmMessage>(_ctx.Messages);

            foreach (var toolCall in toolCalls!)
            {
                ct.ThrowIfCancellationRequested();

                var toolDef = BuiltInTools.FirstOrDefault(t => t.Name == toolCall.Name);
                if (toolDef is not null)
                {
                    var errors = ParameterValidator.Validate(toolCall, toolDef);
                    if (errors.Count > 0)
                    {
                        _ctx.AddToolResult(toolCall.Id, toolCall.Name, ParameterValidator.FormatErrors(errors));
                        continue;
                    }
                }

                // ── Axiom Validation ──
                if (_axiomValidator is not null)
                {
                    var validation = await _axiomValidator.ValidateActionAsync(toolCall.Name, JsonSerializer.Serialize(toolCall.Arguments), ct);
                    if (!validation.Success)
                    {
                        _logger.LogWarning("Axiom violation blocked: {Error}", validation.ErrorMessage);
                        _ctx.AddToolResult(toolCall.Id, toolCall.Name, validation.ErrorMessage ?? "Action blocked by security axioms.");
                        continue;
                    }
                }

                // ── PreToolUse hook ──
                if (_hooks is not null)
                {
                    var preToolCtx = new PreToolUseContext
                    {
                        AgentName = _agentName,
                        WorkspacePath = _ctx.WorkspacePath,
                        SessionId = _ctx.SessionId,
                        ToolName = toolCall.Name,
                        Arguments = JsonSerializer.SerializeToElement(toolCall.Arguments),
                        RawArguments = JsonSerializer.Serialize(toolCall.Arguments),
                        Risk = ToolRisk.Read
                    };
                    var preToolResult = await _hooks.RunAsync(HookPoint.PreToolUse, preToolCtx, ct);
                    if (preToolCtx.Denied)
                    {
                        _ctx.AddToolResult(toolCall.Id, toolCall.Name,
                            $"Tool '{toolCall.Name}' blocked: {preToolCtx.DenyReason ?? "policy"}");
                        continue;
                    }
                    if (!preToolResult.Success)
                    {
                        _ctx.AddToolResult(toolCall.Id, toolCall.Name,
                            $"Tool aborted: {preToolResult.StopReason}");
                        continue;
                    }
                }

                var result = await ExecuteToolCallAsync(toolCall, ct);

                // ── PostToolUse hook ──
                if (_hooks is not null)
                {
                    var postToolCtx = new PostToolUseContext
                    {
                        AgentName = _agentName,
                        WorkspacePath = _ctx.WorkspacePath,
                        SessionId = _ctx.SessionId,
                        ToolName = toolCall.Name,
                        Arguments = default,
                        Result = result,
                        Success = !result.StartsWith("Tool failed:")
                    };
                    await _hooks.RunAllAsync(HookPoint.PostToolUse, postToolCtx, ct);
                    if (postToolCtx.OverrideResult is not null)
                        result = postToolCtx.OverrideResult.ToString()!;
                }

                _ctx.AddToolResult(toolCall.Id, toolCall.Name, result);
            }
            messages = new List<LlmMessage>(_ctx.Messages);
        }

        // ── PostLlmCall hook ──
        if (_hooks is not null)
        {
            var postCtx = new PostLlmCallContext
            {
                AgentName = _agentName,
                WorkspacePath = _ctx.WorkspacePath,
                SessionId = _ctx.SessionId,
                Response = new LlmResponse(fullContent.ToString(), null),
                Latency = TimeSpan.Zero
            };
            await _hooks.RunAllAsync(HookPoint.PostLlmCall, postCtx, ct);
            if (postCtx.OverrideContent is not null)
                fullContent = new StringBuilder(postCtx.OverrideContent);
        }

        // Save final accumulated response
        var finalResponse = fullContent.ToString();
        _ctx.AddAssistant(finalResponse);
        if (_sessionManager is not null)
            await _sessionManager.AppendMessageAsync(_ctx.SessionId, new SessionMessage("assistant", finalResponse, DateTimeOffset.UtcNow), ct);
    }

    private async Task<LlmResponse> RunLlmToolLoopAsync(IReadOnlyList<LlmMessage> messages, WorkingContext ctx, CancellationToken ct)
    {
        if (_hooks is not null)
        {
            var sysPrompt = ctx.Messages.FirstOrDefault(m => m.Role == "system")?.Content ?? "";
            var preCtx = new PreLlmCallContext
            {
                AgentName = _agentName,
                WorkspacePath = ctx.WorkspacePath,
                SessionId = ctx.SessionId,
                SystemPrompt = sysPrompt,
                Messages = messages,
                ModelName = _llm.Model,
                ProviderName = _llm.Name
            };
            var preResult = await _hooks.RunAsync(HookPoint.PreLlmCall, preCtx, ct);
            if (!preResult.Success)
                return new LlmResponse($"[Hook blocked: {preResult.StopReason}]");
            if (preCtx.SystemPrompt != sysPrompt)
                ctx.SetSystemPrompt(preCtx.SystemPrompt);
            messages = ctx.Messages;
        }

        var tools = GetToolDefinitions();
        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            LlmResponse response;
            try
            {
                response = await CompleteWithRetryAsync(new LlmRequest(messages, tools, _reasoningEffort, _thinkingBudgetTokens), ct);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("tool use") || ex.Message.Contains("tool"))
            {
                if (tools is not null)
                {
                    tools = null;
                    response = await _llm.CompleteAsync(new LlmRequest(messages, null, _reasoningEffort, _thinkingBudgetTokens), ct);
                }
                else throw;
            }

            if (!string.IsNullOrEmpty(response.Reasoning))
            {
                _logger.LogInformation("LLM reasoning trace ({Length} chars): {Reasoning}",
                    response.Reasoning.Length, response.Reasoning);
            }

            if (response.ToolCalls is not { Count: > 0 })
            {
                if (_hooks is not null)
                {
                    var postCtx = new PostLlmCallContext
                    {
                        AgentName = _agentName,
                        WorkspacePath = ctx.WorkspacePath,
                        SessionId = ctx.SessionId,
                        Response = response,
                        Latency = TimeSpan.Zero
                    };
                    await _hooks.RunAllAsync(HookPoint.PostLlmCall, postCtx, ct);
                    if (postCtx.OverrideContent is not null)
                        response = response with { Content = postCtx.OverrideContent };
                }
                return response;
            }

            if (_axiomValidator is not null)
                await _axiomValidator.LoadAxiomsAsync(ct);

            ctx.AddAssistantToolCalls(response.Content, response.ToolCalls);
            messages = ctx.Messages;
            foreach (var toolCall in response.ToolCalls)
            {
                ct.ThrowIfCancellationRequested();

                var toolDef = BuiltInTools.FirstOrDefault(t => t.Name == toolCall.Name);
                if (toolDef is not null)
                {
                    var errors = ParameterValidator.Validate(toolCall, toolDef);
                    if (errors.Count > 0)
                    {
                        ctx.AddToolResult(toolCall.Id, toolCall.Name, ParameterValidator.FormatErrors(errors));
                        continue;
                    }
                }

                // ── Axiom Validation ──
                if (_axiomValidator is not null)
                {
                    var validation = await _axiomValidator.ValidateActionAsync(toolCall.Name, JsonSerializer.Serialize(toolCall.Arguments), ct);
                    if (!validation.Success)
                    {
                        _logger.LogWarning("Axiom violation blocked: {Error}", validation.ErrorMessage);
                        ctx.AddToolResult(toolCall.Id, toolCall.Name, validation.ErrorMessage ?? "Action blocked by security axioms.");
                        continue;
                    }
                }

                if (_hooks is not null)
                {
                    var preToolCtx = new PreToolUseContext
                    {
                        AgentName = _agentName,
                        WorkspacePath = ctx.WorkspacePath,
                        SessionId = ctx.SessionId,
                        ToolName = toolCall.Name,
                        Arguments = JsonSerializer.SerializeToElement(toolCall.Arguments),
                        RawArguments = JsonSerializer.Serialize(toolCall.Arguments),
                        Risk = ToolRisk.Read
                    };
                    var preToolResult = await _hooks.RunAsync(HookPoint.PreToolUse, preToolCtx, ct);
                    if (preToolCtx.Denied)
                    {
                        ctx.AddToolResult(toolCall.Id, toolCall.Name,
                            $"Tool '{toolCall.Name}' blocked: {preToolCtx.DenyReason ?? "policy"}");
                        continue;
                    }
                    if (!preToolResult.Success)
                    {
                        ctx.AddToolResult(toolCall.Id, toolCall.Name,
                            $"Tool aborted: {preToolResult.StopReason}");
                        continue;
                    }
                }

                var result = await ExecuteToolCallAsync(toolCall, ct);

                if (_hooks is not null)
                {
                    var postToolCtx = new PostToolUseContext
                    {
                        AgentName = _agentName,
                        WorkspacePath = ctx.WorkspacePath,
                        SessionId = ctx.SessionId,
                        ToolName = toolCall.Name,
                        Arguments = JsonSerializer.SerializeToElement(toolCall.Arguments),
                        Result = result,
                        Success = !result.StartsWith("Tool failed:")
                    };
                    await _hooks.RunAllAsync(HookPoint.PostToolUse, postToolCtx, ct);
                    if (postToolCtx.OverrideResult is not null)
                        result = postToolCtx.OverrideResult.ToString()!;
                }

                ctx.AddToolResult(toolCall.Id, toolCall.Name, result);
            }
            messages = ctx.Messages;
        }

        throw new InvalidOperationException($"AetherSoul exceeded {MaxToolIterations} tool iterations.");
    }

    private IReadOnlyList<LlmTool> GetToolDefinitions()
    {
        if (_toolRegistry is null)
            return BuiltInTools;

        var definitions = _toolRegistry.ListDefinitions(includeDisabled: false);
        if (definitions.Count == 0)
            return BuiltInTools;

        return definitions.Select(ToLlmTool).ToList();
    }

    private static LlmTool ToLlmTool(Aether.Tooling.ToolDefinition definition)
    {
        var schema = definition.ParametersSchema.ValueKind == JsonValueKind.Undefined
            ? "{}"
            : definition.ParametersSchema.GetRawText();
        return new LlmTool(definition.Name, definition.Description, schema, schema);
    }

    private async Task<string> ExecuteToolCallAsync(LlmToolCall toolCall, CancellationToken ct)
    {
        if (_registryTools is not null)
        {
            var argsJson = JsonSerializer.Serialize(toolCall.Arguments);
            var result = await _registryTools.ExecuteAsync(toolCall.Name, argsJson, ct);
            return FormatRegistryToolResult(result);
        }

        if (_legacyTools is not null)
        {
            var result = await _legacyTools.ExecuteAsync(new ToolCall(toolCall.Name, toolCall.Arguments), ct);
            return FormatToolResult(result);
        }

        return $"Tool failed: no tool executor configured for {toolCall.Name}";
    }

    private static string FormatRegistryToolResult(Aether.Tooling.ToolResult result)
    {
        if (!result.Success)
            return string.IsNullOrWhiteSpace(result.Error)
                ? "Tool failed."
                : $"Tool failed: {result.Error}";

        return result.Data switch
        {
            null => string.Empty,
            string text => text,
            _ => JsonSerializer.Serialize(result.Data, new JsonSerializerOptions { WriteIndented = false })
        };
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

    public const string CacheBoundaryMarker = "---SYSTEM_PROMPT_CACHE_BOUNDARY---";

    private static string BuildSystemPrompt(string identityContext, string? recentDiary = null)
    {
        var sb = new StringBuilder();
        // 1. Identity
        sb.AppendLine(identityContext);
        sb.AppendLine();
        // 2. Date
        sb.AppendLine($"Today is {DateTime.UtcNow:yyyy-MM-dd} (UTC).");
        sb.AppendLine();
        sb.AppendLine(CacheBoundaryMarker);
        sb.AppendLine();
        // 3. Memory
        sb.AppendLine("## Memory");
        if (!string.IsNullOrWhiteSpace(recentDiary))
        {
            sb.AppendLine("Below are excerpts from your diary for the last 2 days.");
            sb.AppendLine(recentDiary);
            sb.AppendLine();
        }
        sb.AppendLine("- Important context to persist across sessions → write to memory/YYYY-MM-DD.md");
        sb.AppendLine("- Check memory/ when starting a task — recap what's relevant.");
        sb.AppendLine();
        // 4. Rules
        sb.AppendLine("## Rules");
        sb.AppendLine("- **Reasoning:** Think deeply before every action. Use a <thought> block to plan your next steps.");
        sb.AppendLine("- **Proactivity:** Do not wait for permission. If you need to read 5 files to understand a context, read all 5. Continue looping until the task is genuinely complete.");
        sb.AppendLine("- **Evidence:** Deliver evidence, not promises. If you say you fixed something, prove it with a tool result.");
        sb.AppendLine("- **No Laziness:** Never stop after a single tool call if there is more to be done. Follow the trail of information until you reach a solid conclusion.");
        sb.AppendLine();
        // 5. Group & Plugins
        sb.AppendLine("## Group & Plugins");
        sb.AppendLine("- You operate in a group folder (groups/<name>/). Other agents may share this group.");
        sb.AppendLine("- Group has CLAUDE.md — read it when group-level context or conventions are needed.");
        sb.AppendLine("- Plugins are loaded as DLLs. Their design docs and logs are in your workspace (e.g., DESIGN.md, *.log).");
        sb.AppendLine();
        // 6. Safety
        sb.AppendLine("## Safety");
        sb.AppendLine("Refuse: self-harm, illegal activity, data exfiltration, destructive commands without confirmation.");
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
