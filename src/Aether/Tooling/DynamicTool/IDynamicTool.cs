namespace Aether.Tooling.DynamicTool;

/// <summary>
/// Interface for dynamically compiled tools loaded from .cs files at runtime.
/// Tools are compiled via Roslyn, loaded into a collectible AssemblyLoadContext,
/// and registered in the ToolRegistry for execution by the LLM.
/// </summary>
public interface IDynamicTool
{
    /// <summary>
    /// The unique name of the tool (e.g., "hello_world").
    /// Used as the tool name in LLM tool calls.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// A description of what the tool does, used by the LLM to decide when to call it.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON Schema for the tool's parameters (e.g., NJsonSchema format).
    /// Defines what arguments the tool accepts.
    /// </summary>
    string ParameterSchemaJson { get; }

    /// <summary>
    /// Execute the tool with the given arguments and return the result as a string.
    /// </summary>
    Task<string> ExecuteAsync(Dictionary<string, object> args);
}
