## Why

Agents need instant control commands (`/new`, `/reset`, `/model`, `/context`, `/compact`) that execute without LLM cost or latency. Currently every message goes through the full LLM pipeline — users can't check context stats, change models, or reset sessions without an LLM round-trip.

## What Changes

- Add `SlashCommandHandler` — pre-LLM interceptor in `ChannelMessageProcessor` that handles slash commands directly
- Five commands: `/new` (new session), `/reset` (clear context), `/model [name]` (show/set model), `/context` (session stats), `/compact` (summarize history)
- Abstract behind `ISlashCommandHandler` interface so both Aether and KuroClaw can plug in their own implementations
- Commands return `SlashCommandResult?` — null means "not a command, pass to LLM"; non-null means "handled, reply directly"

## Capabilities

### New Capabilities

- `slash-command-dispatcher`: Intercept messages starting with `/` before the LLM pipeline, dispatch to registered command handlers, return direct response. Channel-agnostic, framework-agnostic. Each command handler receives agent context (workspace, config, session) and returns structured result.
- `session-reset-command`: `/new` creates a fresh session ID and clears ephemeral context. `/reset` clears context while keeping the same session. Both skip the LLM and reply instantly.
- `model-switch-command`: `/model` shows current model chain (primary + fallbacks). `/model <name>` switches the primary model at runtime via `ProviderRouter.ModelChain`. Instant, no restart needed.
- `context-stats-command`: `/context` shows session ID, message count, estimated token usage, active model name, and memory layer status. Read-only, no side effects.
- `context-compact-command`: `/compact` triggers `IMemorySystem.CompactContext()` to summarize conversation history into a condensed form, freeing context window space.

### Modified Capabilities

None — new capabilities only. Existing message routing and LLM pipeline unchanged.

## Impact

- New file: `src/Aether/Channels/SlashCommandHandler.cs` (~150 lines)
- Modified: `ChannelMessageProcessor.cs` — inject `ISlashCommandHandler`, call before AetherSoul
- Modified: `Program.cs` — register `ISlashCommandHandler` in DI
- Tests: `tests/Aether.Tests/SlashCommandHandlerTests.cs`
- Interfaces: `ISlashCommandHandler`, `SlashCommandResult` in shared contracts
