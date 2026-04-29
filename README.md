# Aether

Personal AI agent framework in .NET 9. Model-agnostic, sandboxed, multi-channel.

Connect any LLM to any chat platform. Aether handles routing, sessions, memory, and safe tool execution — you bring the API key and the personality.

## Why

- **No Claude Code CLI dependency** — direct API calls to OpenRouter, Anthropic, Fireworks, or any OpenAI-compatible provider
- **OS-level sandboxing** — bwrap on Linux, no Docker required
- **One process, minimal deps** — just the .NET 9 runtime
- **Multi-agent ready** — agent profiles are directories; Aether is the runtime, not the personality

## Quick Start

```bash
# Prerequisites: .NET 9 SDK
dotnet --version  # should print 9.x.x

# Clone
git clone https://github.com/rfcclub/aether.git
cd aether

# Set your API key
export AETHER_llm__api_key="sk-or-..."  # OpenRouter key

# Create a group folder
mkdir -p groups/main
echo "# Main Group" > groups/main/CLAUDE.md

# Run one-shot prompt
cd src/Aether
dotnet run -- --prompt "Hello, world" --group main

# Or start the host (long-running with channels)
dotnet run
```

## Architecture

```
Channel → Message Router → AetherSoul → LLM Provider
                ↓               ↓
           Session Mgr     Tool Executor (sandboxed)
                ↓               ↓
            SQLite          bwrap / Process
```

| Component | Role |
|-----------|------|
| **Channels** | Telegram, WebSocket, Discord — plugin-based via `IChannel` |
| **Message Router** | Normalize inbound messages, match to group, enqueue |
| **AetherSoul** | Core agent loop: load context → call LLM → execute tools → respond |
| **LLM Providers** | OpenRouter, Anthropic, Fireworks, generic OpenAI-compatible — with health monitoring and automatic fallback |
| **Tool Executor** | Sandboxed bash/read/write/edit/glob/grep — bwrap isolation on Linux |
| **Memory System** | Dual-layer: CLAUDE.md files + SQLite FTS5 search |
| **Session Manager** | Conversation history with token-aware truncation and compaction |
| **Skill System** | Markdown-based skill definitions with keyword auto-trigger |

## Configuration

Two sources, merged (env wins):

**`appsettings.json`** — checked into repo with placeholder values.
**Environment variables** — prefix with `AETHER_`, use `__` for nesting.

```bash
# Required: at least one provider key
export AETHER_llm__api_key="sk-or-..."           # OpenRouter
export AETHER_fireworks__api_key="..."            # Fireworks
export AETHER_anthropic__api_key="..."            # Anthropic direct

# Optional: Telegram channel
export AETHER_channels__telegram__enabled="true"
export AETHER_channels__telegram__bot_token="..."

# Optional: WebSocket channel
export AETHER_channels__websocket__enabled="true"
export AETHER_channels__websocket__port="5099"
```

See [SETUP.md](SETUP.md) for full configuration reference.

## Agent Profiles

Aether is a framework. Agent personalities live in `agents/<name>/` directories and are **not** part of the framework — they're gitignored.

Each agent directory contains:
```
agents/<name>/
├── SOUL.md          # Voice and personality
├── USER.md          # Who the human is
├── IDENTITY.md      # Self-model and boundaries
├── MEMORY.md        # Long-term memory
├── HEARTBEAT.md     # Periodic task list
├── AGENTS_GUARD.md  # Red lines and anti-hang rules
├── TASK_INBOX.md    # Incoming tasks
├── TASK_REPORT.md   # Completed task reports
└── memory/          # Daily episodic logs
```

Start Aether with your agent:
```bash
dotnet run --agent.name my-agent --agent.root agents
```

The `AgentConfig` record in code defines which files are loaded and in what order. FEOFALLS cognitive architecture (v1.9) is supported via `FeofallsConfig` — constitution, identity, cognitive, learning, and working-state layers.

## Project Structure

```
aether/
├── src/Aether/           # Framework source
│   ├── Agent/            # AetherSoul core loop
│   ├── Agents/           # Agent profile system, heartbeat, FEOFALLS
│   ├── Channels/         # IChannel: Telegram, WebSocket
│   ├── Data/             # SQLite, schema
│   ├── Memory/           # File-based + SQLite FTS5 memory
│   ├── Providers/        # LLM provider abstractions
│   ├── Routing/          # Message routing and queues
│   ├── SelfImprovement/  # 5-phase pipeline for agent self-evolution
│   ├── Sessions/         # Session persistence
│   ├── Skills/           # Skill registry, parser, triggers
│   └── Tooling/          # Tool registry, hot-reload, sandbox
├── src/Aether.Tui/       # Terminal.Gui chat interface
├── tests/Aether.Tests/   # xUnit test suite (112+ tests)
├── ARCHITECTURE.md        # Full architecture specification
├── SETUP.md              # Setup and troubleshooting guide
└── PROGRESS.md           # Implementation status
```

## Providers

Three-tier routing with automatic fallback:

| Tier | Provider | Best for |
|------|----------|----------|
| Primary | Fireworks | Fast, cheap (DeepSeek, Qwen) |
| Escalation | OpenRouter | Better models (Claude, GPT) |
| Safety | Anthropic | Direct Claude, highest quality |

Health monitoring with circuit breaker: 3 failures → 60s cooldown. Complex prompts auto-escalate.

## Running Tests

```bash
dotnet test
```

## TUI

Terminal chat interface via Terminal.Gui:

```bash
cd src/Aether.Tui
dotnet run
```

F5 cycles groups, PgUp/PgDn scrolls history, Ctrl+W toggles word wrap.

## License

MIT
