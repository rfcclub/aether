# aether-soul-tool-loop Specification

## Purpose
TBD - created by archiving change llm-tool-call-loop. Update Purpose after archive.
## Requirements
### Requirement: Tool Loop Execution

`AetherSoul.ProcessAsync` MUST execute all tool calls returned by the LLM and feed results back into the next LLM call, repeating until no tool calls are returned.

#### Scenario: LLM returns tool call, tool executes, loop continues
- **WHEN** the LLM response contains one or more tool calls
- **THEN** each tool is executed via `IToolExecutor`, a `tool_result` message is appended to the message list, and the LLM is called again with the updated history

#### Scenario: Loop terminates when no tool calls returned
- **WHEN** an LLM response contains no tool calls and has text content
- **THEN** the loop exits and the text content is returned as the final `AgentResponse`

### Requirement: Iteration Guard

The tool loop MUST stop after `max_tool_iterations` iterations (default 10) to prevent runaway loops.

#### Scenario: Max iterations exceeded
- **WHEN** the tool loop has executed `max_tool_iterations` rounds without the LLM producing a plain-text response
- **THEN** the loop exits, a warning is noted, and the last available text response (or a synthetic `"[Max tool iterations reached]"` message) is returned

### Requirement: Tool Definitions Provided to LLM

`AetherSoul` MUST include the definitions of all 6 built-in tools in every `LlmRequest`.

#### Scenario: Tools included in request
- **WHEN** `ProcessAsync` builds an `LlmRequest`
- **THEN** `request.Tools` contains definitions for `bash`, `read`, `glob`, `grep`, `write`, and `edit`

### Requirement: Intermediate Messages Persisted

All intermediate `assistant` (tool_use) and `user` (tool_result) messages produced during the loop MUST be appended to session history.

#### Scenario: Tool turn messages saved
- **WHEN** the loop processes a tool call
- **THEN** the tool_use message and the corresponding tool_result message are both appended to the session via `ISessionManager.AppendMessageAsync`

