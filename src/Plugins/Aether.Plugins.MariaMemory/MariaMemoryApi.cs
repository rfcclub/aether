using System.Net;
using System.Text;
using System.Text.Json;
using Aether.Plugins.MariaMemory.Models;
using Microsoft.Extensions.Logging;

namespace Aether.Plugins.MariaMemory;

public sealed class MariaMemoryApi
{
    private readonly HttpListener _listener;
    private readonly MariaMemoryStore _store;
    private readonly ContextAssemblyEngine _contextEngine;
    private readonly DreamingService _dreamer;
    private readonly ILogger _logger;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public MariaMemoryApi(
        MariaMemoryStore store, 
        ContextAssemblyEngine contextEngine, 
        DreamingService dreamer, 
        ILogger logger, 
        int port = 5077)
    {
        _store = store;
        _contextEngine = contextEngine;
        _dreamer = dreamer;
        _logger = logger;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token));
        _logger.LogInformation("MariaMemory API listening on http://localhost:5077/");
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener.Stop();
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context, ct), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MariaMemory API listen loop");
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken ct)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            _logger.LogDebug("API Request: {Method} {Url}", request.HttpMethod, request.Url);

            if (request.Url?.AbsolutePath == "/memory/context" && request.HttpMethod == "GET")
            {
                var topic = request.QueryString["topic"] ?? "";
                var limitStr = request.QueryString["limit"] ?? "7000";
                int.TryParse(limitStr, out var limit);
                
                var result = await _contextEngine.AssembleContextAsync(topic, limit, ct);
                await WriteResponseAsync(response, new { success = true, context = result });
            }
            else if (request.Url?.AbsolutePath == "/memory/nodes" && request.HttpMethod == "GET")
            {
                var limitStr = request.QueryString["limit"] ?? "100";
                int.TryParse(limitStr, out var limit);
                var nodes = await _store.GetAllNodesAsync(limit, ct);
                await WriteResponseAsync(response, new { success = true, nodes });
            }
            else if (request.Url?.AbsolutePath == "/memory/nodes" && request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(request.InputStream);
                var json = await reader.ReadToEndAsync(ct);
                var node = JsonSerializer.Deserialize<MemoryNode>(json);
                if (node != null)
                {
                    await _store.AppendAsync(node, ct);
                    await WriteResponseAsync(response, new { success = true, id = node.Id });
                }
                else
                {
                    await WriteErrorAsync(response, 400, "Invalid MemoryNode JSON");
                }
            }
            else if (request.Url?.AbsolutePath == "/memory/dream" && request.HttpMethod == "POST")
            {
                _ = Task.Run(() => _dreamer.PerformDreamCycleAsync(CancellationToken.None));
                await WriteResponseAsync(response, new { success = true, message = "Dreaming initiated" });
            }
            else
            {
                await WriteErrorAsync(response, 404, "Not Found");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling API request");
            await WriteErrorAsync(response, 500, ex.Message);
        }
        finally
        {
            response.Close();
        }
    }

    private static async Task WriteResponseAsync(HttpListenerResponse response, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var buffer = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
    }

    private static async Task WriteErrorAsync(HttpListenerResponse response, int statusCode, string message)
    {
        response.StatusCode = statusCode;
        await WriteResponseAsync(response, new { success = false, error = message });
    }
}
