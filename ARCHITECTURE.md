# Aether — Architecture Specification
> **Project:** Rewrite of NanoClaw from TypeScript/Node.js to .NET 9  
> **Author:** Maria (Architecture Lead)  
> **Date:** 2026-04-19  
> **Status:** Draft — For Review  

---

## 1. Executive Summary

**What is Aether?**

Aether is a lightweight, secure personal AI agent written in .NET 9. It replaces NanoClaw's TypeScript/Node.js + Docker Container architecture with a native .NET application that connects to any LLM via API (no Claude Code CLI dependency).

**Core Philosophy:**
- **Security through OS-level isolation**, not VMs
- **Simplicity over features** — one process, minimal dependencies
- **AI-Native Development** — designed to be configured and extended by AI
- **Model Agnostic** — works with any LLM via OpenRouter or direct API

**Key Changes from NanoClaw:**

| Aspect | NanoClaw (TypeScript) | Aether (.NET 9) |
|--------|---------------------|-----------------|
| Runtime | Node.js + Docker | .NET 9 (native) |
| LLM | Claude Code CLI subprocess | Direct API calls (any model) |
| Isolation | Linux Container (Docker/Lima) | OS Process + bwrap syscall sandbox |
| IPC | stdin/stdout + File polling | Named Pipes / System.Threading.Channels |
| Channels | Skills-based (code injection) | Plugin-based (.NET assemblies) |
| Memory | CLAUDE.md files only | CLAUDE.md + Vector DB (optional) |
| Dependency | Heavy (Node, Docker, Claude CLI) | Light (.NET runtime only) |

---

## 2. System Architecture

### 2.1 High-Level Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           AETHER HOST (.NET 9)                          │
│                                                                         │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐               │
│  │   Channel    │    │   Message   │    │   Task      │               │
│  │   Manager    │───▶│   Router    │    │   Scheduler │               │
│  └──────────────┘    └──────────────┘    └──────────────┘               │
│         │                   │                   │                      │
│         └───────────────────┼───────────────────┘                      │
│                             │                                          │
│                             ▼                                          │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                     AETHER SOUL (Maria)                          │   │
│  │                                                                  │   │
│  │   • Main conversation loop                                       │   │
│  │   • Memory management (CLAUDE.md + Vector DB)                    │   │
│  │   • Tool orchestration                                           │   │
│  │   • Session management                                           │   │
│  │   • LLM integration (via Semantic Kernel or direct API)           │   │
│  │                                                                  │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                             │                                          │
│                             │ spawns                                    │
│                             ▼                                          │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                    SANDBOXED PROCESSES                          │   │
│  │                                                                  │   │
│  │   ┌──────────────┐  ┌──────────────┐  ┌──────────────┐          │   │
│  │   │  Bash Exec   │  │  MCP Server  │  │  Sub-Agent  │          │   │
│  │   │  (bwrap)    │  │  (optional)  │  │  (optional)  │          │   │
│  │   └──────────────┘  └──────────────┘  └──────────────┘          │   │
│  │                                                                  │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                     DATA LAYER                                   │   │
│  │   • SQLite (messages, sessions, tasks, config)                    │   │
│  │   • File System (CLAUDE.md, group folders, transcripts)           │   │
│  │   • Vector DB (optional — Qdrant, SQLite-vss)                   │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

### 2.2 Component Overview

| Component | Responsibility | Key Files |
|-----------|--------------|-----------|
| **Channel Manager** | Connect to messaging platforms (Telegram, Discord, etc.) | `Channels/` |
| **Message Router** | Route inbound messages to correct group/session | `Routing/` |
| **Aether Soul** | Main agent loop, memory, tool orchestration | `Agent/` |
| **LLM Provider** | Abstraction over OpenRouter/Anthropic/OpenAI APIs | `Providers/` |
| **Tool Executor** | Sandboxed Bash + MCP tool execution | `Tools/` |
| **Task Scheduler** | Cron-based and interval-based task execution | `Scheduler/` |
| **Session Manager** | Conversation history, compaction, resumption | `Sessions/` |
| **Memory System** | CLAUDE.md hierarchy + vector search | `Memory/` |

---

## 3. Component Specifications

### 3.1 Channel Manager

**Pattern:** Plugin-based via .NET assembly loading

```csharp
public interface IChannel
{
    string Name { get; }
    Task ConnectAsync(CancellationToken ct);
    Task DisconnectAsync();
    Task SendMessageAsync(string chatId, string text, CancellationToken ct);
    bool IsConnected { get; }
    bool OwnsChatId(string chatId);
    Task SetTypingAsync(string chatId, bool isTyping, CancellationToken ct);
    event EventHandler<InboundMessage> OnMessage;
}
```

**Built-in Channels:**
- Telegram (via Telegram.Bot)
- Discord (via Discord.NET)
- WhatsApp (via Baileys or WhatsApp Web)
- Slack (via SlackNet)
- Local (self-chat / main control)

**Plugin Loading:**
```csharp
// Load channel plugins from Channels/ directory
var channelPlugins = Directory.GetFiles("Channels/", "*.dll")
    .Select Assembly.LoadFrom)
    .SelectMany(a => a.GetTypes<IChannel>());
```

### 3.2 Message Router

**Responsibilities:**
- Normalize inbound messages from all channels
- Match to correct group based on chat ID
- Apply trigger word filtering (if configured)
- Enqueue to session for processing

```csharp
public class MessageRouter
{
    public Task RouteAsync(InboundMessage msg, CancellationToken ct);
    
    // Key logic:
    // 1. Find channel that owns this chatId
    // 2. Check if group is registered
    // 3. Check trigger word (skip for main group)
    // 4. Format prompt with conversation history
    // 5. Pass to Aether Soul
}
```

### 3.3 Aether Soul (Core Agent)

**This is where Maria lives.**

```csharp
public class AetherSoul
{
    private readonly ILLMProvider _llm;
    private readonly IMemorySystem _memory;
    private readonly IToolExecutor _tools;
    private readonly ISessionManager _sessions;

    public async Task<AgentResponse> ProcessAsync(
        Session session, 
        string prompt, 
        CancellationToken ct
    );
    
    // Core loop:
    // 1. Load session context (CLAUDE.md + history)
    // 2. Call LLM with tools
    // 3. Execute tools if needed (sandboxed)
    // 4. Repeat until final response
    // 5. Save to session, return response
}
```

**LLM Integration Options:**

| Option | Pros | Cons |
|--------|------|------|
| **Semantic Kernel** | Built-in tool calling, memory connectors | Opinionated, some overhead |
| **Microsoft.Extensions.AI** | New, modern, Abstractions layer | Very new, less mature |
| **Direct API (HttpClient)** | Full control, minimal deps | More boilerplate |

**Recommendation:** Start with **Semantic Kernel** for built-in tool calling support, or **Direct API** for maximum control. Microsoft.Extensions.AI is an option once it matures.

### 3.4 Tool Executor (Bash Sandboxing)

**Critical security component.**

```csharp
public interface IToolExecutor
{
    Task<ToolResult> ExecuteAsync(
        ToolCall tool, 
        SandboxConfig sandbox,
        CancellationToken ct
    );
}

public class BashSandbox
{
    // Uses bwrap (Linux) or Windows Sandbox API
    // Resource limits: CPU, memory, time
    // Filesystem: whitelist of allowed paths
    // Network: configurable
    
    public async Task<string> ExecuteBashAsync(
        string command,
        string workingDirectory,
        Dictionary<string, string> allowedPaths,
        int timeoutMs = 30000
    );
}
```

**Security Features:**
- `bwrap` on Linux for syscall filtering
- Windows Job Objects on Windows
- Per-group filesystem restrictions
- No network by default (opt-in)
- CPU/memory limits per process
- Timeout enforcement

**Built-in Tools:**
| Tool | Description |
|------|-------------|
| `bash` | Execute command in sandbox |
| `read` | Read file contents |
| `write` | Write file (within group directory) |
| `edit` | Edit file (patch-based) |
| `glob` | Find files by pattern |
| `grep` | Search file contents |
| `web_search` | Search web (requires network opt-in) |
| `web_fetch` | Fetch URL content (requires network opt-in) |

### 3.5 Memory System

**Dual-layer approach:**

```csharp
public interface IMemorySystem
{
    // Layer 1: Structured files (CLAUDE.md hierarchy)
    Task<string> LoadContextAsync(string groupFolder, bool isMain);
    
    // Layer 2: Vector search (optional)
    Task<List<MemoryEntry>> SearchAsync(string query, int topK = 5);
    Task IndexAsync(string groupFolder, string content);
}
```

**CLAUDE.md Hierarchy (preserved from NanoClaw):**
```
groups/
├── CLAUDE.md          # Global context (read by all)
├── global/
│   └── CLAUDE.md      # Global memory (main can write)
├── main/
│   ├── CLAUDE.md      # Main group's identity/instructions
│   └── ...            # Main group's files
└── {group-name}/
    ├── CLAUDE.md      # Group-specific context
    └── ...            # Group's files
```

**Vector DB (Optional Enhancement):**
- **SQLite-vss** (embedded, simple) — recommended for start
- **Qdrant** (external, more powerful) — for larger deployments
- Index conversation summaries for semantic search

### 3.6 Session Manager

```csharp
public class Session
{
    public string Id { get; }
    public string GroupFolder { get; }
    public List<Message> History { get; }
    public DateTime LastActivity { get; }
    public SessionMetadata Metadata { get; }
}

public class SessionManager
{
    Task<Session> GetOrCreateSessionAsync(string groupFolder);
    Task SaveSessionAsync(Session session);
    Task<List<Message>> GetHistoryAsync(string sessionId, int maxTokens);
    Task CompactSessionAsync(Session session); // When context too long
}
```

**Session Compaction:**
- Trigger when token count exceeds threshold (e.g., 150K tokens)
- Use LLM to generate summary
- Archive full transcript to `conversations/` folder
- Keep recent messages + summary

### 3.7 Task Scheduler

```csharp
public interface IScheduler
{
    Task ScheduleTaskAsync(ScheduledTask task);
    Task<List<ScheduledTask>> GetPendingTasksAsync();
    Task MarkTaskCompleteAsync(string taskId);
}

public class ScheduledTask
{
    public string Id { get; }
    public string GroupFolder { get; }
    public string Prompt { get; }
    public string? Script { get; } // Optional pre-execution script
    public ScheduleType Type { get; } // Cron, Interval, OneTime
    public string ScheduleValue { get; }
    public DateTime? NextRun { get; }
    public TaskStatus Status { get; }
}
```

**Schedule Types:**
- **Cron:** Standard cron expression (e.g., `0 9 * * *` for daily at 9 AM)
- **Interval:** Milliseconds since last run (e.g., `3600000` for hourly)
- **OneTime:** ISO 8601 timestamp

---

## 4. Data Architecture

### 4.1 SQLite Schema

```sql
-- Messages
CREATE TABLE messages (
    id TEXT PRIMARY KEY,
    group_jid TEXT NOT NULL,
    sender TEXT NOT NULL,
    content TEXT NOT NULL,
    timestamp TEXT NOT NULL,
    is_from_me INTEGER,
    is_bot_message INTEGER,
    session_id TEXT
);

-- Sessions
CREATE TABLE sessions (
    id TEXT PRIMARY KEY,
    group_folder TEXT NOT NULL,
    created_at TEXT NOT NULL,
    last_activity TEXT NOT NULL
);

-- Tasks
CREATE TABLE tasks (
    id TEXT PRIMARY KEY,
    group_folder TEXT NOT NULL,
    prompt TEXT NOT NULL,
    script TEXT,
    schedule_type TEXT NOT NULL,
    schedule_value TEXT NOT NULL,
    status TEXT NOT NULL,
    next_run TEXT,
    created_at TEXT NOT NULL
);

-- Groups
CREATE TABLE groups (
    jid TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    folder TEXT NOT NULL,
    is_main INTEGER,
    requires_trigger INTEGER DEFAULT 1,
    trigger TEXT,
    container_config TEXT
);

-- Task History
CREATE TABLE task_runs (
    id TEXT PRIMARY KEY,
    task_id TEXT NOT NULL,
    started_at TEXT NOT NULL,
    completed_at TEXT,
    result TEXT,
    error TEXT
);
```

### 4.2 File System Structure

```
aether/
├── Aether.sln
├── src/
│   └── Aether/
│       ├── Program.cs
│       ├── Aether.csproj
│       ├── Channels/
│       ├── Routing/
│       ├── Agent/
│       │   ├── AetherSoul.cs
│       │   ├── ToolExecutor.cs
│       │   └── LLM/
│       ├── Memory/
│       ├── Sessions/
│       ├── Scheduler/
│       ├── Providers/
│       └── Data/
│           └── Schema.sql
├── Channels/           # Plugin directory
│   ├── Telegram/
│   └── Discord/
├── groups/             # Group data (from NanoClaw)
│   ├── CLAUDE.md
│   ├── global/
│   └── main/
├── store/              # SQLite database
├── data/
│   └── sessions/       # Session transcripts
└── logs/
```

---

## 5. Security Architecture

### 5.1 Isolation Layers

```
┌─────────────────────────────────────────────┐
│  AETHER HOST PROCESS                         │
│  ─────────────────────────────────────────  │
│  • Owns all state (DB, files, config)       │
│  • Spawns sandboxed processes               │
│  • Never executes untrusted code directly   │
└─────────────────────────────────────────────┘
                      │
                      │ spawns with restrictions
                      ▼
┌─────────────────────────────────────────────┐
│  SANDBOXED BASH PROCESS                     │
│  ─────────────────────────────────────────  │
│  • bwrap syscall filter (Linux)             │
│  • Job Objects (Windows)                    │
│  • Filesystem: whitelist only                │
│  • Network: denied by default               │
│  • User: unprivileged                       │
│  • Time limit: 30s default                  │
└─────────────────────────────────────────────┘
```

### 5.2 Mount Security (Per-Group)

```csharp
public class MountSecurity
{
    // Validate additional mounts against allowlist
    // Prevent container escape via symlinks
    // Log all mount attempts
    
    public List<PathMapping> ValidateMounts(
        List<string> requested,
        string groupName,
        bool isMain
    );
}
```

### 5.3 Credential Management

- **No credentials in config files** — use environment variables
- **OneCLI gateway** (optional) for credential injection
- **Per-group .env injection** via sandbox config

---

## 6. Configuration

### 6.1 config.json

```json
{
  "assistant": {
    "name": "Aether",
    "trigger": "@Aether"
  },
  "channels": {
    "telegram": {
      "enabled": true,
      "api_id": "${TELEGRAM_API_ID}",
      "api_hash": "${TELEGRAM_API_HASH}",
      "session": "store/telegram.session"
    }
  },
  "llm": {
    "provider": "openrouter",
    "model": "anthropic/claude-3-5-sonnet",
    "api_key": "${OPENROUTER_API_KEY}",
    "base_url": "https://openrouter.ai/api/v1"
  },
  "sandbox": {
    "type": "bwrap",
    "timeout_ms": 30000,
    "max_memory_mb": 512,
    "allowed_paths": [
      "/workspace/group",
      "/workspace/global"
    ]
  },
  "scheduler": {
    "check_interval_ms": 60000
  }
}
```

### 6.2 Environment Variables

```bash
# Required
OPENROUTER_API_KEY=sk-or-...

# Optional
TELEGRAM_API_ID=12345
TELEGRAM_API_HASH=abc...
ANTHROPIC_API_KEY=sk-ant-...
```

---

## 7. Key Design Decisions

### 7.1 Why No Claude Code CLI?

1. **Dependency reduction** — No Node.js, no Claude CLI
2. **Model agnosticism** — Use any LLM, not just Claude
3. **Simplicity** — One less subprocess to manage
4. **Performance** — Direct API vs subprocess overhead

### 7.2 Why Not Docker Container?

1. **Overhead** — Docker adds ~100MB RAM, ~1s startup
2. **Complexity** — Need Docker daemon, images, networking
3. **Windows friendliness** — Docker Desktop issues on Windows
4. **Sufficient isolation** — OS process + bwrap is enough

### 7.3 Why .NET 9?

1. **Cross-platform** — Linux, Windows, macOS
2. **Performance** — Native AOT compilation
3. **Tooling** — Excellent IDE support, debugging
4. **Ecosystem** — Semantic Kernel, .NET Aspire
5. **Team familiarity** — OpenClaw ecosystem uses .NET

### 7.4 Semantic Kernel vs Direct API

**Recommendation:** Start with **Direct API** using `HttpClient`.

Reasons:
- Minimal dependencies
- Full control over request/response
- Easier debugging
- No opinionated abstractions

Switch to Semantic Kernel if:
- Tool calling becomes complex
- Memory connectors are needed
- Team prefers opinionated framework

---

## 8. Migration Path from NanoClaw

### 8.1 Incremental Migration

```
Phase 1: Core Rewrite
─────────────────────
- Channel Manager (new)
- Message Router (new)
- Basic Agent Loop (new)
- SQLite schema (new, compatible)
- File structure (preserved)

Phase 2: Feature Parity
────────────────────────
- All NanoClaw channels
- Task scheduler
- Session management
- Memory system (CLAUDE.md)

Phase 3: Enhancements
──────────────────────
- Vector DB integration
- Additional tools
- MCP server support
- Plugin ecosystem
```

### 8.2 Backwards Compatibility

- **Groups folder** — Identical structure to NanoClaw
- **SQLite schema** — Similar schema, may need migration
- **Config format** — JSON, similar structure
- **Session format** — JSONL, similar format

---

## 9. Open Questions

| Question | Options | Recommendation |
|----------|---------|----------------|
| **MCP Server support?** | Built-in vs Plugin | Plugin — low priority |
| **Vector DB from start?** | Yes vs No | No — add later if needed |
| **Windows container support?** | Yes vs No | No — Windows process is fine |
| **Multi-user support?** | Yes vs No | No — personal agent focus |
| **Plugin sandboxing?** | Same as Bash vs Separate | Same sandbox is fine |

---

## 10. Next Steps

1. ** Miriam to create Project Plan** based on this architecture
2. ** Erza to start implementation** with Channel Manager + basic Agent Loop
3. ** Maria to design test suite** for validation
4. ** Prototype sandbox** implementation early (critical path)

---

*Document Version: 1.0*  
*Last Updated: 2026-04-19*  
*Author: Maria (Architecture Lead for Aether)*
