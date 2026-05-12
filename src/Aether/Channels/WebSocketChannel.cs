using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aether.Ui;
using Microsoft.Extensions.Logging;

namespace Aether.Channels;

/// <summary>
/// WebSocket channel implementing IChannel for non-Telegram clients (web UI, CLI tools, etc).
/// Hosts a WebSocket server on a configurable port; each connection gets a unique chat_id.
///
/// Uses TcpListener + manual WebSocket upgrade handshake for cross-platform compatibility.
/// (HttpListener requires admin/ACL configuration on some platforms.)
/// </summary>
public sealed class WebSocketChannel : IChannel, IDisposable
{
    private readonly int _port;
    private readonly ILogger<WebSocketChannel> _logger;
    private TcpListener? _listener;
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
    public event Func<string, Ui.UiCallback, Task<Ui.UiDocument?>>? OnUiCallback;

    /// <summary>
    /// The actual port the listener is bound to. Useful when passing port 0 for tests.
    /// </summary>
    public int BoundPort { get; private set; }

    public WebSocketChannel(int port, ILogger<WebSocketChannel> logger)
    {
        _port = port;
        _logger = logger;
    }

    public Task ConnectAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        _listener = new TcpListener(IPAddress.Loopback, _port);
        _listener.Start();
        BoundPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

        _logger.LogInformation("WebSocket channel listening on ws://localhost:{Port}/ws", BoundPort);

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

    public async Task SendMessageAsync(string chatId, string text, CancellationToken ct)
    {
        if (!_connections.TryGetValue(chatId, out var conn))
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
        if (!_connections.TryGetValue(chatId, out var conn))
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
        if (!_connections.TryGetValue(chatId, out var conn))
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
        if (!_connections.TryGetValue(chatId, out var conn))
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
            && _connections.ContainsKey(chatId);
    }

    /// <summary>
    /// Accept loop: accepts TCP connections, performs WebSocket HTTP upgrade handshake,
    /// then spins off a handler for each connection.
    /// </summary>
    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await _listener!.AcceptTcpClientAsync(ct);

                _ = Task.Run(() => HandleConnectionAsync(tcpClient, ct), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket accept loop error");
            }
        }
    }

    /// <summary>
    /// Performs the WebSocket HTTP upgrade handshake, then enters the receive loop.
    /// </summary>
    private async Task HandleConnectionAsync(TcpClient tcpClient, CancellationToken ct)
    {
        var remoteEndPoint = tcpClient.Client.RemoteEndPoint as IPEndPoint;
        NetworkStream? stream = null;

        try
        {
            stream = tcpClient.GetStream();

            // Perform the HTTP WebSocket upgrade handshake
            var webSocket = await UpgradeToWebSocketAsync(stream, ct);
            if (webSocket is null)
            {
                // Not a WebSocket request -- send 426 and close
                await SendHttpResponseAsync(stream, 426, "Upgrade Required",
                    "This endpoint accepts WebSocket connections at ws:// url scheme.");
                return;
            }

            var connectionId = Interlocked.Increment(ref _nextConnectionId);
            var chatId = $"websocket:{connectionId}";

            var conn = new WebSocketConnection(chatId, webSocket, stream, tcpClient, remoteEndPoint);
            _connections[chatId] = conn;

            _logger.LogInformation(
                "WebSocket client connected: {RemoteEndPoint} -> {ChatId}",
                remoteEndPoint, chatId);

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
            _logger.LogWarning(ex, "WebSocket connection error from {RemoteEndPoint}", remoteEndPoint);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebSocket connection from {RemoteEndPoint}", remoteEndPoint);
        }
        finally
        {
            // Clean up the connection
            var entry = _connections.FirstOrDefault(kvp => kvp.Value.TcpClient == tcpClient);
            if (entry.Key is not null)
            {
                _connections.TryRemove(entry.Key, out _);
                _logger.LogInformation("WebSocket client disconnected: {ChatId}", entry.Key);
            }

            stream?.Dispose();
            tcpClient.Dispose();
        }
    }

    /// <summary>
    /// Perform the WebSocket HTTP upgrade handshake on the TCP stream.
    /// Returns a WebSocket instance on success, null if the request is not a WebSocket upgrade.
    /// Throws on protocol errors.
    /// </summary>
    private static async Task<WebSocket?> UpgradeToWebSocketAsync(NetworkStream stream, CancellationToken ct)
    {
        // Read the HTTP request headers
        var headerBuilder = new StringBuilder();
        var buffer = new byte[4096];
        var totalRead = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), ct);
            if (read == 0)
                return null; // Connection closed

            totalRead += read;
            var data = Encoding.ASCII.GetString(buffer, 0, totalRead);

            headerBuilder.Append(data);

            // Headers end with \r\n\r\n
            if (data.Contains("\r\n\r\n", StringComparison.Ordinal))
                break;

            if (totalRead >= buffer.Length)
                return null; // Headers too large
        }

        var headerText = headerBuilder.ToString();
        var headers = headerText.Split("\r\n");

        // First line must be "GET /ws HTTP/1.1" (or similar)
        var requestLine = headers[0];
        if (!requestLine.StartsWith("GET ", StringComparison.Ordinal))
            return null;

        // Parse headers into dictionary
        var headerDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < headers.Length; i++)
        {
            var line = headers[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var colonPos = line.IndexOf(':');
            if (colonPos > 0)
            {
                var headerName = line[..colonPos].Trim();
                var headerValue = line[(colonPos + 1)..].Trim();
                headerDict[headerName] = headerValue;
            }
        }

        // Verify this is a WebSocket upgrade request
        if (!string.Equals(headerDict.GetValueOrDefault("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!string.Equals(headerDict.GetValueOrDefault("Connection"), "Upgrade", StringComparison.OrdinalIgnoreCase)
            && !(headerDict.GetValueOrDefault("Connection")?.Contains("Upgrade", StringComparison.OrdinalIgnoreCase) ?? false))
            return null;

        var key = headerDict.GetValueOrDefault("Sec-WebSocket-Key");
        if (string.IsNullOrEmpty(key))
            return null;

        // Compute the accept key (WebSocket protocol magic)
        var acceptKey = ComputeAcceptKey(key);

        // Send 101 Switching Protocols response
        var response = new StringBuilder();
        response.Append("HTTP/1.1 101 Switching Protocols\r\n");
        response.Append("Upgrade: websocket\r\n");
        response.Append("Connection: Upgrade\r\n");
        response.Append($"Sec-WebSocket-Accept: {acceptKey}\r\n");
        response.Append("\r\n");

        var responseBytes = Encoding.ASCII.GetBytes(response.ToString());
        await stream.WriteAsync(responseBytes, ct);

        // Create the server-side WebSocket from the stream
        return System.Net.WebSockets.WebSocket.CreateFromStream(stream, isServer: true, subProtocol: null, keepAliveInterval: TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Compute the Sec-WebSocket-Accept value per RFC 6455.
    /// </summary>
    private static string ComputeAcceptKey(string key)
    {
        const string MagicGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";
        var combined = key + MagicGuid;
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(combined));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Send a plain HTTP response (for non-WebSocket requests).
    /// </summary>
    private static async Task SendHttpResponseAsync(NetworkStream stream, int statusCode, string statusText, string body)
    {
        var response = new StringBuilder();
        response.Append($"HTTP/1.1 {statusCode} {statusText}\r\n");
        response.Append("Content-Type: text/plain\r\n");
        response.Append($"Content-Length: {body.Length}\r\n");
        response.Append("Connection: close\r\n");
        response.Append("\r\n");
        response.Append(body);

        var bytes = Encoding.ASCII.GetBytes(response.ToString());
        await stream.WriteAsync(bytes);
        await stream.FlushAsync();
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
            ChatId: conn.ChatId,
            SenderId: $"ws:{conn.RemoteEndPoint}",
            Text: text,
            Timestamp: DateTimeOffset.UtcNow);

        _logger.LogDebug(
            "WebSocket message from {ChatId}: {Text} (group={Group})",
            conn.ChatId, text, group);

        OnMessage?.Invoke(this, inbound);
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
        _listener?.Stop();
    }

    /// <summary>
    /// Represents an active WebSocket connection with its assigned chat_id.
    /// </summary>
    private sealed record WebSocketConnection(
        string ChatId,
        WebSocket WebSocket,
        NetworkStream Stream,
        TcpClient TcpClient,
        IPEndPoint? RemoteEndPoint);
}
