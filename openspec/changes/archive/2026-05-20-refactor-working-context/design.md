# Design: Refactor Working Context

## Core Pattern

```
┌─────────────────────────────┐
│ System Prompt (static)      │
│ - Agent identity (1 line)   │
│ - Workspace path            │
│ - Tools available           │
│ - Rules (short)             │
├─────────────────────────────┤
│ Message 1 (user)            │
│ Message 2 (assistant)       │
│ ...                         │
│ Message N (user)            │
└─────────────────────────────┘
        │
        ▼
   LLM API Call (only async boundary)
```

## WorkingContext class

```csharp
class WorkingContext {
    string SystemPrompt     // built once
    List<LlmMessage> Messages  // conversation IS memory
    string WorkspacePath
    IReadOnlyList<LlmTool> Tools
    string SessionId
    
    void AddUser(string content)
    void AddAssistant(string content)
    void AddToolResult(...)
    void Reset()
    void Compact(int maxTokens)
}
```

## AetherSoul refactored

```csharp
async Task<AgentResponse> ProcessAsync(prompt) {
    _ctx.AddUser(prompt);
    var response = await _llm.CompleteAsync(request);  // ONLY async
    _ctx.AddAssistant(response.Content);
    return response;
}
```

No LoadIdentityContext. No LoadDailyMemory. No LoadWorkingState. No ContextAssembler per turn.

## Async boundaries

Only `_llm.CompleteAsync()` stays async. Everything else is synchronous:

| Operation | Before | After |
|-----------|--------|-------|
| File reads (config, memory) | async | sync |
| Tool execution (read/write/edit) | async | sync |
| Session management | async (SQLite) | sync (in-memory + async DB for persistence) |
| LLM API call | async | async (unchanged) |
| Context assembly | async (file reads) | sync (built once) |
