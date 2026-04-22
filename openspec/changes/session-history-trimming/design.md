## Context

`AetherSoul` loads up to 40 messages. With tool loops, a single agent turn can generate 6–10 messages (tool_use + tool_result pairs), and tool outputs can be large. Claude's context window is ~200K tokens but OpenRouter imposes model-specific limits. Exceeding them causes API errors. A lightweight trim prevents this without the complexity of full compaction (summarization).

## Goals / Non-Goals

**Goals:**
- Drop oldest history messages when estimated token count exceeds budget
- Keep the system prompt intact (never trimmed)
- Provide a `CharTokenEstimator` (chars/4 heuristic) as the default — no tokenizer dependency
- Make budget configurable; default to 80K tokens (leaves headroom for system prompt + new message + response)

**Non-Goals:**
- Full compaction / summarization (future phase)
- Exact token counting via tiktoken / anthropic tokenizer (overkill for a heuristic guard)
- Trimming individual messages (always drop whole messages, oldest first)

## Decisions

**D1 — Trim in AetherSoul, not SessionManager**: Trimming is a concern of the agent loop (how much history to feed the LLM), not of storage. `SessionManager` stores all messages; `AetherSoul` decides what to include in the request.

**D2 — Character heuristic**: 1 token ≈ 4 chars is a well-known approximation. `CharTokenEstimator.Estimate(text)` returns `text.Length / 4 + 1`. Overestimates slightly, which is the safe direction.

**D3 — Drop oldest first, preserve pairs**: When dropping, drop the oldest message. To preserve message integrity in tool-use turns (which pair tool_use + tool_result), optionally drop entire paired groups. In the MVP, drop one message at a time for simplicity.

**D4 — Static utility**: `HistoryTrimmer.Trim(messages, systemPromptTokens, budget, estimator)` returns a new trimmed list. Pure function, no I/O, easy to test.

## Risks / Trade-offs

- **Heuristic inaccuracy**: Character-based estimation can be off by 20–30% for non-Latin text. This is acceptable for a guard — the goal is to avoid hard overflow, not exact fitting.
- **Tool result message orphaning**: Dropping a `tool_use` message without its `tool_result` (or vice versa) could confuse the LLM. Acceptable for now — LLMs are robust to missing history context.
- **System prompt not counted in budget**: The system prompt token count should be estimated separately and subtracted from the budget before trimming history. If `memoryContext` is large, this matters.
