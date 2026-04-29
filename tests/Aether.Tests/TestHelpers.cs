using Aether.Agents;
using System.Net;
using Aether.Providers;
using Aether.Agent;
using Aether.Memory;
using Aether.Sessions;
using Aether.SelfImprovement;

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

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var response = await CompleteAsync(request, ct);
        yield return response.Content;
    }

    public async IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var response = await CompleteAsync(request, ct);
        yield return new StreamEvent.TextToken(response.Content);
        yield return new StreamEvent.Response(response);
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

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var response = await CompleteAsync(request, ct);
        yield return response.Content;
    }

    public async IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var response = await CompleteAsync(request, ct);
        yield return new StreamEvent.TextToken(response.Content);
        yield return new StreamEvent.Response(response);
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

internal class FakeMemorySystem : IMemorySystem
{
    public bool ShouldThrowOnPromote { get; set; }
    public bool PromoteReturnsTrue { get; set; }
    public List<PromotionCandidate> PromotedCandidates { get; } = new();
    public Func<DateTime, CancellationToken, Task<IReadOnlyList<SessionSummary>>>? OnGetRecentSessions { get; set; }

    public virtual Task<string> LoadContextAsync(string groupFolder, CancellationToken ct = default) =>
        Task.FromResult("fake memory context");

    public void AddToContext(string content, float priority = 0.5f) { }
    public void CompactContext(int targetTokens) { }
    public IReadOnlyList<ContextEntry> GetContext() => Array.Empty<ContextEntry>();
    public virtual Task<string> CreateSessionAsync(string agentId, CancellationToken ct = default) => Task.FromResult("fake-session");
    public Task AppendMessageAsync(string sessionId, string role, string content, CancellationToken ct = default) => Task.CompletedTask;
    public Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 10, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());
    public Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<SessionSummary?>(null);
    public virtual Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(DateTime since, CancellationToken ct = default)
    {
        if (OnGetRecentSessions is not null)
            return OnGetRecentSessions(since, ct);
        return Task.FromResult<IReadOnlyList<SessionSummary>>(Array.Empty<SessionSummary>());
    }
    public Task<string> GetDurableMemoryAsync(CancellationToken ct = default) => Task.FromResult("");
    public virtual Task<bool> TryPromoteAsync(PromotionCandidate candidate, CancellationToken ct = default)
    {
        if (ShouldThrowOnPromote) throw new InvalidOperationException("Simulated promotion failure");
        PromotedCandidates.Add(candidate);
        return Task.FromResult(PromoteReturnsTrue);
    }
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

    public Task<IReadOnlyList<Session>> GetRecentSessionsAsync(int limit = 10, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<Session>>(Array.Empty<Session>());
    }
}

internal sealed class FakePipelineTracker : IPipelineTracker
{
    public List<PromotionCandidate> Tracked { get; } = new();
    public List<(PromotionCandidate, CandidateState)> Transitions { get; } = new();

    public CandidateState DefaultState { get; set; } = CandidateState.PROPOSED;
    public bool ThrowOnTrack { get; set; }
    public bool ThrowOnTransition { get; set; }

    public Task TrackAsync(PromotionCandidate candidate, CancellationToken ct = default)
    {
        if (ThrowOnTrack) throw new InvalidOperationException("Simulated track failure");
        Tracked.Add(candidate);
        return Task.CompletedTask;
    }

    public Task TransitionAsync(PromotionCandidate candidate, CandidateState newState, CancellationToken ct = default)
    {
        if (ThrowOnTransition) throw new InvalidOperationException("Simulated transition failure");
        Transitions.Add((candidate, newState));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TrackedCandidate>> GetCandidatesAsync(CancellationToken ct = default)
    {
        var result = Tracked.Select(c => new TrackedCandidate(
            Guid.NewGuid().ToString(), "hash", DefaultState, c.Source, c.Content,
            DateTime.UtcNow, DateTime.UtcNow)).ToList();
        return Task.FromResult<IReadOnlyList<TrackedCandidate>>(result);
    }

    public Task<IReadOnlyList<TrackedCandidate>> GetByStateAsync(CandidateState state, CancellationToken ct = default)
    {
        var result = Transitions
            .Where(t => t.Item2 == state)
            .Select(t => new TrackedCandidate(
                Guid.NewGuid().ToString(), "hash", state, t.Item1.Source, t.Item1.Content,
                DateTime.UtcNow, DateTime.UtcNow))
            .ToList();
        return Task.FromResult<IReadOnlyList<TrackedCandidate>>(result);
    }
}

internal sealed class FakeBenchmarkGate : IBenchmarkGate
{
    public bool Passes { get; set; } = true;
    public int CallCount { get; private set; }
    public bool ThrowOnRun { get; set; }

    public Task<BenchmarkResult> RunTestsAsync(CancellationToken ct = default)
    {
        CallCount++;
        if (ThrowOnRun) throw new InvalidOperationException("Simulated benchmark failure");
        return Task.FromResult(new BenchmarkResult(Passes, Passes ? 0 : 1, "", Passes ? "" : "test failure"));
    }
}

internal sealed class FakeSelfImprovementService : ISelfImprovementService
{
    public int CallCount { get; private set; }
    public bool ThrowOnRun { get; set; }

    public Task RunDailyReviewAsync(CancellationToken ct = default)
    {
        CallCount++;
        if (ThrowOnRun) throw new InvalidOperationException("Simulated review failure");
        return Task.CompletedTask;
    }
}

/// <summary>
/// A fake LLM provider that yields tokens one by one via streaming events.
/// Useful for testing that ProcessStreamingAsync yields individual tokens correctly.
/// Can also return tool calls via CompleteStreamingEventsAsync for tool-loop testing.
/// </summary>
internal sealed class FakeStreamingProvider : ILLMProvider
{
    private readonly string _response;
    private readonly IReadOnlyList<LlmToolCall>? _toolCalls;
    private readonly bool _throwToolError;

    public int CallCount { get; private set; }
    public LlmRequest? LastRequest { get; private set; }

    public string Name => "fake-streamer";
    public string Model => "fake-streamer-model";
    public bool SupportsStreaming => true;
    public bool SupportsTools => _toolCalls is not null;

    public FakeStreamingProvider(string response, IReadOnlyList<LlmToolCall>? toolCalls = null, bool throwToolError = false)
    {
        _response = response;
        _toolCalls = toolCalls;
        _throwToolError = throwToolError;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;
        if (_throwToolError) throw new InvalidOperationException("tool use not supported");
        return Task.FromResult(new LlmResponse(_response, _toolCalls));
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;
        if (_throwToolError) throw new InvalidOperationException("tool use not supported");

        foreach (var ch in _response)
        {
            yield return ch.ToString();
        }
    }

    public async IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(
        LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        CallCount++;
        LastRequest = request;
        if (_throwToolError) throw new InvalidOperationException("tool use not supported");

        foreach (var ch in _response)
        {
            yield return new StreamEvent.TextToken(ch.ToString());
        }

        yield return new StreamEvent.Response(new LlmResponse(_response, _toolCalls));
    }

    public Task<bool> HealthCheckAsync(CancellationToken ct) => Task.FromResult(true);
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

internal static class TestAgentProfile
{
    public static Agents.IAgentProfile NoOp(string name = "test-agent")
    {
        var dir = Path.Combine(Path.GetTempPath(), $"aether-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "SOUL.md"), "Aether");
        return new Agents.AgentProfile(name, dir, new Agents.AgentConfig { StartupFiles = new() { "SOUL.md" } });
    }
}
