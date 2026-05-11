using System.Text;
using System.Text.Json;
using Aether.Agents;
using Aether.Plugins;
using Aether.Providers;
using Aether.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RegistryToolExecutor = Aether.Tooling.ToolExecutor;
using ToolRegistry = Aether.Tooling.ToolRegistry;

namespace Aether.Agent;

public sealed class AetherSoul
{
    private const int MaxToolIterations = 8;
    private readonly ILLMProvider _llm;
    private readonly ToolExecutor? _legacyTools;
    private readonly RegistryToolExecutor? _registryTools;
    private readonly ToolRegistry? _toolRegistry;
    private readonly HookEngine? _hooks;
    private readonly string _agentName;
    private readonly ILogger<AetherSoul> _logger;

    private WorkingContext _ctx;

    public AetherSoul(
        ILLMProvider llm,
        ToolExecutor tools,
        AgentProfile profile,
        ILogger<AetherSoul>? logger = null,
        HookEngine? hooks = null)
    {
        _llm = llm;
        _legacyTools = tools;
        _hooks = hooks;
        _agentName = profile.Name;
        _logger = logger ?? NullLogger<AetherSoul>.Instance;
        _ctx = new WorkingContext(profile.AgentDirectory, BuiltInTools);

        var identity = profile.LoadIdentityContext();
        if (!string.IsNullOrWhiteSpace(identity))
            _ctx.SetSystemPrompt(BuildSystemPrompt(identity));
    }

    public AetherSoul(
        ILLMProvider llm,
        RegistryToolExecutor tools,
        ToolRegistry toolRegistry,
        AgentProfile profile,
        ILogger<AetherSoul>? logger = null,
        HookEngine? hooks = null)
    {
        _llm = llm;
        _registryTools = tools;
        _toolRegistry = toolRegistry;
        _hooks = hooks;
        _agentName = profile.Name;
        _logger = logger ?? NullLogger<AetherSoul>.Instance;
        _ctx = new WorkingContext(profile.AgentDirectory, GetToolDefinitions());

        var identity = profile.LoadIdentityContext();
        if (!string.IsNullOrWhiteSpace(identity))
            _ctx.SetSystemPrompt(BuildSystemPrompt(identity));
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
        _ctx.AddUser(prompt);

        var response = await RunLlmToolLoopAsync(_ctx.Messages, _ctx, ct);

        _ctx.AddAssistant(response.Content);

        return new AgentResponse(response.Content, _ctx.SessionId);
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
        _ctx.AddUser(prompt);

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
        _ctx.AddAssistant(fullContent.ToString());
    }

    private async Task<LlmResponse> RunLlmToolLoopAsync(IReadOnlyList<LlmMessage> messages, WorkingContext ctx, CancellationToken ct)
    {
        var tools = GetToolDefinitions();
        for (var iteration = 0; iteration < MaxToolIterations; iteration++)
        {
            LlmResponse response;
            try
            {
                response = await _llm.CompleteAsync(new LlmRequest(messages, tools), ct);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("tool use") || ex.Message.Contains("tool"))
            {
                if (tools is not null)
                {
                    tools = null;
                    response = await _llm.CompleteAsync(new LlmRequest(messages, null), ct);
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
                return response;
            }

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

                var result = await ExecuteToolCallAsync(toolCall, ct);
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

    private static string BuildSystemPrompt(string identityContext)
    {
        var sb = new StringBuilder();
        sb.AppendLine(identityContext);
        sb.AppendLine();
        sb.AppendLine($"Today is {DateTime.UtcNow:yyyy-MM-dd} (UTC).");
        sb.AppendLine();
        sb.AppendLine(CacheBoundaryMarker);
        sb.AppendLine();
        sb.AppendLine("## Rules");
        sb.AppendLine("- Clear request → act immediately. Don't describe — do.");
        sb.AppendLine("- Continue until done or genuinely blocked.");
        sb.AppendLine("- Read before write/edit. Minimal scope.");
        sb.AppendLine("- Deliver evidence, not promises.");
        sb.AppendLine();
        sb.AppendLine("## Memory");
        sb.AppendLine("- Important context to persist across sessions → write to memory/YYYY-MM-DD.md");
        sb.AppendLine("- Check memory/ when starting a task — recap what's relevant.");
        sb.AppendLine();
        sb.AppendLine("## Group");
        sb.AppendLine("- You operate in a group folder (groups/<name>/). Other agents may share this group.");
        sb.AppendLine("- Group has CLAUDE.md — read it when group-level context or conventions are needed.");
        sb.AppendLine();
        sb.AppendLine("## Skills");
        sb.AppendLine("- Skills are capability modules in skills/ — read the matching skill before a specialized task.");
        sb.AppendLine("- Use the `read` tool to load a skill when the task matches its domain.");
        sb.AppendLine();
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
