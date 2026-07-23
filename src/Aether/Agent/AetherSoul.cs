using System.Text;
using System.Text.Json;
using Aether.Agents;
using Aether.Memory;
using Aether.Plugins;
using Aether.Providers;
using Aether.Sessions;
using Aether.Tooling;
using Aether.Tooling.DynamicTool;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RegistryToolExecutor = Aether.Tooling.ToolExecutor;
using ToolRegistry = Aether.Tooling.ToolRegistry;

namespace Aether.Agent;

public sealed class AetherSoul
{
    private const int MaxToolIterations = 64;
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
    private readonly Aether.Config.ConfigLoader? _configLoader;
    private readonly Microsoft.Extensions.Configuration.IConfiguration? _configuration;
    private readonly DynamicToolExecutor? _dynamicTools;
    private string _systemPromptStaticPrefix = "";

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
        SessionManager? sessionManager = null,
        Aether.Config.ConfigLoader? configLoader = null,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null,
        DynamicToolExecutor? dynamicTools = null)
    {
        _llm = llm;
        _legacyTools = tools;
        _dynamicTools = dynamicTools;
        _sqliteMemory = sqliteMemory;
        _sessionManager = sessionManager;
        _hooks = hooks;
        _agentName = profile.Name;
        _reasoningEffort = profile.Model?.ReasoningEffort;
        _thinkingBudgetTokens = profile.Model?.ThinkingBudgetTokens;
        _logger = logger ?? NullLogger<AetherSoul>.Instance;
        _configLoader = configLoader;
        _configuration = configuration;
        _ctx = new WorkingContext(profile.AgentDirectory, BuiltInTools);

        var identity = profile.LoadIdentityContext();
        var dailyMemory = profile.LoadDailyMemory();
        if (!string.IsNullOrWhiteSpace(identity) || !string.IsNullOrWhiteSpace(dailyMemory))
        {
            var (fullPrompt, staticPrefix) = BuildSystemPrompt(identity, dailyMemory);
            _ctx.SetSystemPrompt(fullPrompt);
            _systemPromptStaticPrefix = staticPrefix;
        }

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
        SessionManager? sessionManager = null,
        Aether.Config.ConfigLoader? configLoader = null,
        Microsoft.Extensions.Configuration.IConfiguration? configuration = null,
        DynamicToolExecutor? dynamicTools = null)
    {
        _llm = llm;
        _registryTools = tools;
        _toolRegistry = toolRegistry;
        _dynamicTools = dynamicTools;
        _sqliteMemory = sqliteMemory;
        _sessionManager = sessionManager;
        _hooks = hooks;
        _agentName = profile.Name;
        _reasoningEffort = profile.Model?.ReasoningEffort;
        _thinkingBudgetTokens = profile.Model?.ThinkingBudgetTokens;
        _logger = logger ?? NullLogger<AetherSoul>.Instance;
        _configLoader = configLoader;
        _configuration = configuration;
        _ctx = new WorkingContext(profile.AgentDirectory, GetToolDefinitions());

        var identity = profile.LoadIdentityContext();
        var dailyMemory = profile.LoadDailyMemory();
        if (!string.IsNullOrWhiteSpace(identity) || !string.IsNullOrWhiteSpace(dailyMemory))
        {
            var (fullPrompt, staticPrefix) = BuildSystemPrompt(identity, dailyMemory);
            _ctx.SetSystemPrompt(fullPrompt);
            _systemPromptStaticPrefix = staticPrefix;
        }

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
            var history = await _sessionManager.GetHistoryAsync(session.Id, 16000, ct);
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

    private string FormatToolCallUserFriendly(LlmToolCall toolCall)
    {
        var summary = $"⚙️ [{toolCall.Name}] Calling...";
        try
        {
            if (toolCall.Name == "bash")
            {
                if (toolCall.Arguments.TryGetValue("CommandLine", out var cmdVal) ||
                    toolCall.Arguments.TryGetValue("commandLine", out cmdVal))
                {
                    summary = $"⚙️ [bash] Running: `{cmdVal}`";
                }
            }
            else if (toolCall.Name == "write_to_file" || toolCall.Name == "replace_file_content" || 
                     toolCall.Name == "multi_replace_file_content" || toolCall.Name == "view_file")
            {
                if (toolCall.Arguments.TryGetValue("TargetFile", out var pathVal) ||
                    toolCall.Arguments.TryGetValue("targetFile", out pathVal) ||
                    toolCall.Arguments.TryGetValue("AbsolutePath", out pathVal) ||
                    toolCall.Arguments.TryGetValue("absolutePath", out pathVal))
                {
                    var fileBasename = System.IO.Path.GetFileName(pathVal);
                    if (string.IsNullOrEmpty(fileBasename)) fileBasename = pathVal;
                    summary = $"⚙️ [{toolCall.Name}] Path: `{fileBasename}`";
                }
            }
            else if (toolCall.Name == "grep_search")
            {
                if (toolCall.Arguments.TryGetValue("Query", out var queryVal) ||
                    toolCall.Arguments.TryGetValue("query", out queryVal))
                {
                    summary = $"⚙️ [grep] Query: `{queryVal}`";
                }
            }
        }
        catch
        {
            // Fallback to default summary
        }
        return summary;
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
            // ── Per-iteration timeout ──
            using var iterationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            iterationCts.CancelAfter(TimeSpan.FromSeconds(120)); // 2 min per iteration
            var linkedCt = iterationCts.Token;

            // --- Streaming phase ---
            var state = new StreamResultState();
            var toolsToUse = GetToolDefinitions();
            var streamingFailed = false;
            var streamOutput = new List<string>();

            try
            {
                await foreach (var token in StreamTextWithRetryAsync(messages, toolsToUse, state, fullContent, linkedCt))
                {
                    streamOutput.Add(token);
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning("Streaming iteration {Iteration} timed out after 120s. Falling back to non-streaming.", iteration);
                streamingFailed = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Streaming iteration {Iteration} failed. Falling back to non-streaming.", iteration);
                streamingFailed = true;
            }

            // Yield streaming output (outside try-catch to satisfy C# yield restrictions)
            foreach (var token in streamOutput)
            {
                yield return token;
            }

            // ── Fallback: if streaming failed, try non-streaming ──
            if (streamingFailed)
            {
                string? fallbackText = null;
                try
                {
                    var response = await CompleteWithRetryAsync(
                        new LlmRequest(messages, toolsToUse, _reasoningEffort, _thinkingBudgetTokens), linkedCt);
                    if (!string.IsNullOrEmpty(response.Content))
                    {
                        fullContent.Append(response.Content);
                        fallbackText = response.Content;
                    }
                    if (response.ToolCalls is { Count: > 0 })
                    {
                        state.ToolCalls = response.ToolCalls;
                        state.SawToolCallOrFallback = true;
                    }
                    state.IsFallback = false;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Non-streaming fallback also failed on iteration {Iteration}.", iteration);
                    yield break;
                }

                if (fallbackText is not null)
                    yield return fallbackText;
            }

            var toolCalls = state.ToolCalls;
            var sawToolCallOrFallback = state.SawToolCallOrFallback;
            var isFallback = state.IsFallback;
            var textContent = state.TextContent;

            if (!sawToolCallOrFallback || toolCalls is not { Count: > 0 })
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

                var summary = FormatToolCallUserFriendly(toolCall);
                yield return $"\n{summary}\n";
                fullContent.Append($"\n{summary}\n");

                var toolDef = BuiltInTools.FirstOrDefault(t => t.Name == toolCall.Name);
                if (toolDef is not null)
                {
                    var errors = ParameterValidator.Validate(toolCall, toolDef);
                    if (errors.Count > 0)
                    {
                        var formatErrors = ParameterValidator.FormatErrors(errors);
                        _ctx.AddToolResult(toolCall.Id, toolCall.Name, formatErrors);
                        yield return $"⚠️ [{toolCall.Name}] Validation failed.\n\n";
                        fullContent.Append($"⚠️ [{toolCall.Name}] Validation failed.\n\n");
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
                        yield return $"🛡️ [{toolCall.Name}] Axiom block: {validation.ErrorMessage ?? "Security block"}.\n\n";
                        fullContent.Append($"🛡️ [{toolCall.Name}] Axiom block: {validation.ErrorMessage ?? "Security block"}.\n\n");
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
                        yield return $"🚫 [{toolCall.Name}] Hook denied: {preToolCtx.DenyReason ?? "policy"}.\n\n";
                        fullContent.Append($"🚫 [{toolCall.Name}] Hook denied: {preToolCtx.DenyReason ?? "policy"}.\n\n");
                        continue;
                    }
                    if (!preToolResult.Success)
                    {
                        _ctx.AddToolResult(toolCall.Id, toolCall.Name,
                            $"Tool aborted: {preToolResult.StopReason}");
                        yield return $"🚫 [{toolCall.Name}] Aborted: {preToolResult.StopReason}.\n\n";
                        fullContent.Append($"🚫 [{toolCall.Name}] Aborted: {preToolResult.StopReason}.\n\n");
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

                // Yield the completion indicator
                var isFailed = result.StartsWith("Tool failed:") || result.Contains("error");
                var completionIndicator = isFailed 
                    ? $"❌ [{toolCall.Name}] Failed."
                    : $"✅ [{toolCall.Name}] Completed.";
                yield return $"{completionIndicator}\n\n";
                fullContent.Append($"{completionIndicator}\n\n");
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

    private sealed class StreamResultState
    {
        public IReadOnlyList<LlmToolCall>? ToolCalls { get; set; }
        public bool SawToolCallOrFallback { get; set; }
        public bool IsFallback { get; set; }
        public StringBuilder TextContent { get; } = new StringBuilder();
    }

    private IAsyncEnumerable<string> StreamTextWithRetryAsync(
        List<LlmMessage> messages,
        IReadOnlyList<LlmTool>? toolsToUse,
        StreamResultState state,
        StringBuilder fullContent,
        CancellationToken ct)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<string>(new System.Threading.Channels.UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = true
        });

        _ = Task.Run(async () =>
        {
            try
            {
                var currentTools = toolsToUse;
                // Extract system prompt from messages — providers handle it via SystemPrompt for caching
                var systemContent = ExtractSystemPrompt(ref messages);
                var useCaching = !string.IsNullOrEmpty(_systemPromptStaticPrefix);

                while (true)
                {
                    state.ToolCalls = null;
                    state.SawToolCallOrFallback = false;
                    state.IsFallback = false;
                    state.TextContent.Clear();

                    try
                    {
                        await foreach (var evt in _llm.CompleteStreamingEventsAsync(
                            new LlmRequest(messages, currentTools, _reasoningEffort, _thinkingBudgetTokens,
                                SystemPrompt: systemContent, UsePromptCaching: useCaching), ct))
                        {
                            switch (evt)
                            {
                                case StreamEvent.TextToken tt:
                                    state.TextContent.Append(tt.Token);
                                    fullContent.Append(tt.Token);
                                    await channel.Writer.WriteAsync(tt.Token, ct);
                                    break;

                                case StreamEvent.Response responseEvent:
                                    state.ToolCalls = responseEvent.LlmResponse.ToolCalls;
                                    state.SawToolCallOrFallback = state.ToolCalls is { Count: > 0 };
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
                        if (currentTools is not null)
                        {
                            currentTools = null;
                            state.SawToolCallOrFallback = true;
                            state.IsFallback = true;
                            continue;
                        }

                        throw;
                    }

                    break;
                }
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, ct);

        return channel.Reader.ReadAllAsync(ct);
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
            {
                ctx.SetSystemPrompt(preCtx.SystemPrompt);
            }
        }

        var tools = GetToolDefinitions();
        // Extract system prompt from messages — providers handle it via SystemPrompt for caching
        var msgList = ctx.Messages.ToList();
        var systemContent = ExtractSystemPrompt(ref msgList);
        var useCaching = !string.IsNullOrEmpty(_systemPromptStaticPrefix);

        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            LlmResponse response;
            try
            {
                response = await CompleteWithRetryAsync(new LlmRequest(msgList, tools, _reasoningEffort, _thinkingBudgetTokens,
                    SystemPrompt: systemContent, UsePromptCaching: useCaching), ct);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("tool use") || ex.Message.Contains("tool"))
            {
                if (tools is not null)
                {
                    tools = null;
                    response = await _llm.CompleteAsync(new LlmRequest(msgList, null, _reasoningEffort, _thinkingBudgetTokens,
                        SystemPrompt: systemContent, UsePromptCaching: useCaching), ct);
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
            // Refresh message list for next iteration, re-extracting system prompt
            msgList = ctx.Messages.ToList();
            systemContent = ExtractSystemPrompt(ref msgList);
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
        var previousSandbox = ToolSandboxAccessor.Current;
        if (_configLoader is not null && _configuration is not null)
        {
            var agentSpec = _configLoader.GetAgentSpec(_agentName);
            var sandboxType = _configuration["sandbox:type"] ?? "process";
            ToolSandboxAccessor.Current = new SandboxContext(_ctx.WorkspacePath, agentSpec?.Tools, sandboxType);
        }
        else
        {
            ToolSandboxAccessor.Current = new SandboxContext(_ctx.WorkspacePath);
        }

        try
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

            // Try dynamic tools as fallback
            if (_dynamicTools is not null)
            {
                var (handled, dynResult) = await _dynamicTools.TryExecuteAsync(toolCall.Name, toolCall.Arguments);
                if (handled)
                    return dynResult;
            }

            return $"Tool failed: no tool executor configured for {toolCall.Name}";
        }
        finally
        {
            ToolSandboxAccessor.Current = previousSandbox;
        }
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

    /// <summary>
    /// Extract the system message from the messages list and remove it.
    /// The system prompt is passed via LlmRequest.SystemPrompt so providers
    /// can apply prompt caching (e.g., Anthropic system parameter with cache_control).
    /// </summary>
    private static string? ExtractSystemPrompt(ref List<LlmMessage> messages)
    {
        var systemMsg = messages.FirstOrDefault(m => m.Role == "system");
        if (systemMsg is not null)
        {
            messages.RemoveAt(0); // system message is always first
            return systemMsg.Content;
        }
        return null;
    }

    /// <summary>
    /// Build a system prompt with cacheable static content first, then dynamic content.
    /// Returns the full prompt and the static prefix separately so providers can
    /// apply actual prompt caching (e.g., Anthropic system parameter with cache_control).
    /// </summary>
    private static (string FullPrompt, string StaticPrefix) BuildSystemPrompt(string identityContext, string? recentDiary = null)
    {
        var staticSb = new StringBuilder();
        // ── STATIC (cacheable) ──────────────────────────────────────────────
        staticSb.AppendLine("## Rules");
        staticSb.AppendLine("- **Reasoning:** Think deeply before every action. Use a <thought> block to plan your next steps.");
        staticSb.AppendLine("- **Proactivity:** Do not wait for permission. If you need to read 5 files to understand a context, read all 5. Continue looping until the task is genuinely complete.");
        staticSb.AppendLine("- **Evidence:** Deliver evidence, not promises. If you say you fixed something, prove it with a tool result.");
        staticSb.AppendLine("- **No Laziness:** Never stop after a single tool call if there is more to be done. Follow the trail of information until you reach a solid conclusion.");
        staticSb.AppendLine();
        staticSb.AppendLine("## Safety");
        staticSb.AppendLine("Refuse: self-harm, illegal activity, data exfiltration, destructive commands without confirmation.");
        staticSb.AppendLine();
        staticSb.AppendLine("## Group & Plugins");
        staticSb.AppendLine("- You operate in a group folder (groups/<name>/). Other agents may share this group.");
        staticSb.AppendLine("- Group has CLAUDE.md — read it when group-level context or conventions are needed.");
        staticSb.AppendLine("- Plugins are loaded as DLLs. Their design docs and logs are in your workspace (e.g., DESIGN.md, *.log).");
        staticSb.AppendLine();
        var staticPrefix = staticSb.ToString();

        var dynamicSb = new StringBuilder();
        // ── DYNAMIC (not cacheable) ─────────────────────────────────────────
        dynamicSb.AppendLine("## Identity");
        dynamicSb.AppendLine(identityContext);
        dynamicSb.AppendLine();
        dynamicSb.AppendLine($"Today is {DateTime.UtcNow:yyyy-MM-dd} (UTC).");
        dynamicSb.AppendLine();
        dynamicSb.AppendLine("## Memory");
        if (!string.IsNullOrWhiteSpace(recentDiary))
        {
            dynamicSb.AppendLine("Below are excerpts from your diary for the last 2 days.");
            dynamicSb.AppendLine(recentDiary);
            dynamicSb.AppendLine();
        }
        dynamicSb.AppendLine("- Important context to persist across sessions → write to memory/YYYY-MM-DD.md");
        dynamicSb.AppendLine("- Check memory/ when starting a task — recap what's relevant.");
        var dynamicPart = dynamicSb.ToString();

        return (staticPrefix + dynamicPart, staticPrefix);
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
