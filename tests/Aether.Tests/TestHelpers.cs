using System.Net;
using Aether.Providers;
using Aether.Agent;
using Aether.Memory;
using Aether.Sessions;

namespace Aether.Tests;

// Shared test doubles for xUnit tests

internal sealed class FakeLlmProvider : ILLMProvider
{
    private readonly LlmResponse? _response;
    private readonly bool _throwOnCall;
    private readonly bool _throwOnHealthCheck;
    public int CallCount { get; private set; }
    public LlmRequest? LastRequest { get; private set; }

    public string Name { get; }
    public string Model { get; }
    public bool SupportsStreaming => false;
    public bool SupportsTools => true;

    public FakeLlmProvider(string name, string model, LlmResponse? response = null, bool throwOnCall = false, bool throwOnHealthCheck = false)
    {
        Name = name;
        Model = model;
        _response = response;
        _throwOnCall = throwOnCall;
        _throwOnHealthCheck = throwOnHealthCheck;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        LastRequest = request;
        CallCount++;
        if (_throwOnCall) throw new InvalidOperationException("Simulated failure");
        return Task.FromResult(_response ?? new LlmResponse(""));
    }

    public Task<bool> HealthCheckAsync(CancellationToken ct)
    {
        if (_throwOnHealthCheck) throw new InvalidOperationException("Simulated health check failure");
        return Task.FromResult(true);
    }
}

internal sealed class MultiResponseProvider : ILLMProvider
{
    private readonly Queue<LlmResponse> _responses;
    public List<LlmRequest> Requests { get; } = new();

    public string Name => "multi";
    public string Model => "multi-model";
    public bool SupportsStreaming => false;
    public bool SupportsTools => true;

    public MultiResponseProvider(params LlmResponse[] responses)
    {
        _responses = new Queue<LlmResponse>(responses);
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (_responses.Count == 0) throw new InvalidOperationException("No more responses");
        return Task.FromResult(_responses.Dequeue());
    }

    public Task<bool> HealthCheckAsync(CancellationToken ct) => Task.FromResult(true);
}

internal sealed class FakeToolExecutor : IToolExecutor
{
    private readonly ToolResult _defaultResult;
    public List<ToolCall> Calls { get; } = new();

    public FakeToolExecutor(ToolResult? defaultResult = null)
    {
        _defaultResult = defaultResult ?? new ToolResult(true, "ok");
    }

    public Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        Calls.Add(call);
        return Task.FromResult(_defaultResult);
    }
}

internal sealed class FakeMemorySystem : IMemorySystem
{
    public Task<string> LoadContextAsync(string groupFolder, CancellationToken ct = default) =>
        Task.FromResult("fake memory context");

    public void AddToContext(string content, float priority = 0.5f) { }
    public void CompactContext(int targetTokens) { }
    public IReadOnlyList<ContextEntry> GetContext() => Array.Empty<ContextEntry>();
    public Task<string> CreateSessionAsync(string agentId, CancellationToken ct = default) => Task.FromResult("fake-session");
    public Task AppendMessageAsync(string sessionId, string role, string content, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 10, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());
    public Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<SessionSummary?>(null);
    public Task<string> GetDurableMemoryAsync(CancellationToken ct = default) => Task.FromResult("");
    public Task<bool> TryPromoteAsync(PromotionCandidate candidate, CancellationToken ct = default) => Task.FromResult(false);
    public Task ForceConsolidationAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeSessionManager : ISessionManager
{
    public List<SessionMessage> SavedMessages { get; } = new();

    public Task<Session> GetOrCreateSessionAsync(string groupFolder, CancellationToken ct)
    {
        return Task.FromResult(new Session("session-1", groupFolder, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    public Task AppendMessageAsync(string sessionId, SessionMessage message, CancellationToken ct)
    {
        SavedMessages.Add(message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionMessage>> GetHistoryAsync(string sessionId, int maxMessages, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<SessionMessage>>(Array.Empty<SessionMessage>());
    }
}

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly string _responseJson;
    private readonly HttpStatusCode _statusCode;

    public HttpRequestMessage? LastRequest { get; private set; }
    public string LastBody { get; private set; } = "";

    public FakeHttpHandler(string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseJson = responseJson;
        _statusCode = statusCode;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(_statusCode) { Content = new StringContent(_responseJson) };
    }
}
