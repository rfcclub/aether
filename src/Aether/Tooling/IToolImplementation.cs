using System.Text.Json;

namespace Aether.Tooling;

/// <summary>
/// A code-registered tool implementation that provides real execution logic,
/// replacing the passive stub behavior of hot-reloaded tools.
/// </summary>
public interface IToolImplementation
{
    string Name { get; }
    string Description { get; }
    JsonElement ParametersSchema { get; }
    Task<object> ExecuteAsync(JsonElement args, SandboxContext sandbox, CancellationToken ct);
}
