using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Aether.Channels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aether.Tests;

/// <summary>
/// Tests for WebSocketChannel. Simple interface-contract tests use a dummy port (1)
/// since the channel validates port >= 1. Integration tests that actually connect
/// use GetRandomAvailablePort() to find a free port.
/// </summary>
public sealed class WebSocketChannelTests
{
    // Simple interface contract tests (no real WebSocket server needed)

    [Fact]
    public void Name_is_websocket()
    {
        using var channel = new WebSocketChannel(port: 1, new NullLogger<WebSocketChannel>());
        Assert.Equal("websocket", channel.Name);
    }

    [Fact]
    public void IsConnected_is_false_by_default()
    {
        using var channel = new WebSocketChannel(port: 1, new NullLogger<WebSocketChannel>());
        Assert.False(channel.IsConnected);
    }

    [Fact]
    public void OwnsChatId_returns_false_for_unconnected()
    {
        using var channel = new WebSocketChannel(port: 1, new NullLogger<WebSocketChannel>());
        Assert.False(channel.OwnsChatId("websocket:42"));
        Assert.False(channel.OwnsChatId("telegram:42"));
        Assert.False(channel.OwnsChatId("unknown:42"));
    }

    [Fact]
    public async Task SendMessageAsync_is_safe_with_unknown_chat_id()
    {
        using var channel = new WebSocketChannel(port: 1, new NullLogger<WebSocketChannel>());
        await channel.SendMessageAsync("websocket:999", "hello", CancellationToken.None);
    }

    [Fact]
    public async Task SetTypingAsync_is_safe_with_unknown_chat_id()
    {
        using var channel = new WebSocketChannel(port: 1, new NullLogger<WebSocketChannel>());
        await channel.SetTypingAsync("websocket:999", true, CancellationToken.None);
    }

    [Fact]
    public async Task SendStreamingChunkAsync_is_safe_with_unknown_chat_id()
    {
        using var channel = new WebSocketChannel(port: 1, new NullLogger<WebSocketChannel>());
        await channel.SendStreamingChunkAsync("websocket:999", "chunk", 0, CancellationToken.None);
    }

    [Fact]
    public async Task SendStreamingCompleteAsync_is_safe_with_unknown_chat_id()
    {
        using var channel = new WebSocketChannel(port: 1, new NullLogger<WebSocketChannel>());
        await channel.SendStreamingCompleteAsync("websocket:999", "full text", CancellationToken.None);
    }

    // Integration tests (require a real WebSocket server)

    [Fact]
    public async Task Connect_and_accepts_WebSocket_connection()
    {
        var port = GetRandomAvailablePort();
        var channel = new WebSocketChannel(port, new NullLogger<WebSocketChannel>());
        try
        {
            await channel.ConnectAsync(CancellationToken.None);
            Assert.True(channel.IsConnected);

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);

            Assert.Equal(WebSocketState.Open, ws.State);

            // Should receive a welcome/connected message
            var buffer = new byte[4096];
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);

            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal("connected", doc.RootElement.GetProperty("type").GetString());
            Assert.NotNull(doc.RootElement.GetProperty("chat_id").GetString());
        }
        finally
        {
            await channel.DisconnectAsync(CancellationToken.None);
            channel.Dispose();
        }
    }

    [Fact]
    public async Task Send_message_and_receive_response()
    {
        var port = GetRandomAvailablePort();
        var channel = new WebSocketChannel(port, new NullLogger<WebSocketChannel>());
        try
        {
            await channel.ConnectAsync(CancellationToken.None);

            InboundMessage? captured = null;
            channel.OnMessage += (_, msg) => captured = msg;

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);

            // Read welcome
            var buffer = new byte[4096];
            await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            // Send a JSON message
            var sendJson = """{"type":"message","text":"hello aether","group":"main"}""";
            var sendBytes = Encoding.UTF8.GetBytes(sendJson);
            await ws.SendAsync(new ArraySegment<byte>(sendBytes), WebSocketMessageType.Text, true, CancellationToken.None);

            // Give the server time to process
            await Task.Delay(500);

            Assert.NotNull(captured);
            Assert.Equal("hello aether", captured!.Value.Text);
            Assert.StartsWith("websocket:", captured.Value.ChatId);
            Assert.Equal("websocket", captured.Value.ChannelName);
        }
        finally
        {
            await channel.DisconnectAsync(CancellationToken.None);
            channel.Dispose();
        }
    }

    [Fact]
    public async Task Non_WebSocket_request_gets_426()
    {
        var port = GetRandomAvailablePort();
        var channel = new WebSocketChannel(port, new NullLogger<WebSocketChannel>());
        try
        {
            await channel.ConnectAsync(CancellationToken.None);

            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var response = await httpClient.GetAsync($"http://localhost:{port}/ws");
            Assert.Equal(426, (int)response.StatusCode);
        }
        finally
        {
            await channel.DisconnectAsync(CancellationToken.None);
            channel.Dispose();
        }
    }

    [Fact]
    public async Task Send_to_known_chat_id_delivers_JSON()
    {
        var port = GetRandomAvailablePort();
        var channel = new WebSocketChannel(port, new NullLogger<WebSocketChannel>());
        try
        {
            await channel.ConnectAsync(CancellationToken.None);

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);

            // Read welcome to get chat_id
            var buffer = new byte[8192];
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var welcomeJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var welcomeDoc = JsonDocument.Parse(welcomeJson);
            var chatId = welcomeDoc.RootElement.GetProperty("chat_id").GetString()!;
            Assert.StartsWith("websocket:", chatId);

            // Use the channel to send a message back to this client
            await channel.SendMessageAsync(chatId, "hello from server", CancellationToken.None);

            // Client should receive the message
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var responseJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var responseDoc = JsonDocument.Parse(responseJson);
            Assert.Equal("message", responseDoc.RootElement.GetProperty("type").GetString());
            Assert.Equal("hello from server", responseDoc.RootElement.GetProperty("text").GetString());
            Assert.NotNull(responseDoc.RootElement.GetProperty("message_id").GetString());
        }
        finally
        {
            await channel.DisconnectAsync(CancellationToken.None);
            channel.Dispose();
        }
    }

    [Fact]
    public async Task Streaming_chunks_delivered_as_separate_messages()
    {
        var port = GetRandomAvailablePort();
        var channel = new WebSocketChannel(port, new NullLogger<WebSocketChannel>());
        try
        {
            await channel.ConnectAsync(CancellationToken.None);

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);

            // Read welcome
            var buffer = new byte[8192];
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var welcomeJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var welcomeDoc = JsonDocument.Parse(welcomeJson);
            var chatId = welcomeDoc.RootElement.GetProperty("chat_id").GetString()!;

            // Send streaming chunks
            await channel.SendStreamingChunkAsync(chatId, "Hello", 0, CancellationToken.None);
            await channel.SendStreamingChunkAsync(chatId, " World", 1, CancellationToken.None);
            await channel.SendStreamingCompleteAsync(chatId, "Hello World", CancellationToken.None);

            // Read chunk 0
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var chunk0 = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var doc0 = JsonDocument.Parse(chunk0);
            Assert.Equal("chunk", doc0.RootElement.GetProperty("type").GetString());
            Assert.Equal("Hello", doc0.RootElement.GetProperty("text").GetString());
            Assert.Equal(0, doc0.RootElement.GetProperty("index").GetInt32());

            // Read chunk 1
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var chunk1 = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var doc1 = JsonDocument.Parse(chunk1);
            Assert.Equal("chunk", doc1.RootElement.GetProperty("type").GetString());
            Assert.Equal(" World", doc1.RootElement.GetProperty("text").GetString());
            Assert.Equal(1, doc1.RootElement.GetProperty("index").GetInt32());

            // Read complete
            result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var complete = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var doc2 = JsonDocument.Parse(complete);
            Assert.Equal("complete", doc2.RootElement.GetProperty("type").GetString());
            Assert.Equal("Hello World", doc2.RootElement.GetProperty("text").GetString());
        }
        finally
        {
            await channel.DisconnectAsync(CancellationToken.None);
            channel.Dispose();
        }
    }

    [Fact]
    public async Task Ping_gets_pong()
    {
        var port = GetRandomAvailablePort();
        var channel = new WebSocketChannel(port, new NullLogger<WebSocketChannel>());
        try
        {
            await channel.ConnectAsync(CancellationToken.None);

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);

            // Read welcome
            var buffer = new byte[4096];
            await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            // Send ping
            var pingJson = """{"type":"ping"}""";
            var pingBytes = Encoding.UTF8.GetBytes(pingJson);
            await ws.SendAsync(new ArraySegment<byte>(pingBytes), WebSocketMessageType.Text, true, CancellationToken.None);

            // Read pong
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var responseJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var doc = JsonDocument.Parse(responseJson);
            Assert.Equal("pong", doc.RootElement.GetProperty("type").GetString());
        }
        finally
        {
            await channel.DisconnectAsync(CancellationToken.None);
            channel.Dispose();
        }
    }

    [Fact]
    public async Task Send_without_type_gets_error()
    {
        var port = GetRandomAvailablePort();
        var channel = new WebSocketChannel(port, new NullLogger<WebSocketChannel>());
        try
        {
            await channel.ConnectAsync(CancellationToken.None);

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);

            // Read welcome
            var buffer = new byte[4096];
            await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            // Send JSON without type field
            var badJson = """{"text":"hello"}""";
            var badBytes = Encoding.UTF8.GetBytes(badJson);
            await ws.SendAsync(new ArraySegment<byte>(badBytes), WebSocketMessageType.Text, true, CancellationToken.None);

            // Read error
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            var responseJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
            using var doc = JsonDocument.Parse(responseJson);
            Assert.Equal("error", doc.RootElement.GetProperty("type").GetString());
            Assert.Contains("type", doc.RootElement.GetProperty("text").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await channel.DisconnectAsync(CancellationToken.None);
            channel.Dispose();
        }
    }

    /// <summary>
    /// Get a random available port by binding to port 0 and reading the assigned port.
    /// </summary>
    private static int GetRandomAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
