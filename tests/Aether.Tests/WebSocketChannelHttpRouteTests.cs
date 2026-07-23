using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using Aether.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aether.Tests;

/// <summary>
/// Tests that WebSocketChannel serves HTTP routes (/memory/maria/*) alongside
/// WebSocket upgrade (/ws) on a single HttpListener port.
/// Spec: SC-WS-HTTP-01..04.
/// </summary>
public sealed class WebSocketChannelHttpRouteTests
{
    [Fact]
    public async Task SC_WS_HTTP_01_HttpGetRecall_ReturnsJson()
    {
        var port = GetRandomAvailablePort();
        var channel = new WebSocketChannel(port, NullLogger<WebSocketChannel>.Instance);
        try
        {
            await channel.ConnectAsync(CancellationToken.None);
            using var client = new HttpClient();
            var res = await client.GetAsync($"http://localhost:{port}/memory/maria/recall?query=test");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
        }
        finally
        {
            await channel.DisconnectAsync(CancellationToken.None);
            channel.Dispose();
        }
    }

    [Fact]
    public async Task SC_WS_HTTP_02_WsUpgrade_StillWorks()
    {
        var port = GetRandomAvailablePort();
        var channel = new WebSocketChannel(port, NullLogger<WebSocketChannel>.Instance);
        try
        {
            await channel.ConnectAsync(CancellationToken.None);

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri($"ws://localhost:{port}/ws"), CancellationToken.None);
            Assert.Equal(WebSocketState.Open, ws.State);

            // Read welcome message (existing behavior)
            var buffer = new byte[4096];
            var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        }
        finally
        {
            await channel.DisconnectAsync(CancellationToken.None);
            channel.Dispose();
        }
    }

    [Fact]
    public async Task SC_WS_HTTP_03_MissingQuery_Returns400()
    {
        var port = GetRandomAvailablePort();
        var channel = new WebSocketChannel(port, NullLogger<WebSocketChannel>.Instance);
        try
        {
            await channel.ConnectAsync(CancellationToken.None);
            using var client = new HttpClient();
            var res = await client.GetAsync($"http://localhost:{port}/memory/maria/recall");
            Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        }
        finally
        {
            await channel.DisconnectAsync(CancellationToken.None);
            channel.Dispose();
        }
    }

    [Fact]
    public async Task SC_WS_HTTP_04_NoMatch_ReturnsEmptyNodes()
    {
        var port = GetRandomAvailablePort();
        var channel = new WebSocketChannel(port, NullLogger<WebSocketChannel>.Instance);
        try
        {
            await channel.ConnectAsync(CancellationToken.None);
            using var client = new HttpClient();
            var res = await client.GetAsync($"http://localhost:{port}/memory/maria/recall?query=nonexistentterm99999xyz");
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var body = await res.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(body);
            Assert.True(doc.RootElement.GetProperty("success").GetBoolean());
            Assert.Empty(doc.RootElement.GetProperty("nodes").EnumerateArray());
        }
        finally
        {
            await channel.DisconnectAsync(CancellationToken.None);
            channel.Dispose();
        }
    }

    private static int GetRandomAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
