using System.Text.Json;
using System.Text.Json.Serialization;
using Aether.Tooling;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aether.Mcp;

public sealed class McpServerEndpoint : IHostedService, IDisposable
{
    private readonly ToolRegistry _toolRegistry;
    private readonly ILogger<McpServerEndpoint> _logger;
    private readonly CancellationTokenSource _shutdownCts = new();
    private Task? _listenTask;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    public McpServerEndpoint(
        ToolRegistry toolRegistry,
        ILogger<McpServerEndpoint> logger)
    {
        _toolRegistry = toolRegistry;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listenTask = ListenAsync(_shutdownCts.Token);
        _logger.LogInformation("MCP Server endpoint started on stdio");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdownCts.Cancel();
        if (_listenTask != null)
            await _listenTask.WaitAsync(TimeSpan.FromSeconds(5));
        _logger.LogInformation("MCP Server endpoint stopped");
    }

    private async Task ListenAsync(CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(Console.OpenStandardInput());
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                var response = await HandleRequestAsync(line, ct);
                var json = JsonSerializer.Serialize(response, JsonOptions);
                await Console.Out.WriteLineAsync(json);
                await Console.Out.FlushAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP listen loop terminated unexpectedly");
        }
    }

    internal async Task<JsonRpcMessage> HandleRequestAsync(string requestJson, CancellationToken ct)
    {
        JsonRpcMessage? request;
        try
        {
            request = JsonSerializer.Deserialize<JsonRpcMessage>(requestJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ErrorResponse(null, -32700, $"Invalid JSON: {ex.Message}");
        }

        if (request == null)
            return ErrorResponse(null, -32700, "Empty request");
        if (string.IsNullOrEmpty(request.Method))
            return ErrorResponse(request.Id, -32601, "Method not specified");

        _logger.LogDebug("MCP request: {Method} (id={Id})", request.Method, request.Id);

        try
        {
            return request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "tools/list" => await HandleListToolsAsync(),
                "tools/call" => await HandleCallToolAsync(request, ct),
                "notifications/initialized" => EmptyResponse(request.Id),
                _ => ErrorResponse(request.Id, -32601, $"Method not found: {request.Method}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in MCP method: {Method}", request.Method);
            return ErrorResponse(request.Id, -32603, $"Internal error: {ex.Message}");
        }
    }

    private static JsonRpcMessage HandleInitialize(JsonRpcMessage request)
    {
        var result = new Dictionary<string, object>
        {
            ["protocolVersion"] = "2025-03-26",
            ["serverInfo"] = new Dictionary<string, object>
            {
                ["name"] = "aether",
                ["version"] = "3.0.1"
            },
            ["capabilities"] = new Dictionary<string, object>
            {
                ["tools"] = new Dictionary<string, object>(),
                ["resources"] = new Dictionary<string, object> { ["subscribe"] = false }
            }
        };

        return new JsonRpcMessage
        {
            Jsonrpc = "2.0",
            Id = request.Id,
            Result = JsonSerializer.SerializeToElement(result, JsonOptions)
        };
    }

    private Task<JsonRpcMessage> HandleListToolsAsync()
    {
        var toolDefs = _toolRegistry.ListDefinitions();
        var toolList = new List<Dictionary<string, object>>();

        foreach (var def in toolDefs)
        {
            toolList.Add(new Dictionary<string, object>
            {
                ["name"] = def.Name,
                ["description"] = def.Description,
                ["inputSchema"] = new Dictionary<string, object>
                {
                    ["type"] = "object",
                    ["properties"] = new Dictionary<string, object>()
                }
            });
        }

        var result = new Dictionary<string, object> { ["tools"] = toolList };
        return Task.FromResult(new JsonRpcMessage
        {
            Jsonrpc = "2.0",
            Id = "1",
            Result = JsonSerializer.SerializeToElement(result, JsonOptions)
        });
    }

    private async Task<JsonRpcMessage> HandleCallToolAsync(JsonRpcMessage request, CancellationToken ct)
    {
        if (request.Params == null)
            return ErrorResponse(request.Id, -32602, "Missing params");

        var callRequest = request.Params.Value.Deserialize<CallToolRequest>(JsonOptions);
        if (callRequest == null || string.IsNullOrEmpty(callRequest.Name))
            return ErrorResponse(request.Id, -32602, "Invalid params: name required");

        var toolDef = _toolRegistry.Resolve(callRequest.Name);
        if (toolDef == null)
            return ErrorResponse(request.Id, -32602, $"Tool not found: {callRequest.Name}");

        var argsJson = callRequest.Arguments != null
            ? JsonSerializer.Serialize(callRequest.Arguments, JsonOptions)
            : "{}";
        var argsElement = JsonSerializer.Deserialize<JsonElement>(argsJson);

        var result = await toolDef.Execute(argsElement, ct);
        var resultStr = result?.ToString() ?? "";

        var response = new CallToolResponse
        {
            Content = new List<McpContentPart>
            {
                new() { Type = "text", Text = resultStr }
            }
        };

        return new JsonRpcMessage
        {
            Jsonrpc = "2.0",
            Id = request.Id,
            Result = JsonSerializer.SerializeToElement(response, JsonOptions)
        };
    }

    private static JsonRpcMessage EmptyResponse(string? id)
    {
        return new JsonRpcMessage
        {
            Jsonrpc = "2.0",
            Id = id,
            Result = JsonSerializer.SerializeToElement(new { }, JsonOptions)
        };
    }

    private static JsonRpcMessage ErrorResponse(string? id, int code, string message)
    {
        return new JsonRpcMessage
        {
            Jsonrpc = "2.0",
            Id = id,
            Error = new JsonRpcError { Code = code, Message = message }
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _shutdownCts.Cancel();
            _shutdownCts.Dispose();
            _disposed = true;
        }
    }
}
