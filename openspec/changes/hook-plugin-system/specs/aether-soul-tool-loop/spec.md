## ADDED Requirements

### Requirement: PreLlmCall Hook Fired Before LLM Request

`AetherSoul.ProcessStreamingAsync` SHALL fire `HookPoint.PreLlmCall` before each LLM API call, passing a `PreLlmCallContext` with the current system prompt, messages list, model name, provider name, and estimated token count. The hook SHALL be able to modify `SystemPrompt` and set `ShouldEscalate`.

#### Scenario: Hook modifies system prompt before LLM call
- **WHEN** a PreLlmCall hook sets `PreLlmCallContext.SystemPrompt = "Modified prompt"`
- **THEN** the LLM SHALL receive the modified system prompt in its request

#### Scenario: Hook escalates provider
- **WHEN** a PreLlmCall hook sets `PreLlmCallContext.ShouldEscalate = true`
- **THEN** `AetherSoul` SHALL re-resolve the provider to the next tier before calling the LLM

#### Scenario: Hook blocks LLM call
- **WHEN** a PreLlmCall hook returns `HookResult.Stop("reason")`
- **THEN** the LLM call SHALL NOT proceed
- **AND** the stop reason SHALL be yielded as the streaming response content

### Requirement: PreToolUse Hook Fired Before Tool Execution

`AetherSoul` SHALL fire `HookPoint.PreToolUse` before executing each tool call, passing a `PreToolUseContext` with tool name, arguments, and risk level. The hook SHALL be able to deny execution (`Denied = true`) or transform arguments (`OverrideArguments`).

#### Scenario: Hook denies tool execution
- **WHEN** a PreToolUse hook sets `PreToolUseContext.Denied = true` and `DenyReason = "dangerous command"`
- **THEN** the tool SHALL NOT execute
- **AND** the tool result message SHALL be `"Tool 'bash' blocked: dangerous command"`

#### Scenario: Hook transforms tool arguments
- **WHEN** a PreToolUse hook sets `PreToolUseContext.OverrideArguments` to modified args
- **THEN** the tool SHALL execute with the overridden arguments, not the original

#### Scenario: PreToolUse hook blocks pipeline entirely
- **WHEN** a PreToolUse hook returns `HookResult.Stop("critical policy violation")`
- **THEN** no further tool calls in this iteration SHALL execute
- **AND** the stop reason SHALL be yielded as content

### Requirement: PostToolUse Hook Fired After Tool Execution

`AetherSoul` SHALL fire `HookPoint.PostToolUse` after each tool execution completes (success or failure), using `HookEngine.RunAllAsync` so all PostToolUse hooks execute regardless of individual results.

#### Scenario: Hook transforms tool result
- **WHEN** a PostToolUse hook sets `PostToolUseContext.OverrideResult = "redacted"`
- **THEN** "redacted" SHALL be added to the message list as the tool result, not the original output

#### Scenario: Failed tool still fires PostToolUse
- **WHEN** a tool execution fails with an error
- **THEN** `PostToolUseContext.Success` SHALL be false
- **AND** `PostToolUseContext.Error` SHALL contain the error message
- **AND** all PostToolUse hooks SHALL still execute

### Requirement: PostLlmCall Hook Fired After LLM Response

`AetherSoul` SHALL fire `HookPoint.PostLlmCall` after the LLM returns its final response (after tool loop completes), using `HookEngine.RunAllAsync`. The hook SHALL be able to set `ShouldRetry` (retry LLM call) or `OverrideContent` (replace response text).

#### Scenario: Hook retries LLM call
- **WHEN** a PostLlmCall hook sets `PostLlmCallContext.ShouldRetry = true` and `RetryReason = "incomplete response"`
- **THEN** `AetherSoul` SHALL call the LLM once more with the same messages

#### Scenario: Hook overrides response content
- **WHEN** a PostLlmCall hook sets `PostLlmCallContext.OverrideContent = "filtered response"`
- **THEN** "filtered response" SHALL be the final agent response content

### Requirement: No Hooks Means No Behavior Change

When `HookEngine` has zero registered hooks, all hook calls SHALL be no-ops that return `HookResult.Continue` immediately with zero allocation overhead. The agent loop SHALL behave identically to the pre-hook implementation.

#### Scenario: Zero hooks registered
- **WHEN** `HookEngine` is constructed with an empty hooks list
- **THEN** `RunAsync` SHALL return `HookResult.Continue` without iterating
- **AND** `RunAllAsync` SHALL return immediately
