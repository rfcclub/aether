# Aether Setup Guide

## Prerequisites

- **.NET 9 SDK** — [download](https://dotnet.microsoft.com/download/dotnet/9.0)
- **OpenRouter API key** (or direct Anthropic/Fireworks key) — at least one LLM provider
- **Telegram bot token** (optional) — for Telegram channel integration
- **Linux** (WSL2 on Windows works) — required for bwrap sandbox; macOS and native Windows run without sandbox isolation

Verify installation:

```bash
dotnet --version  # should print 9.x.x
```

## Quick Start

```bash
# 1. Clone and enter the repo
cd aether/src/Aether

# 2. Set your API key (pick one)
export AETHER_llm__api_key="sk-or-..."

# 3. Create group folder
mkdir -p groups/main
echo "## Main Group" > groups/main/CLAUDE.md

# 4. Initialize the database (happens automatically on first run)

# 5. Run the host
dotnet run

# Or with flags:
dotnet run -- --prompt "Hello, Aether" --group main
```

The host starts, initializes the SQLite database, connects the channel (Telegram or no-op), and listens for inbound messages.

## Configuration

Aether reads configuration from two sources (merged, latter wins):

1. `appsettings.json` — checked into repo (template with placeholders)
2. Environment variables prefixed with `AETHER_`

### appsettings.json

Located at `src/Aether/appsettings.json`. Default values shown below — unset keys fall back to these.

```json
{
  "assistant": {
    "name": "Aether",
    "trigger": "@Aether"
  },
  "channels": {
    "telegram": {
      "enabled": false,
      "bot_token": "${TELEGRAM_BOT_TOKEN}"
    }
  },
  "llm": {
    "provider": "openrouter",
    "model": "anthropic/claude-3-5-sonnet",
    "api_key": "${OPENROUTER_API_KEY}",
    "base_url": "https://openrouter.ai/api/v1",
    "timeout_seconds": 90
  },
  "fireworks": {
    "api_key": "${FIREWORKS_API_KEY}",
    "model": "accounts/fireworks/models/deepseek-v3-0324",
    "base_url": "https://api.fireworks.ai/inference/v1"
  },
  "anthropic": {
    "api_key": "${ANTHROPIC_API_KEY}",
    "model": "claude-3-5-sonnet-20241022",
    "base_url": "https://api.anthropic.com"
  },
  "database": {
    "path": "store/aether.db",
    "schema": "Data/Schema.sql"
  },
  "memory": {
    "db_path": "store/memory.db",
    "file_path": "store/MEMORY.md"
  },
  "groups": {
    "path": "groups"
  },
  "sandbox": {
    "type": "bwrap",
    "timeout_ms": 30000,
    "max_memory_mb": 512,
    "network_enabled": false,
    "allowed_paths": [
      "/workspace/group",
      "/workspace/global"
    ]
  },
  "scheduler": {
    "check_interval_ms": 60000
  },
  "provider_health": {
    "check_interval_seconds": 30,
    "failure_threshold": 3
  }
}
```

### Environment Variables

Map `appsettings.json` keys to env vars by replacing `:` with `__` (double underscore) and prefixing `AETHER_`:

| Config key | Environment variable |
|---|---|
| `llm:api_key` | `AETHER_llm__api_key` |
| `llm:model` | `AETHER_llm__model` |
| `channels:telegram:enabled` | `AETHER_channels__telegram__enabled` |
| `channels:telegram:bot_token` | `AETHER_channels__telegram__bot_token` |
| `fireworks:api_key` | `AETHER_fireworks__api_key` |
| `anthropic:api_key` | `AETHER_anthropic__api_key` |
| `database:path` | `AETHER_database__path` |
| `groups:path` | `AETHER_groups__path` |

Single-underscore fallback (`AETHER_llm_api_key`) also works but `__` is the canonical form.

### API Key Options

You can provide API keys three ways, in order of precedence:

1. **`--api-key-file` flag** (prompt harness only) — path to file containing the key
   ```bash
   dotnet run -- --prompt "hello" --api-key-file /path/to/key.txt
   ```
2. **Environment variable** — `AETHER_llm__api_key=sk-or-...`
3. **`appsettings.json`** — `"api_key": "sk-or-..."` (don't commit real keys)

## Telegram Channel Setup

### 1. Create a Bot

1. Open Telegram and message **@BotFather**
2. Send `/newbot` and follow the prompts
3. Save the bot token (looks like `123456:ABC-DEF1234gh...`)

### 2. Configure Aether

```bash
export AETHER_channels__telegram__enabled="true"
export AETHER_channels__telegram__bot_token="123456:ABC-DEF1234gh..."
```

Or in `appsettings.json`:

```json
{
  "channels": {
    "telegram": {
      "enabled": true,
      "bot_token": "123456:ABC-DEF1234gh..."
    }
  }
}
```

### 3. Add Bot to a Group

1. Create a Telegram group
2. Add your bot as a member
3. Register the group in Aether's database:

```sql
INSERT INTO groups (jid, name, folder, is_main, requires_trigger, trigger)
VALUES ('telegram:YOUR_CHAT_ID', 'My Group', 'my-group', 1, 0, NULL);
```

The `jid` uses the format `telegram:{chat_id}`. Find the chat ID by sending a message in the group and checking the bot's logs.

### 4. Start the Host

```bash
cd src/Aether
dotnet run
```

The host connects, logs `Telegram channel connected as @YourBot (id=...)`, and begins polling for messages.

## Providers

Aether supports three provider tiers with automatic fallback:

| Tier | Provider | Best for |
|------|----------|----------|
| **Primary** | Fireworks | Fast, cheap models (DeepSeek, Qwen) |
| **Escalation** | OpenRouter | Better models (Claude, GPT) |
| **Safety** | Anthropic | Direct Claude access, highest quality |

### Provider Routing

- Simple prompts → Fireworks (primary)
- Complex prompts (contains "architecture", "design", "security" etc.) → escalates to OpenRouter
- Health monitoring with circuit breaker: 3 failures → 60s cooldown

### Getting API Keys

- **OpenRouter**: https://openrouter.ai/keys
- **Fireworks**: https://fireworks.ai/account/api-keys
- **Anthropic**: https://console.anthropic.com/settings/keys

At least one provider key is required. OpenRouter is the recommended starting point — it gives access to all major models.

## Group Folders and CLAUDE.md

Aether uses a file-based context system identical to NanoClaw/Claude Code:

```
groups/
├── CLAUDE.md              # Global context (loaded for every group)
├── main/
│   └── CLAUDE.md          # Main group identity and instructions
└── my-project/
    ├── CLAUDE.md          # Project-specific context
    └── ...                # Project files
```

- `groups/CLAUDE.md` — loaded for ALL groups as base context
- `groups/{folder}/CLAUDE.md` — loaded for that specific group
- Only `CLAUDE.md` files are loaded; other files in the group folder are accessible via tools

The group folder name is the key used throughout Aether (`--group` flag, `group_folder` column in DB).

## Database

SQLite database at `store/aether.db` (created automatically on first run). Schema includes:

| Table | Purpose |
|-------|---------|
| `sessions` | Conversation session tracking |
| `messages` | Full-text searchable message history |
| `groups` | Group/channel routing configuration |
| `provider_usage` | Cost and latency tracking per LLM call |
| `promotion_candidates` | Memory system candidate tracking |
| `tasks` / `task_runs` | Scheduled task tracking |

Initialize manually (optional):

```bash
cd src/Aether
sqlite3 store/aether.db < Data/Schema.sql
```

The host calls `AetherDb.InitializeAsync()` on startup, which runs the schema idempotently (`CREATE TABLE IF NOT EXISTS`).

## Running

### Host Mode (long-running with channels)

```bash
cd src/Aether
dotnet run
```

Starts the .NET Generic Host with DI, connects channels, and runs until interrupted (Ctrl+C).

### Prompt Harness (one-shot)

```bash
cd src/Aether
dotnet run -- --prompt "Your question here" --group main
```

Additional flags:

| Flag | Description |
|------|-------------|
| `--prompt <text>` | One-shot prompt (enables harness mode) |
| `--group <name>` | Group folder name (default: `main`) |
| `--model <name>` | Override the LLM model |
| `--api-key-file <path>` | Read API key from file |
| `--timeout-seconds <n>` | Prompt timeout (default: 90) |
| `--database-path <path>` | Custom DB path |
| `--trace-startup` | Print startup timing traces to stderr |
| `--smoke` | Exit immediately (CI smoke test) |

### Examples

```bash
# Quick test with OpenRouter
export AETHER_llm__api_key="sk-or-..."
dotnet run -- --prompt "What is the capital of France?" --group main

# Use Fireworks instead
export AETHER_fireworks__api_key="..."
dotnet run -- --model "accounts/fireworks/models/qwen-qwq-32b" --prompt "Explain quantum computing"

# Trace startup for debugging
dotnet run -- --trace-startup
```

## Running Tests

```bash
# From repo root
dotnet test

# Or from test project directory
cd tests/Aether.Tests
dotnet test
```

78 xUnit tests covering: AetherSoul, ProviderRouter, SkillSystem, OpenRouter, SessionManager, FileMemory, MessageRouter, ToolRegistry, AetherDb, and more.

## Troubleshooting

### "Aether schema file was not found"

Aether can't find `Data/Schema.sql`. Make sure you run from the `src/Aether` directory, or set:
```bash
export AETHER_database__schema="/absolute/path/to/Schema.sql"
```

### "All provider groups failed"

No LLM provider responded. Check:
1. API key is set and valid
2. Network can reach the provider URL
3. Model name is correct for the provider

### "Telegram channel is not connected"

Appears when `channels:telegram:enabled` is `false` or `bot_token` is empty. Verify:
```bash
echo $AETHER_channels__telegram__enabled  # should be "true"
echo $AETHER_channels__telegram__bot_token  # should be your bot token
```

### Database locked

SQLite uses single-writer concurrency. Only one Aether host should run per database file. If you get "database is locked", stop any other Aether processes.

### Tool execution times out

Increase sandbox timeout in config:
```json
{
  "sandbox": {
    "timeout_ms": 60000
  }
}
```

## Project Structure

```
aether/
├── SETUP.md                           # This file
├── ARCHITECTURE.md                    # Full architecture specification
├── PROGRESS.md                        # Implementation status
├── Aether.sln                         # .NET solution
├── src/Aether/
│   ├── Program.cs                     # Entry point, DI wiring
│   ├── Aether.csproj                  # Project file with dependencies
│   ├── appsettings.json               # Default configuration
│   ├── Agent/                         # AetherSoul core loop
│   ├── Channels/                      # IChannel implementations
│   ├── Data/                          # SQLite, Schema.sql
│   ├── Memory/                        # IMemorySystem implementations
│   ├── Providers/                     # LLM provider abstractions
│   ├── Routing/                       # Message routing
│   ├── Sessions/                      # Session persistence
│   ├── Skills/                        # Skill registry and trigger
│   └── Tooling/                       # Tool registry and sandbox
├── tests/Aether.Tests/                # xUnit test suite
│   ├── TestHelpers.cs                 # Shared test doubles
│   └── *Tests.cs                      # 13 test classes
├── groups/                            # Group data and CLAUDE.md files
└── store/                             # SQLite databases (gitignored)
```
