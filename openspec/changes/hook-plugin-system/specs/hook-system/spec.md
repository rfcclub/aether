## ADDED Requirements

### Requirement: IHook Interface Contract

The system SHALL provide an `IHook` interface in namespace `Aether.Plugins` with properties `Name` (string), `SubscribesTo` (HookPoint flags enum), and `Priority` (int, lower = earlier), and a method `Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct)`.

#### Scenario: Hook implementation compiles
- **WHEN** a class implements `IHook`
- **THEN** it SHALL compile with `Name`, `SubscribesTo`, `Priority`, and `ExecuteAsync` members

### Requirement: 14 Hook Points Defined

The system SHALL define a `HookPoint` flags enum with exactly these values: `None`, `OnMessageReceived`, `OnMessageRouted`, `OnMessageSent`, `PreLlmCall`, `PostLlmCall`, `PreToolUse`, `PostToolUse`, `OnSessionStart`, `OnSessionCompact`, `OnSessionEnd`, `OnMemoryWrite`, `OnMemoryPromote`, `OnAgentStart`, `OnAgentStop`, `OnHeartbeatTick`, and convenience combinations `All`, `MessageLifecycle`, `LlmLifecycle`, `ToolLifecycle`, `SessionLifecycle`, `AgentLifecycle`.

#### Scenario: Hook subscribes to multiple points
- **WHEN** a hook sets `SubscribesTo = HookPoint.PreLlmCall | HookPoint.PostLlmCall`
- **THEN** the hook SHALL be invoked at both hook points

### Requirement: Hook Engine Priority Execution

`HookEngine` SHALL execute hooks in ascending `Priority` order (lower numbers first). Hooks with equal priority SHALL be ordered by `Name` alphabetically for deterministic tiebreaking.

#### Scenario: Hooks execute in priority order
- **WHEN** three hooks are registered with priorities 10, 5, and 50
- **AND** the hook point fires
- **THEN** hook with priority 5 SHALL execute first, then 10, then 50

### Requirement: Hook Engine Short-Circuit on Pre Hooks

`HookEngine.RunAsync` SHALL stop executing hooks for the current hook point when any hook returns `HookResult` with `Success = false`. The stop reason SHALL be returned to the caller.

#### Scenario: PreLlmCall hook blocks pipeline
- **WHEN** a PreLlmCall hook returns `HookResult.Stop("escalation required")`
- **THEN** no subsequent PreLlmCall hooks SHALL execute
- **AND** the LLM call SHALL NOT proceed
- **AND** the stop reason "escalation required" SHALL be returned

### Requirement: Hook Engine Fire-and-Forget on Post Hooks

`HookEngine.RunAllAsync` SHALL execute all subscribed hooks regardless of individual `HookResult.Success` values. Post hooks (PostLlmCall, PostToolUse, OnMessageSent, OnSessionEnd, OnMemoryWrite, OnAgentStop) SHALL use this method.

#### Scenario: PostLlmCall hook fails but others proceed
- **WHEN** the first PostLlmCall hook returns `HookResult.Stop("error")`
- **THEN** subsequent PostLlmCall hooks SHALL still execute

### Requirement: Hook Exception Isolation

When a hook's `ExecuteAsync` method throws an unhandled exception, `HookEngine` SHALL catch the exception, log it at Error level, and continue executing remaining hooks for that hook point. A single failing hook SHALL NOT crash the agent or prevent other hooks from running.

#### Scenario: Hook throws but pipeline continues
- **WHEN** hook at priority 10 throws `InvalidOperationException`
- **THEN** the exception SHALL be caught and logged
- **AND** hook at priority 20 SHALL still execute

### Requirement: Hook Timeout Monitoring

`HookEngine` SHALL log a warning when any hook's `ExecuteAsync` takes longer than 500ms. If a hook takes longer than 5000ms, the HookEngine SHALL cancel its `CancellationToken` and treat it as an exception.

#### Scenario: Slow hook logged
- **WHEN** a hook's execution exceeds 500ms
- **THEN** a warning log SHALL be emitted with hook name, hook point, and duration

### Requirement: Typed Hook Contexts

The system SHALL provide typed context records for each hook point, all inheriting from a base `HookContext` record with shared fields: `AgentName`, `WorkspacePath`, `SessionId`, `Timestamp`, and a `Dictionary<string, object?> Bag` for cross-hook state sharing within a pipeline.

#### Scenario: Hook accesses typed context
- **WHEN** a PreToolUse hook's `ExecuteAsync` is called
- **THEN** the context SHALL be of type `PreToolUseContext` with `ToolName`, `Arguments`, `RawArguments`, `Risk`, `Denied`, `DenyReason`, and `OverrideArguments` fields

### Requirement: HookResult Struct

The system SHALL provide a `HookResult` value type with `Success` (bool) and `StopReason` (string?). Static factory properties SHALL be `HookResult.Continue` (Success=true) and `HookResult.Stop(string reason)` (Success=false).

#### Scenario: Hook returns continue
- **WHEN** a hook returns `HookResult.Continue`
- **THEN** `Success` SHALL be true and the pipeline SHALL proceed

### Requirement: HookPoint Enum Has Correct Values

Each HookPoint flag SHALL have a unique power-of-2 value: `OnMessageReceived = 1`, `OnMessageRouted = 2`, `OnMessageSent = 4`, `PreLlmCall = 8`, `PostLlmCall = 16`, `PreToolUse = 32`, `PostToolUse = 64`, `OnSessionStart = 128`, `OnSessionCompact = 256`, `OnSessionEnd = 512`, `OnMemoryWrite = 1024`, `OnMemoryPromote = 2048`, `OnAgentStart = 4096`, `OnAgentStop = 8192`, `OnHeartbeatTick = 16384`.

#### Scenario: HookPoint flags combine correctly
- **WHEN** `HookPoint.OnMessageReceived | HookPoint.OnMessageSent` is evaluated
- **THEN** the combined value SHALL be distinct from any single flag and represent both points
