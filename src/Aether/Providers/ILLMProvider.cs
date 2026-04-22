namespace Aether.Providers;

public interface ILLMProvider
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}

public sealed record LlmRequest(IReadOnlyList<LlmMessage> Messages);

public sealed record LlmMessage(string Role, string Content);

public sealed record LlmResponse(string Content);
