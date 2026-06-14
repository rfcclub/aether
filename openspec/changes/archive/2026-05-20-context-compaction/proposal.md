# Proposal: Multi-Tiered Context Compaction (Immune System)

## Problem

Aether's current `SessionManager` lacks an automated way to manage token growth in long-running sessions. The `/compact` command only truncates ephemeral context to 4000 tokens, but the conversation history stored in `AetherDb` grows indefinitely. When the history exceeds the LLM's context window (or attention span), the agent begins to hallucinate, forgets its early instructions, or loses its core identity ("Tension").

## Proposed Solution

Implement a 3-layer "Immune System" for context compaction, prioritizing agent autonomy and token efficiency:

1.  **Tier 1: Mechanical Trimming (C)**
    When fetching history, automatically filter out old `ToolCall` and `ToolResult` blocks. The agent only needs to remember the conclusions it drew from tool usage, not the raw output (e.g., massive file reads or web scrapes).
2.  **Tier 2: State Injection (B)**
    Empower the agent to autonomously recognize when a conversational arc or task is complete. The agent will use tools to distill and inject the "Tension" or key outcomes directly into static memory files (`MEMORY.md` or `2B/MEMBRANE_STATE.md`).
3.  **Tier 3: Semantic Summary (A) - The Fallback**
    If the history remains too large even after mechanical trimming (e.g., a long philosophical discussion without tool usage), the system triggers an asynchronous LLM call. It retains the 10 most recent messages for conversational heat, summarizes the older messages, and replaces them in the database with a single `[System Summary]` node.

## Scope

-   **Modified files**: `SessionManager.cs`, `AetherSoul.cs`, `ChannelMessageProcessor.cs`, `AetherDb.cs`.
-   **New background service**: `SessionCompactionService.cs` (to handle async summarization without blocking the main conversational loop).
-   **Prompt changes**: Updates to the core agent system prompt to teach it to self-compact via memory files.

## Out of Scope

-   Vector database integration (Qdrant). This remains a separate feature for long-term semantic search.