using System.Text;
using Aether.Agents;
using Aether.Providers;
using Aether.Tooling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Agent;

public sealed class AetherSoul
{
    private const int MaxToolIterations = 8;
    private readonly ILLMProvider _llm;
    private readonly ToolExecutor _tools;
    private readonly ILogger<AetherSoul> _logger;

    private WorkingContext _ctx;

    public AetherSoul(
        ILLMProvider llm,
        ToolExecutor tools,
        AgentProfile profile,
        ILogger<AetherSoul>? logger = null)
    {
        _llm = llm;
        _tools = tools;
        _logger = logger ?? NullLogger<AetherSoul>.Instance;
        _ctx = new WorkingContext(profile.AgentDirectory, BuiltInTools);

        // Identity context loaded once, sync — stays resident in WorkingContext for session lifetime.
        var identity = profile.LoadIdentityContext();
        if (!string.IsNullOrWhiteSpace(identity))
            _ctx.SetSystemPrompt(BuildSystemPrompt(identity));
    }

    public void Reset() => _ctx.Reset();

    /// <summary>
    /// Process a task prompt — minimal path, no persona loading.
    /// </summary>
    public Task<AgentResponse> ProcessTaskAsync(string groupFolder, string prompt, CancellationToken ct = default)
        => ProcessAsync(groupFolder, prompt, ct);

    public Task<AgentResponse> ProcessTaskAsync(string groupFolder, string prompt, string? workingStateOverride, CancellationToken ct = default)
        => ProcessAsync(groupFolder, prompt, ct);

    public async Task<AgentResponse> ProcessAsync(string groupFolder, string prompt, CancellationToken ct = default)
    {
        _ctx.AddUser(prompt);

        var response = await RunLlmToolLoopAsync(_ctx.Messages, ct);

        _ctx.AddAssistant(response.Content);

        return new AgentResponse(response.Content, _ctx.SessionId);
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

                var result = _tools.Execute(new ToolCall(toolCall.Name, toolCall.Arguments));
                _ctx.AddToolResult(toolCall.Id, toolCall.Name, FormatToolResult(result));
            }
            messages = new List<LlmMessage>(_ctx.Messages);
            // Continue loop to stream the LLM's response to tool results
        }

        // Save final accumulated response
        _ctx.AddAssistant(fullContent.ToString());
    }

    private async Task<LlmResponse> RunLlmToolLoopAsync(IReadOnlyList<LlmMessage> messages, CancellationToken ct)
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

            if (!string.IsNullOrEmpty(response.Reasoning))
            {
                _logger.LogInformation("LLM reasoning trace ({Length} chars): {Reasoning}",
                    response.Reasoning.Length, response.Reasoning);
            }

            if (response.ToolCalls is not { Count: > 0 })
            {
                return response;
            }

            _ctx.AddAssistantToolCalls(response.Content, response.ToolCalls);
            messages = _ctx.Messages;
            foreach (var toolCall in response.ToolCalls)
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

                var result = _tools.Execute(new ToolCall(toolCall.Name, toolCall.Arguments));
                _ctx.AddToolResult(toolCall.Id, toolCall.Name, FormatToolResult(result));
            }
            messages = _ctx.Messages;
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
