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

internal sealed class FakeToolExecutor : ToolExecutor
{
    private readonly ToolResult _defaultResult;
    public List<ToolCall> Calls { get; } = new();

    public FakeToolExecutor(ToolResult? defaultResult = null) : base()
    {
        _defaultResult = defaultResult ?? new ToolResult(true, "ok");
    }

    public override Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        Calls.Add(call);
        return Task.FromResult(_defaultResult);
    }
}

internal class FakeMemorySystem : FileMemory
{
    public bool ShouldThrowOnPromote { get; set; }
    public bool PromoteReturnsTrue { get; set; }
    public List<PromotionCandidate> PromotedCandidates { get; } = new();
    public Func<DateTime, CancellationToken, Task<IReadOnlyList<SessionSummary>>>? OnGetRecentSessions { get; set; }

    public FakeMemorySystem() : base(Path.Combine(Path.GetTempPath(), "aether-test-memory"))
    {
    }

    public override Task<string> LoadContextAsync(string groupFolder, CancellationToken ct = default) =>
        Task.FromResult("fake memory context");

    public override void AddToContext(string content, float priority = 0.5f) { }
    public override void CompactContext(int targetTokens) { }
    public override IReadOnlyList<ContextEntry> GetContext() => Array.Empty<ContextEntry>();
    public override Task<string> CreateSessionAsync(string agentId, CancellationToken ct = default) => Task.FromResult("fake-session");
    public override Task AppendMessageAsync(string sessionId, string role, string content, CancellationToken ct = default) => Task.CompletedTask;
    public override Task<IReadOnlyList<SearchResult>> SearchAsync(string query, int limit = 10, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SearchResult>>(Array.Empty<SearchResult>());
    public override Task<SessionSummary?> GetSessionAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<SessionSummary?>(null);
    public override Task<IReadOnlyList<SessionSummary>> GetRecentSessionsAsync(DateTime since, CancellationToken ct = default)
    {
        if (OnGetRecentSessions is not null)
            return OnGetRecentSessions(since, ct);
        return Task.FromResult<IReadOnlyList<SessionSummary>>(Array.Empty<SessionSummary>());
    }
    public override Task<string> GetDurableMemoryAsync(CancellationToken ct = default) => Task.FromResult("");
    public override Task<bool> TryPromoteAsync(PromotionCandidate candidate, CancellationToken ct = default)
    {
        if (ShouldThrowOnPromote) throw new InvalidOperationException("Simulated promotion failure");
        PromotedCandidates.Add(candidate);
        return Task.FromResult(PromoteReturnsTrue);
    }
    public override Task ForceConsolidationAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeSessionManager : SessionManager
{
    public FakeSessionManager() : base() { }
    public List<SessionMessage> SavedMessages { get; } = new();

    public override Task<Session> GetOrCreateSessionAsync(string groupFolder, CancellationToken ct)
    {
        return Task.FromResult(new Session("session-1", groupFolder, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    public override Task AppendMessageAsync(string sessionId, SessionMessage message, CancellationToken ct)
    {
        SavedMessages.Add(message);
        return Task.CompletedTask;
    }

    public override Task<IReadOnlyList<SessionMessage>> GetHistoryAsync(string sessionId, int maxMessages, CancellationToken ct)
    {
        return Task.FromResult<IReadOnlyList<SessionMessage>>(Array.Empty<SessionMessage>());
    }

    public override Task<IReadOnlyList<Session>> GetRecentSessionsAsync(int limit = 10, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<Session>>(Array.Empty<Session>());
    }
}

internal sealed class FakePipelineTracker : PipelineTracker
{
    public FakePipelineTracker() : base(new Aether.Data.AetherDb(":memory:", Path.GetTempFileName()), null!) { }
    public List<PromotionCandidate> Tracked { get; } = new();
    public List<(PromotionCandidate, CandidateState)> Transitions { get; } = new();

    public CandidateState DefaultState { get; set; } = CandidateState.PROPOSED;
    public bool ThrowOnTrack { get; set; }
    public bool ThrowOnTransition { get; set; }

    public new Task TrackAsync(PromotionCandidate candidate, CancellationToken ct = default)
    {
        if (ThrowOnTrack) throw new InvalidOperationException("Simulated track failure");
        Tracked.Add(candidate);
        return Task.CompletedTask;
    }

    public new Task TransitionAsync(PromotionCandidate candidate, CandidateState newState, CancellationToken ct = default)
    {
        if (ThrowOnTransition) throw new InvalidOperationException("Simulated transition failure");
        Transitions.Add((candidate, newState));
        return Task.CompletedTask;
    }

    public new Task<IReadOnlyList<TrackedCandidate>> GetCandidatesAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TrackedCandidate>>(Array.Empty<TrackedCandidate>());
    }

    public new Task<IReadOnlyList<TrackedCandidate>> GetByStateAsync(CandidateState state, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<TrackedCandidate>>(Array.Empty<TrackedCandidate>());
    }
}

internal sealed class FakeBenchmarkGate : BenchmarkGate
{
    public FakeBenchmarkGate() : base(".", 60, null!) { }
    public BenchmarkResult? FixedResult { get; set; }
    public bool ShouldThrow { get; set; }
    public bool Passes { get; set; } = true;
    public bool ThrowOnRun { get; set; }

    public new Task<BenchmarkResult> RunTestsAsync(CancellationToken ct = default)
    {
        if (ThrowOnRun) throw new InvalidOperationException("Simulated benchmark failure");
        return Task.FromResult(FixedResult ?? new BenchmarkResult(Passes, 0, "", ""));
    }
}

internal sealed class FakeSelfImprovementService : SelfImprovementService
{
    public FakeSelfImprovementService() : base(null!, null!, null!, null!, ".", null!) { }
    public bool RunDailyReviewCalled { get; set; }
    public bool ShouldThrow { get; set; }
    public bool ThrowOnRun { get; set; }

    public new Task RunDailyReviewAsync(CancellationToken ct = default)
    {
        if (ThrowOnRun || ShouldThrow) throw new InvalidOperationException("Simulated review failure");
        RunDailyReviewCalled = true;
        return Task.CompletedTask;
    }
}

internal sealed class FakeStreamingProvider : ILLMProvider
{
    private readonly string _fullText;
    private readonly int _chunks;
    public string Name => "fake-streaming";
    public string Model => "stream-model";
    public bool SupportsStreaming => true;
    public bool SupportsTools => false;
    public int CallCount { get; private set; }
    public LlmRequest? LastRequest { get; private set; }

    public FakeStreamingProvider(string fullText, int chunks = 5)
    {
        _fullText = fullText;
        _chunks = chunks;
    }

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
    {
        LastRequest = request;
        CallCount++;
        return Task.FromResult(new LlmResponse(_fullText));
    }

    public async IAsyncEnumerable<string> CompleteStreamingAsync(LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        LastRequest = request;
        CallCount++;
        var words = _fullText.Split(' ');
        var perChunk = Math.Max(1, words.Length / _chunks);
        for (var i = 0; i < words.Length; i += perChunk)
        {
            ct.ThrowIfCancellationRequested();
            yield return string.Join(' ', words.Skip(i).Take(perChunk)) + " ";
        }
    }

    public async IAsyncEnumerable<StreamEvent> CompleteStreamingEventsAsync(LlmRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var token in CompleteStreamingAsync(request, ct))
            yield return new StreamEvent.TextToken(token);
        yield return new StreamEvent.Response(new LlmResponse(_fullText));
    }

    public Task<bool> HealthCheckAsync(CancellationToken ct) => Task.FromResult(true);
}

internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly string? _responseContent;
    private readonly HttpStatusCode _statusCode;
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastBody { get; private set; }

    public FakeHttpHandler() { }

    public FakeHttpHandler(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        _responseContent = responseContent;
        _statusCode = statusCode;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        LastBody = request.Content is not null ? await request.Content.ReadAsStringAsync(ct) : null;
        return new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_responseContent ?? "")
        };
    }
}

internal static class TestAgentProfile
{
    public static Aether.Agents.AgentProfile NoOp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "aether-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "AGENTS.md"), "You are Aether. Be helpful.");
        return new Aether.Agents.AgentProfile("aether", dir, new AgentConfig());
    }
}
