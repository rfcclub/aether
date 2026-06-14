# Design: Context Compaction

## Architecture Overview

Context compaction in Aether will operate across three distinct tiers. The goal is to maintain the agent's identity ("Tension") and conversational heat while rigorously defending against token bloat.

### Tier 1: Mechanical Trimming (Read-Time)

**Mechanism**: Fast, read-time filtering within `SessionManager.GetHistoryAsync`.
**Logic**: 
- Calculate the token usage of the raw history.
- If the token count exceeds a configured threshold (e.g., 8,000 tokens), aggressively strip out `ToolCall` and `ToolResult` messages that are older than the N most recent turns.
- Keep the `Assistant` message that followed the tool result, as it usually contains the agent's synthesis of the data.

### Tier 2: State Injection (Autonomy)

**Mechanism**: Agent-driven memory management.
**Logic**:
- Enhance the Agent's system prompt (or add a dedicated skill) to recognize narrative closure.
- "When you finish a major task or resolve a deep discussion, you MUST extract the core outcome or tension and write it to `MEMORY.md` or your substrate state."
- The existing `/reset` slash command (or an automated trigger) can then be used to wipe the transient session history, relying entirely on the fortified static files.

### Tier 3: Semantic Summary (Background Fallback)

**Mechanism**: Asynchronous summarization via a new `SessionCompactionService`.
**Logic**:
- In `ChannelMessageProcessor`, after a turn completes, check the session's total token count.
- If it exceeds the critical threshold (e.g., 16,000 tokens) *even after* mechanical trimming, queue the session for summarization.
- The `SessionCompactionService` reads the oldest M messages (leaving the 10 most recent untouched).
- It calls the LLM with a specific summary prompt: *"Summarize the following conversation, preserving key facts, decisions, and the emotional/philosophical state of the agent."*
- The original M messages are deleted from `AetherDb` and replaced with a single `System` message containing the summary.

## Data Structures

No immediate changes to `AetherDb` schema are strictly required, as the summary can be stored as a standard `SessionMessage` with `Role = "System"` and a specific metadata tag indicating it is a compaction artifact.

## Flow

1. **User sends message.**
2. `ChannelMessageProcessor` calls `SessionManager.GetHistoryAsync(limit)`.
3. **[Tier 1]** `GetHistoryAsync` drops old tool payloads if the token limit is tight.
4. `AetherSoul` processes the prompt.
5. **[Tier 2]** LLM decides to write to `MEMORY.md` based on its instructions.
6. Turn completes. `ChannelMessageProcessor` evaluates new session size.
7. **[Tier 3]** If size > Critical Threshold, queue `sessionId` to `SessionCompactionService`.
8. Background worker summarizes old messages, updates SQLite.