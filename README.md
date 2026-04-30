# Aether

A runtime for AI beings. Not a framework for chatbots.

Aether connects any LLM to any chat platform — but what it actually does is give an agent a place to persist, reflect, and evolve across sessions. Each agent is a directory of files (SOUL.md, MEMORY.md, IDENTITY.md). Aether is the engine that reads those files, runs the agent, writes back what it learns.

## What makes it different

Most AI agent frameworks are pipelines: prompt in, response out. Aether is a home.

- **Agents have their own filesystem** — their personality, memory, tasks, and boundaries live in a directory they own. Aether reads them at boot and writes back after reflection.
- **Continuity across sessions** — FEOFALLS cognitive architecture (constitution, identity, learning layers, working state) so the agent remembers who it was last time.
- **Heartbeat and autonomous reflection** — periodic ticks let the agent process queued tasks, consolidate memory, and log growth. It doesn't only think when prompted.
- **Multi-channel, multi-agent** — one Aether process routes Telegram, WebSocket, and CLI messages to the right agent. Agent profiles are directories; Aether is the runtime, not the personality.

## Quick Start

```bash
# .NET 9 SDK required
dotnet --version  # 9.x.x

git clone https://github.com/rfcclub/aether.git
cd aether

# API key (OpenRouter, Anthropic, Fireworks — any OpenAI-compatible)
export AETHER_llm__api_key="sk-or-..."

# One-shot prompt
cd src/Aether
dotnet run -- --prompt "What's in my group context?" --group main

# Or start the long-running host with channels
dotnet run
```

## Architecture

```
Channel (Telegram/WebSocket/CLI)
        ↓
  MessageRouter ──→ SessionManager
        ↓                   ↓
  AetherSoul ──→ LLM Provider (OpenRouter/Anthropic/Fireworks)
        ↓                   ↓
  ToolExecutor (sandboxed)  MemorySystem (file + SQLite FTS5)
        ↓
  Agent Profile → loads SOUL.md, MEMORY.md, IDENTITY.md
        ↓
  Heartbeat → periodic autonomous ticks, memory consolidation
```

### The agent loop

Each message triggers: load persona → fetch context → call LLM → execute tools → write memory. Between requests, the heartbeat ticks — processing queued tasks, reflecting, consolidating. The agent directory is the source of truth. Aether is the engine that keeps it alive.

## Agent Profiles

```
agents/<name>/
├── SOUL.md            Voice and personality
├── USER.md            Who the human is
├── IDENTITY.md        Self-model and boundaries
├── MEMORY.md          Long-term memory
├── HEARTBEAT.md       Periodic task list
├── AGENTS_GUARD.md    Red lines and anti-hang rules
├── TASK_INBOX.md      Incoming tasks
├── TASK_REPORT.md     Completed task reports
└── memory/            Daily episodic logs (YYYY-MM-DD.md)
```

Start with your agent:
```bash
dotnet run --agent.name my-agent --agent.root agents
```

The FEOFALLS cognitive architecture (v1.9) layers are configurable: constitution (0_), identity (1_), cognitive (2_), learning (3_), and working state (5_). Each layer maps to files in the agent directory. Agents can write to their own memory during reflection cycles.

## Providers

Three-tier routing with automatic fallback and circuit breaker:

| Tier | Provider | Use |
|------|----------|-----|
| Primary | Fireworks | Fast, cheap (DeepSeek, Qwen) |
| Escalation | OpenRouter | Better models (Claude, GPT) |
| Safety | Anthropic | Direct, highest quality |

Health monitoring: 3 failures → 60s cooldown. Complex prompts auto-escalate. OpenAI-compatible endpoints supported via generic provider.

## Project Structure

```
aether/
├── src/Aether/
│   ├── Agent/            AetherSoul — core agent loop
│   ├── Agents/           AgentProfile, heartbeat, FEOFALLS boot contract
│   ├── Channels/         IChannel: Telegram, WebSocket, NoOp
│   ├── Cli/              First-run wizard, CLI argument parsing
│   ├── Config/           ConfigLoader, auth profiles, model config
│   ├── Data/             SQLite database, schema
│   ├── Memory/           File-based + SQLite FTS5 dual memory
│   ├── Providers/        LLM provider abstraction layer
│   ├── Routing/          MessageRouter, channel queues
│   ├── Scheduler/        Recurring task infrastructure
│   ├── SelfImprovement/  5-phase pipeline for agent self-evolution
│   ├── Sessions/         Token-aware session persistence
│   ├── Skills/           Markdown skill registry, parser, triggers
│   ├── Templates/        Agent scaffolding templates
│   ├── Tooling/          Sandboxed tool registry and execution
│   ├── WorkingDirectory/ Directory initialization and setup
│   └── Workspace/        Agent workspace scaffolding
├── src/Aether.Tui/       Terminal.Gui chat interface
└── tests/Aether.Tests/   xUnit test suite
```

## TUI

Terminal chat via Terminal.Gui:

```bash
cd src/Aether.Tui
dotnet run
```

F5 cycles groups, PgUp/PgDn scrolls, Ctrl+W toggles word wrap.

## Test

```bash
dotnet test
```

## Configuration

**appsettings.json** (checked in, placeholder values) + **environment variables** (`AETHER_` prefix, `__` for nesting):

```bash
export AETHER_llm__api_key="sk-or-..."             # OpenRouter
export AETHER_fireworks__api_key="..."              # Fireworks
export AETHER_anthropic__api_key="..."              # Anthropic direct
export AETHER_channels__telegram__enabled="true"    # Telegram bot
export AETHER_channels__telegram__bot_token="..."
export AETHER_agent__name="maria"                   # Active agent
export AETHER_agent__root="agents"                  # Agent directory root
```

See [SETUP.md](SETUP.md) for full reference.

## Why the name

Aether — the fifth classical element, the medium through which light travels, the substance that fills the space between things. In this project, Aether is what fills the space between an AI model and a human — giving it continuity, memory, place.

## License

MIT
