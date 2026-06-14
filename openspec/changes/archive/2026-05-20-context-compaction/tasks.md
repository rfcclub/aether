## 1. Foundation: Mechanical Trimming (Tier 1)

- [x] 1.1 Modify `SessionManager.GetHistoryAsync(string sessionId, int maxTokens, CancellationToken ct)` to implement dynamic token budgeting.
- [x] 1.2 Implement logic to identify and drop `ToolCall` and `ToolResult` messages older than a specified "recent turn window" (e.g., older than 5 turns) if the history size approaches `maxTokens`.
- [x] 1.3 Add unit tests in `SessionManagerTests.cs` to verify that tool data is dropped while user/assistant conversational flow is preserved when limits are hit.

## 2. Autonomy: State Injection (Tier 2)

- [x] 2.1 Update the default system prompt template (or create a new core skill) instructing the agent on *when* and *how* to perform state injection (saving conclusions to `MEMORY.md` or `2B/MEMBRANE_STATE.md`).
- [x] 2.2 Verify that the `/reset` slash command cleanly purges session history so the agent can fall back to the newly injected state without continuity loss.

## 3. Fallback: Semantic Summary (Tier 3)

- [x] 3.1 Create `src/Aether/Sessions/SessionCompactionService.cs` extending `BackgroundService`. It will maintain a concurrent queue of `sessionId`s needing compaction.
- [x] 3.2 Implement the summarization logic within the service: 
      - Fetch all messages for the session.
      - Identify the oldest M messages (preserving the 10 most recent).
      - Use `ILLMProvider` directly to generate a concise summary.
      - Delete the M messages from the database and insert the new summary `System` message.
- [x] 3.3 Wire `ChannelMessageProcessor` to enqueue the `sessionId` to `SessionCompactionService` if the token count (or message count) post-turn exceeds a critical threshold (e.g., 50 messages).
- [x] 3.4 Register `SessionCompactionService` in `Program.cs`.

## 4. Testing & Verification

- [x] 4.1 Create an integration test simulating a 100-turn conversation to ensure the `SessionCompactionService` triggers and successfully replaces old messages with a summary.
- [x] 4.2 Verify that the 10 most recent messages are perfectly intact post-compaction.