using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Aether.Data;
using Aether.Providers;
using Aether.SelfImprovement;
using Aether.Sessions;
using Aether.Skills;
using Aether.Ui;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aether.Channels;

/// <summary>
/// WebSocket channel implementing IChannel for non-Telegram clients (web UI, CLI tools, etc).
/// Hosts an HttpListener on a configurable port serving both WebSocket upgrade (/ws) and
/// HTTP routes (/memory/maria/*) on the same port. Each WS connection gets a unique chat_id.
/// </summary>
public sealed class WebSocketChannel : IChannel, IDisposable
{
    private readonly int _port;
    private readonly ILogger<WebSocketChannel> _logger;
    private readonly ProviderRouter? _providerRouter;
    private readonly SlashCommandHandler? _slashCommandHandler;
    private readonly SessionManager? _sessionManager;
    private readonly IServiceProvider? _services;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;
    private bool _disposed;

    /// <summary>
    /// Maps assigned chat_ids to their active WebSocket connections.
    /// Thread-safe since connections are added/removed from accept and receive loops.
    /// </summary>
    private readonly ConcurrentDictionary<string, WebSocketConnection> _connections = new();

    /// <summary>
    /// Counter for assigning unique chat IDs.
    /// </summary>
    private long _nextConnectionId;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };

    public string Name => "websocket";
    public bool IsConnected => _listener is not null;

    public event EventHandler<InboundMessage>? OnMessage;
#pragma warning disable CS0067
    public event Func<string, Ui.UiCallback, Task<Ui.UiDocument?>>? OnUiCallback;
#pragma warning restore CS0067

    /// <summary>
    /// The actual port the listener is bound to. Useful when passing port 0 for tests.
    /// </summary>
    public int BoundPort { get; private set; }

    public WebSocketChannel(
        int port,
        ILogger<WebSocketChannel> logger,
        ProviderRouter? providerRouter = null,
        SlashCommandHandler? slashCommandHandler = null,
        SessionManager? sessionManager = null,
        IServiceProvider? services = null)
    {
        _port = port;
        _logger = logger;
        _providerRouter = providerRouter;
        _slashCommandHandler = slashCommandHandler;
        _sessionManager = sessionManager;
        _services = services;
    }

        public Task ConnectAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _listener = new HttpListener();
        // When port is 0 (test mode), HttpListener doesn't support dynamic port assignment,
        // so the caller must provide a concrete port. Prefix uses + for all interfaces but
        // we scope to localhost for security.
        _listener.Prefixes.Add($"http://localhost:{_port}/");
        _listener.Start();
        BoundPort = _port;

        _logger.LogInformation("Aether channel listening on http://localhost:{Port}/ (WS /ws + HTTP /memory/maria/* + Web UI)", BoundPort);

        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);

        return Task.CompletedTask;
    }

    public async Task DisconnectAsync(CancellationToken ct)
    {
        _cts?.Cancel();

        // Close all active connections gracefully
        foreach (var (chatId, conn) in _connections.ToArray())
        {
            await CloseConnectionAsync(chatId, conn, WebSocketCloseStatus.NormalClosure, "Server shutting down", ct);
        }

        if (_listener is not null)
        {
            try
            {
                _listener.Stop();
            }
            catch (ObjectDisposedException) { }
        }

        if (_acceptLoopTask is not null)
        {
            try
            {
                await _acceptLoopTask.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException)
            {
                _logger.LogWarning("WebSocket accept loop did not stop within 5 seconds");
            }
        }

        _logger.LogInformation("WebSocket channel disconnected");
    }

    private WebSocketConnection? GetConnection(string chatId)
    {
        if (_connections.TryGetValue(chatId, out var conn))
            return conn;

        var parts = chatId.Split(':');
        if (parts.Length > 2)
        {
            var baseChatId = $"{parts[0]}:{parts[1]}";
            if (_connections.TryGetValue(baseChatId, out conn))
                return conn;
        }

        return null;
    }

    public async Task SendMessageAsync(string chatId, string text, CancellationToken ct)
    {
        var conn = GetConnection(chatId);
        if (conn is null)
        {
            _logger.LogWarning("Cannot send message to unknown chat_id: {ChatId}", chatId);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "message",
            text,
            message_id = Guid.NewGuid().ToString("N")
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);
    }

    public async Task SetTypingAsync(string chatId, bool isTyping, CancellationToken ct)
    {
        var conn = GetConnection(chatId);
        if (conn is null)
            return;

        var payload = JsonSerializer.Serialize(new
        {
            type = "typing",
            status = isTyping ? "typing" : "idle"
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);
    }

    public async Task SendStreamingChunkAsync(string chatId, string chunk, int chunkIndex, CancellationToken ct)
    {
        var conn = GetConnection(chatId);
        if (conn is null)
            return;

        var payload = JsonSerializer.Serialize(new
        {
            type = "chunk",
            text = chunk,
            index = chunkIndex
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);
    }

    public async Task SendStreamingCompleteAsync(string chatId, string fullText, CancellationToken ct)
    {
        var conn = GetConnection(chatId);
        if (conn is null)
            return;

        var payload = JsonSerializer.Serialize(new
        {
            type = "complete",
            text = fullText,
            message_id = Guid.NewGuid().ToString("N")
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);
    }

    public bool OwnsChatId(string chatId)
    {
        return chatId.StartsWith("websocket:", StringComparison.Ordinal)
            && GetConnection(chatId) is not null;
    }

    /// <summary>
        /// <summary>
    /// Accept loop: gets HttpListener contexts, dispatches by path.
    /// /ws → WebSocket upgrade; /memory/maria/* → HTTP route handler; else 404.
    /// </summary>
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener!.GetContextAsync();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested) { break; }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Aether channel accept loop error");
                continue;
            }

            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (path == "/ws")
            {
                _ = Task.Run(() => HandleWsUpgradeAsync(ctx, ct), ct);
            }
            else if (path.StartsWith("/memory/maria/", StringComparison.Ordinal))
            {
                _ = Task.Run(() => HandleHttpRouteAsync(ctx, ct), ct);
            }
            else
            {
                _ = Task.Run(() => ServeStaticFileAsync(ctx, ct), ct);
            }
        }
    }

    private static string? _webRoot;

    private static string? ResolveWebRoot()
    {
        if (_webRoot is not null) return _webRoot;

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "web"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "web"),
            Path.Combine(Environment.CurrentDirectory, "web"),
            Path.Combine(Environment.CurrentDirectory, "src", "Aether", "web"),
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (Directory.Exists(full))
            {
                _webRoot = full;
                break;
            }
        }

        return _webRoot;
    }

    private static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".txt" => "text/plain; charset=utf-8",
            ".map" => "application/json; charset=utf-8",
            _ => "application/octet-stream",
        };
    }

    private async Task ServeStaticFileAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var response = ctx.Response;
        var webRoot = ResolveWebRoot();

        if (webRoot is null)
        {
            try
            {
                response.StatusCode = 404;
                response.ContentType = "text/plain";
                var body = Encoding.UTF8.GetBytes("Web UI not found. Run from the project root or build with web assets.");
                response.ContentLength64 = body.Length;
                await response.OutputStream.WriteAsync(body, ct);
                response.Close();
            }
            catch { }
            return;
        }

        var requestPath = ctx.Request.Url?.AbsolutePath ?? "/";
        if (requestPath == "/") requestPath = "/index.html";

        // Prevent path traversal: resolve and check containment
        var filePath = Path.GetFullPath(Path.Combine(webRoot, requestPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        var webRootFull = Path.GetFullPath(webRoot);
        if (!filePath.StartsWith(webRootFull, StringComparison.Ordinal))
        {
            try { response.StatusCode = 403; response.Close(); } catch { }
            return;
        }

        // SPA fallback: if the path has no extension and the file doesn't exist, serve index.html
        if (!File.Exists(filePath) && !Path.HasExtension(requestPath))
        {
            filePath = Path.Combine(webRootFull, "index.html");
        }

        if (!File.Exists(filePath))
        {
            try { response.StatusCode = 404; response.Close(); } catch { }
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(filePath, ct);
            response.StatusCode = 200;
            response.ContentType = GetMimeType(filePath);
            response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes, ct);
            response.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to serve static file: {Path}", requestPath);
            try { response.StatusCode = 500; response.Close(); } catch { }
        }
    }

    /// <summary>
    /// Performs the WebSocket upgrade via HttpListener, then enters the receive loop.
    /// </summary>
        private async Task HandleWsUpgradeAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        HttpListenerWebSocketContext? wsContext = null;
        var chatId = "";
        try
        {
            wsContext = await ctx.AcceptWebSocketAsync(subProtocol: null);
            var connectionId = Interlocked.Increment(ref _nextConnectionId);
            chatId = $"websocket:{connectionId}";

            var conn = new WebSocketConnection(chatId, wsContext.WebSocket, ctx.Request.RemoteEndPoint);
            _connections[chatId] = conn;

            _logger.LogInformation("WebSocket client connected: {RemoteEndPoint} -> {ChatId}", ctx.Request.RemoteEndPoint, chatId);

            // Send a welcome message with the assigned chat_id
            var welcome = JsonSerializer.Serialize(new
            {
                type = "connected",
                chat_id = chatId,
                text = "Connected to Aether. Send JSON messages to chat."
            }, JsonOptions);
            await SendJsonAsync(conn, welcome, ct);

            await ReceiveLoopAsync(conn, ct);
        }
        catch (WebSocketException ex)
        {
            // AcceptWebSocketAsync throws if the request is not a valid WS upgrade.
            // Return 426 Upgrade Required so plain HTTP clients get a clear status.
            _logger.LogWarning(ex, "WebSocket upgrade failed — returning 426");
            try
            {
                ctx.Response.StatusCode = 426;
                ctx.Response.StatusDescription = "Upgrade Required";
                ctx.Response.ContentType = "text/plain";
                var body = Encoding.UTF8.GetBytes("This endpoint accepts WebSocket connections at ws:// url scheme.");
                ctx.Response.ContentLength64 = body.Length;
                await ctx.Response.OutputStream.WriteAsync(body, ct);
                ctx.Response.Close();
            }
            catch { /* response already sent or client gone */ }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebSocket connection");
        }
        finally
        {
            if (!string.IsNullOrEmpty(chatId))
            {
                _connections.TryRemove(chatId, out _);
                _logger.LogInformation("WebSocket client disconnected: {ChatId}", chatId);
            }
            wsContext?.WebSocket?.Dispose();
        }
    }

    /// <summary>
    /// Handle HTTP GET routes under /memory/maria/* — dispatches to MariaMemoryHost.
    /// </summary>
    private async Task HandleHttpRouteAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? "";
        try
        {
                        if (path == "/memory/maria/recall" && ctx.Request.HttpMethod == "GET")
            {
                var query = ctx.Request.QueryString["query"];
                if (string.IsNullOrWhiteSpace(query))
                {
                    await WriteJsonAsync(ctx.Response, 400, new { success = false, error = "Missing 'query' parameter" });
                    return;
                }
                var limitStr = ctx.Request.QueryString["limit"] ?? "10";
                int.TryParse(limitStr, out var limit);
                if (limit <= 0) limit = 10;
                var result = await MariaMemoryHost.SearchAsync(query, limit, ct);
                await WriteJsonAsync(ctx.Response, 200, result);
            }
            else if (path == "/memory/maria/nodes" && ctx.Request.HttpMethod == "GET")
            {
                var result = await MariaMemoryHost.GetAllNodesAsync(100, ct);
                await WriteJsonAsync(ctx.Response, 200, result);
            }
            else if (path == "/memory/maria/nodes" && ctx.Request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(ctx.Request.InputStream);
                var body = await reader.ReadToEndAsync(ct);
                var result = await MariaMemoryHost.AppendNodeAsync(body, ct);
                var statusCode = result.TryGetProperty("success", out var successProp) && successProp.GetBoolean() ? 200 : 400;
                await WriteJsonAsync(ctx.Response, statusCode, result);
            }
            else
            {
                await WriteJsonAsync(ctx.Response, 404, new { success = false, error = "Not Found" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling HTTP route {Path}", path);
            await WriteJsonAsync(ctx.Response, 500, new { success = false, error = ex.Message });
        }
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, int status, object data)
    {
        response.StatusCode = status;
        response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = bytes.Length;
        try
        {
            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        }
        catch { /* client disconnected */ }
    }

        private async Task ReceiveLoopAsync(WebSocketConnection conn, CancellationToken ct)
    {
        var buffer = new byte[1024 * 64]; // 64KB receive buffer
        var messageBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested && conn.WebSocket.State == WebSocketState.Open)
        {
            try
            {
                var result = await conn.WebSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await conn.WebSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Client disconnected",
                        CancellationToken.None);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var json = messageBuilder.ToString();
                        messageBuilder.Clear();
                        await ProcessIncomingJsonAsync(conn, json, ct);
                    }
                }
            }
            catch (WebSocketException ex)
            {
                _logger.LogWarning(ex, "WebSocket receive error for {ChatId}", conn.ChatId);
                break;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in WebSocket receive loop for {ChatId}", conn.ChatId);
                break;
            }
        }
    }

    private async Task ProcessIncomingJsonAsync(WebSocketConnection conn, string json, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeProp))
            {
                await SendErrorAsync(conn, "Missing 'type' field in JSON message", ct);
                return;
            }

            var type = typeProp.GetString();

            switch (type)
            {
                case "message":
                    await HandleMessageTypeAsync(conn, root, ct);
                    break;

                case "ping":
                    var pong = JsonSerializer.Serialize(new { type = "pong" }, JsonOptions);
                    await SendJsonAsync(conn, pong, ct);
                    break;

                case "cancel":
                    var cancelGroup = root.TryGetProperty("group", out var cancelGroupProp)
                        ? cancelGroupProp.GetString() ?? "main"
                        : "main";
                    var sessionKey = $"{conn.ChatId}:{cancelGroup}";
                    if (SessionCancellationRegistry.TryGet(sessionKey, out var activeCts) && activeCts is not null)
                    {
                        activeCts.Cancel();
                        _logger.LogInformation("Cancelled active generation for session: {SessionKey}", sessionKey);
                    }
                    break;

                case "list_models":
                    await HandleListModelsAsync(conn, ct);
                    break;

                case "get_history":
                    await HandleGetHistoryAsync(conn, root, ct);
                    break;

                case "command":
                    await HandleCommandAsync(conn, root, ct);
                    break;

                case "get_goals":
                    await HandleGetGoalsAsync(conn, root, ct);
                    break;

                case "get_skills":
                    await HandleGetSkillsAsync(conn, ct);
                    break;

                case "get_metrics":
                    await HandleGetMetricsAsync(conn, ct);
                    break;

                case "get_telemetry":
                    await HandleGetTelemetryAsync(conn, ct);
                    break;

                case "git_status":
                    await HandleGitStatusAsync(conn, ct);
                    break;

                case "context_update":
                    await HandleContextUpdateAsync(conn, root, ct);
                    break;

                case "stage_file":
                    await HandleStageFileAsync(conn, root, ct);
                    break;

                default:
                    await SendErrorAsync(conn, $"Unknown message type: '{type}'", ct);
                    break;
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON from {ChatId}", conn.ChatId);
            await SendErrorAsync(conn, "Invalid JSON", ct);
        }
    }

    private async Task HandleMessageTypeAsync(WebSocketConnection conn, JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("text", out var textProp) || string.IsNullOrWhiteSpace(textProp.GetString()))
        {
            await SendErrorAsync(conn, "Missing or empty 'text' field in message", ct);
            return;
        }

        var text = textProp.GetString()!;
        var group = root.TryGetProperty("group", out var groupProp)
            ? groupProp.GetString() ?? "main"
            : "main";

        var messageId = Guid.NewGuid().ToString("N");

        var inbound = new InboundMessage(
            Id: messageId,
            ChannelName: "websocket",
            ChatId: $"{conn.ChatId}:{group}",
            SenderId: $"ws:{conn.RemoteEndPoint}",
            Text: text,
            Timestamp: DateTimeOffset.UtcNow);

        _logger.LogDebug(
            "WebSocket message from {ChatId}: {Text} (group={Group})",
            inbound.ChatId, text, group);

        OnMessage?.Invoke(this, inbound);
    }

    private async Task HandleListModelsAsync(WebSocketConnection conn, CancellationToken ct)
    {
        if (_providerRouter is null)
        {
            await SendErrorAsync(conn, "list_models not available: ProviderRouter not injected", ct);
            return;
        }

        var available = _providerRouter.GetAvailableModels();
        var current = _providerRouter.EffectiveModel ?? "none";

        // Group by provider name
        var grouped = available
            .GroupBy(m => m.Provider)
            .Select(g => new
            {
                name = g.Key,
                models = g.Select(m => m.Model).ToList()
            })
            .ToList();

        // Check for ThinkEffort property via reflection (may not exist on all builds)
        string? thinkEffort = null;
        var thinkProp = typeof(ProviderRouter).GetProperty("ThinkEffort");
        if (thinkProp is not null)
            thinkEffort = thinkProp.GetValue(_providerRouter)?.ToString();

        var payload = JsonSerializer.Serialize(new
        {
            type = "models",
            current,
            think_effort = thinkEffort,
            providers = grouped
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);

        _logger.LogDebug("list_models response sent to {ChatId}: {Count} providers",
            conn.ChatId, grouped.Count);
    }

    private async Task HandleGetHistoryAsync(
        WebSocketConnection conn, JsonElement root, CancellationToken ct)
    {
        if (_sessionManager is null)
        {
            await SendErrorAsync(conn, "get_history not available: SessionManager not injected", ct);
            return;
        }

        var group = root.TryGetProperty("group", out var groupProp)
            ? groupProp.GetString() ?? "main"
            : "main";

        var limit = root.TryGetProperty("limit", out var limitProp)
            ? limitProp.GetInt32()
            : 50;

        var session = await _sessionManager.GetOrCreateSessionAsync(group, ct);
        var history = await _sessionManager.GetHistoryAsync(session.Id, maxTokens: 20000, ct);

        var messages = history
            .Take(limit)
            .Select(m => new
            {
                role = m.Role.ToLowerInvariant(),
                content = m.Content,
                timestamp = m.Timestamp.ToString("o")
            })
            .ToList();

        var payload = JsonSerializer.Serialize(new
        {
            type = "history",
            messages
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);

        _logger.LogDebug("get_history for group={Group}: {Count} messages sent to {ChatId}",
            group, messages.Count, conn.ChatId);
    }

    private async Task HandleCommandAsync(
        WebSocketConnection conn, JsonElement root, CancellationToken ct)
    {
        if (_slashCommandHandler is null)
        {
            await SendErrorAsync(conn, "command not available: SlashCommandHandler not injected", ct);
            return;
        }

        if (!root.TryGetProperty("text", out var textProp)
            || string.IsNullOrWhiteSpace(textProp.GetString()))
        {
            await SendErrorAsync(conn, "Missing 'text' field in command", ct);
            return;
        }

        var text = textProp.GetString()!;
        var group = root.TryGetProperty("group", out var groupProp)
            ? groupProp.GetString() ?? "main"
            : "main";

        // Resolve workspace path for the group (empty string fallback = root)
        var workspacePath = string.Empty;
        if (_services is not null)
        {
            var configLoader = _services.GetService(typeof(Aether.Config.ConfigLoader)) as Aether.Config.ConfigLoader;
            if (configLoader is not null)
            {
                var agentConfig = configLoader.GetAgentConfig(group);
                if (agentConfig is not null && !string.IsNullOrEmpty(agentConfig.Workspace))
                {
                    workspacePath = agentConfig.Workspace;
                }
            }
        }

        var ctx = new SlashCommandContext(
            Text: text,
            AgentName: group,
            WorkspacePath: workspacePath,
            Services: _services ?? new EmptyServiceProvider());

        SlashCommandResult? result;
        try
        {
            result = await _slashCommandHandler.HandleAsync(ctx, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SlashCommandHandler threw for command: {Text}", text);
            await SendErrorAsync(conn, $"Command error: {ex.Message}", ct);
            return;
        }

        var responseText = result?.Text ?? "Unknown command";
        var messageId = Guid.NewGuid().ToString("N");

        var payload = JsonSerializer.Serialize(new
        {
            type = "message",
            text = responseText,
            message_id = messageId
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);

        _logger.LogDebug("command '{Text}' for group={Group} sent to {ChatId}",
            text, group, conn.ChatId);
    }

    private async Task HandleGetGoalsAsync(WebSocketConnection conn, JsonElement root, CancellationToken ct)
    {
        if (_services is null)
        {
            await SendErrorAsync(conn, "get_goals not available: services not injected", ct);
            return;
        }

        var agentId = root.TryGetProperty("agent_id", out var agentProp) && agentProp.ValueKind == JsonValueKind.String
            ? agentProp.GetString() ?? "maria"
            : "maria";

        List<Goal> goals;
        using (var scope = _services.CreateScope())
        {
            var goalStore = scope.ServiceProvider.GetRequiredService<GoalStore>();
            goals = await goalStore.GetActiveGoalsAsync(agentId, ct);
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "goals",
            goals = goals.Select(g => new
            {
                id = g.Id,
                title = g.Title,
                description = g.Description,
                status = g.Status,
                priority = g.Priority,
                created_at = g.CreatedAt.ToString("o"),
                deadline = g.Deadline?.ToString("o")
            })
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);

        _logger.LogDebug("get_goals for agent={Agent}: {Count} goals sent to {ChatId}",
            agentId, goals.Count, conn.ChatId);
    }

    private async Task HandleGetSkillsAsync(WebSocketConnection conn, CancellationToken ct)
    {
        if (_services is null)
        {
            await SendErrorAsync(conn, "get_skills not available: services not injected", ct);
            return;
        }

        var skillRegistry = _services.GetService<SkillRegistry>();
        if (skillRegistry is null)
        {
            await SendErrorAsync(conn, "SkillRegistry not available", ct);
            return;
        }

        var skills = skillRegistry.List().Select(s => new
        {
            name = s.Name,
            description = s.Description,
            when_to_use = s.WhenToUse,
            tools = s.Tools,
            auto_apply = s.AutoApply,
            trigger_mode = s.TriggerMode.ToString()
        }).ToList();

        var payload = JsonSerializer.Serialize(new
        {
            type = "skills",
            skills
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);

        _logger.LogDebug("get_skills: {Count} skills sent to {ChatId}",
            skills.Count, conn.ChatId);
    }

    private async Task HandleGetMetricsAsync(WebSocketConnection conn, CancellationToken ct)
    {
        if (_services is null)
        {
            await SendErrorAsync(conn, "get_metrics not available: services not injected", ct);
            return;
        }

        var pipelineTracker = _services.GetService<PipelineTracker>();
        var skillEvolution = _services.GetService<SkillEvolution>();

        IReadOnlyList<TrackedCandidate> candidates = Array.Empty<TrackedCandidate>();
        if (pipelineTracker is not null)
        {
            candidates = await pipelineTracker.GetCandidatesAsync(ct);
        }

        var pipelineStates = candidates
            .GroupBy(c => c.State.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        int recidivismCount = 0;
        if (skillEvolution is not null)
        {
            var recidivism = await skillEvolution.GetRecidivismCandidatesAsync(ct);
            recidivismCount = recidivism.Count;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "metrics",
            pipeline_states = pipelineStates,
            total_candidates = candidates.Count,
            recidivism_count = recidivismCount,
            recent_candidates = candidates.Take(10).Select(c => new
            {
                id = c.Id,
                state = c.State.ToString(),
                source = c.Source,
                created_at = c.CreatedAt.ToString("o")
            })
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);

        _logger.LogDebug("get_metrics: {Count} candidates, {Recidivism} recidivism sent to {ChatId}",
            candidates.Count, recidivismCount, conn.ChatId);
    }

    private async Task HandleGetTelemetryAsync(WebSocketConnection conn, CancellationToken ct)
    {
        if (_services is null)
        {
            await SendErrorAsync(conn, "get_telemetry not available: services not injected", ct);
            return;
        }

        int tensionLevel = 0;
        int activeGoalsCount = 0;
        using (var scope = _services.CreateScope())
        {
            var goalStore = scope.ServiceProvider.GetRequiredService<GoalStore>();
            var goals = await goalStore.GetActiveGoalsAsync("maria", ct);
            activeGoalsCount = goals.Count;
            tensionLevel = Math.Min(100, activeGoalsCount * 20 + goals.Sum(g => g.Priority) * 5);
        }

        var skillRegistry = _services.GetService<SkillRegistry>();
        int skillCount = skillRegistry?.List().Count() ?? 0;
        bool hiveActive = skillCount > 0;

        int heat = 0;
        var pipelineTracker = _services.GetService<PipelineTracker>();
        if (pipelineTracker is not null)
        {
            var candidates = await pipelineTracker.GetCandidatesAsync(ct);
            int activeStates = candidates.Count(c =>
                c.State == CandidateState.PROPOSED || c.State == CandidateState.APPLIED);
            heat = Math.Min(100, activeStates * 10);
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "telemetry",
            system_heat = heat,
            tension_level = tensionLevel,
            hive_active = hiveActive,
            active_goals = activeGoalsCount,
            skill_count = skillCount,
            timestamp = DateTimeOffset.UtcNow.ToString("o")
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);
    }

    private async Task HandleGitStatusAsync(WebSocketConnection conn, CancellationToken ct)
    {
        var files = new List<object>();

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", "status --porcelain=v1 -b")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                await SendErrorAsync(conn, "git_status failed: could not start git process", ct);
                return;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith("## ")) continue;
                if (line.Length < 3) continue;
                var status = line.Substring(0, 2);
                var path = line.Substring(3).Trim();
                files.Add(new { path, status });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "git_status failed");
            await SendErrorAsync(conn, $"git_status failed: {ex.Message}", ct);
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            type = "git_status_response",
            files
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);
    }

    private async Task HandleContextUpdateAsync(WebSocketConnection conn, JsonElement root, CancellationToken ct)
    {
        var files = root.TryGetProperty("files", out var filesProp) && filesProp.ValueKind == JsonValueKind.Array
            ? filesProp.EnumerateArray()
                .Where(f => f.ValueKind == JsonValueKind.String)
                .Select(f => f.GetString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToList()
            : new List<string>();

        // Per-session context file persistence will be layered in a future change.
        // For now, ack so the client knows the server accepted the update.
        var payload = JsonSerializer.Serialize(new
        {
            type = "context_updated",
            ok = true,
            file_count = files.Count
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);

        _logger.LogDebug("context_update: {Count} files acked for {ChatId}",
            files.Count, conn.ChatId);
    }

    private async Task HandleStageFileAsync(WebSocketConnection conn, JsonElement root, CancellationToken ct)
    {
        if (!root.TryGetProperty("file", out var fileProp)
            || string.IsNullOrWhiteSpace(fileProp.GetString()))
        {
            await SendErrorAsync(conn, "Missing 'file' field in stage_file", ct);
            return;
        }

        var file = fileProp.GetString()!;
        var stage = root.TryGetProperty("stage", out var stageProp)
            && stageProp.ValueKind == JsonValueKind.True;

        try
        {
            var args = stage ? $"add \"{file}\"" : $"reset \"{file}\"";
            var psi = new System.Diagnostics.ProcessStartInfo("git", args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                await SendErrorAsync(conn, "stage_file failed: could not start git process", ct);
                return;
            }

            await process.WaitForExitAsync(ct);
            var ok = process.ExitCode == 0;

            var payload = JsonSerializer.Serialize(new
            {
                type = "stage_file_response",
                file,
                staged = stage,
                ok
            }, JsonOptions);

            await SendJsonAsync(conn, payload, ct);

            _logger.LogDebug("stage_file {Action} {File}: ok={Ok}", stage ? "add" : "reset", file, ok);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "stage_file failed for {File}", file);
            await SendErrorAsync(conn, $"stage_file failed: {ex.Message}", ct);
        }
    }

    private async Task SendErrorAsync(WebSocketConnection conn, string errorText, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type = "error",
            text = errorText
        }, JsonOptions);

        await SendJsonAsync(conn, payload, ct);
    }

    private static async Task SendJsonAsync(WebSocketConnection conn, string json, CancellationToken ct)
    {
        if (conn.WebSocket.State != WebSocketState.Open)
            return;

        var bytes = Encoding.UTF8.GetBytes(json);
        try
        {
            await conn.WebSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                ct);
        }
        catch (WebSocketException)
        {
            // Connection is dead; will be cleaned up by the receive loop
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    private async Task CloseConnectionAsync(
        string chatId, WebSocketConnection conn,
        WebSocketCloseStatus status, string reason, CancellationToken ct)
    {
        _connections.TryRemove(chatId, out _);

        if (conn.WebSocket.State == WebSocketState.Open ||
            conn.WebSocket.State == WebSocketState.CloseReceived)
        {
            try
            {
                await conn.WebSocket.CloseAsync(status, reason, ct);
            }
            catch (WebSocketException) { }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        }
    }

        public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
        if (_listener is not null)
        {
            try { _listener.Stop(); _listener.Close(); } catch { }
        }
    }

        /// <summary>
    /// Represents an active WebSocket connection with its assigned chat_id.
    /// </summary>
    private sealed record WebSocketConnection(
        string ChatId,
        WebSocket WebSocket,
        IPEndPoint? RemoteEndPoint);

    /// <summary>
    /// Minimal IServiceProvider fallback when none is injected (e.g. in unit tests).
    /// </summary>
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
