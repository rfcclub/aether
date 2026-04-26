# Aether — Open Specification

Agent runtime with bounded memory, self-improvement loop, and multi-agent routing.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│  AETHER RUNTIME                                             │
│                                                             │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐   │
│  │  Gateway    │  │  AIAgent    │  │  Memory System      │   │
│  │  (routing)  │◄─┤  (core)     │◄─┤  (3-layer)          │   │
│  │             │  │             │  │                     │   │
│  │ • Telegram  │  │ • Prompt    │  │ • Ephemeral (~4K)   │   │
│  │ • WebSocket │  │   builder   │  │ • Working (FTS5)      │   │
│  │ • Webhook   │  │ • Tool      │  │ • Durable (2.5K)      │   │
│  │             │  │   executor  │  │                     │   │
│  └─────────────┘  └─────────────┘  └─────────────────────┘   │
│         │                │                                    │
│         └────────────────┬───────────────────────────────────┘
│                          │
│                   ┌──────┴──────┐
│                   │  Tooling    │
│                   │  • Registry   │
│                   │  • Executor   │
│                   └─────────────┘
└─────────────────────────────────────────────────────────────┘
```

## Core Components

### 1. Gateway (TODO)
Entry point for all external communication.
- **Responsibilities**: Channel integration, message routing, session identification
- **Channels**: WebSocket (primary), Telegram (optional), Webhook
- **Output**: Normalized message → AIAgent

### 2. AIAgent (TODO)
Core conversation loop.
- **Responsibilities**: Prompt construction, LLM calls, tool dispatch, response streaming
- **State**: Session context, tool results, partial outputs
- **Integration**: MemorySystem (context injection), ToolExecutor (capabilities)

### 3. Memory System (SKELETON IMPLEMENTED)
Three-layer persistence with bounded durable memory.

| Layer | Storage | Limit | Lifecycle |
|-------|---------|-------|-----------|
| Ephemeral | In-memory list | ~4K tokens | Session-only, auto-compacted |
| Working | SQLite + FTS5 | Unbounded | Auto-archived after 30 days |
| Durable | MEMORY.md | 2,500 chars | Human-editable, curated |

**Promotion Pipeline:**
```
Reflection/Observation
        ↓ (confidence ≥ 0.7, evidence ≥ 3)
PromotionCandidate
        ↓ (TryPromoteAsync)
   [Bounds check]
        ↓
   MEMORY.md
        ↓ (at limit)
ForceConsolidationAsync
```

**Files:**
- `IMemorySystem.cs` — interface specification
- `SqliteMemorySystem.cs` — skeleton implementation

**TODO:**
- [ ] SQLite schema creation (sessions, messages_fts virtual table, promotion_candidates)
- [ ] FTS5 BM25 ranking for search
- [ ] Smart compaction (priority-based eviction, not naive truncation)
- [ ] Consolidation algorithm (merge similar entries, evict lowest confidence)
- [ ] Host startup initialization

### 4. Tooling (SKELETON IMPLEMENTED)
Dynamic tool registration and execution.

**Components:**
- `IToolRegistry` — register/unregister/resolve tools
- `IToolExecutor` — execute with error handling
- `ToolDefinition` — name, description, schema, delegate

**TODO:**
- [ ] JSON Schema validation (NJsonSchema or System.Text.Json.Schema)
- [ ] Built-in tools: read, write, edit, exec, search
- [ ] Hot-reload with FileSystemWatcher
- [ ] Permission model (agent-specific tool sets)

### 5. Skill System (NOT STARTED)
Procedural capabilities defined in markdown, not code.

**Format (SKILL.md):**
```markdown
---
name: github-code-review
description: Review pull requests for common issues
when_to_use: When user asks to review code, check PR, or audit changes
tools: [read, search, exec]
auto_apply: false  # require confirmation
---

# GitHub Code Review

## Steps
1. Read the PR diff
2. Check for: security issues, bugs, style violations
3. Comment on each issue found
4. Summarize overall quality

## Examples

### Good review
Input: "Review this PR: https://github.com/..."
Output: "Found 2 issues: SQL injection on line 42, unused import on line 15"
```

**Components:**
- `ISkillRegistry` — load, validate, list skills
- `SkillContext` — inject skill into prompt when triggered
- `SkillTrigger` — auto (description match) or explicit (`/<skill-name>`)

**TODO:**
- [ ] SKILL.md parser (frontmatter + body)
- [ ] Skill loader from directory
- [ ] Trigger detection (description similarity or explicit command)
- [ ] Skill context injection into prompt builder
- [ ] Skill evolution hooks (for self-improvement loop)

---

### 6. LLM Provider System (SKELETON IMPLEMENTED)
Multi-provider abstraction with intelligent fallback.

**Design Goals:**
- No vendor lock-in
- Cost optimization (unlimited where possible)
- Quality fallback for complex tasks
- Transparent routing decisions

**Provider Tiers:**

| Tier | Provider | Cost | Quality | Use Case |
|------|----------|------|---------|----------|
| **Primary** | Fireworks (kimi-k2.5) | Unlimited | Medium | Daily work, coding, simple queries |
| **Fallback** | OpenRouter/Kimi 2.6 | Pay-per-use | High | Complex reasoning, architecture |
| **Resilience** | Same provider, different key/server | Failover | Same | Primary key rate-limited or down |
| **Local** | Ollama/vLLM | Free (GPU) | Variable | Offline, privacy-sensitive |

**Key-based Fallback:**
- Primary: `FIREWORKS_KEY_1` → server 1
- Resilience: `FIREWORKS_KEY_2` → server 2 (same provider, different endpoint)
- Only escalate to different provider (OpenRouter) if complexity demands

**Fallback Triggers:**
```csharp
// Auto-escalation conditions
if (task.ComplexityScore > 0.8) -> Fallback
if (primaryResponse.Confidence < 0.6) -> Retry with Fallback
if (task.Contains("safety|security|production")) -> Safety tier
if (primaryResponse.Refusal) -> Fallback
```

**Configuration:**
```json
{
  "llm": {
    "providers": [
      { "name": "fireworks", "model": "kimi-k2.5", "priority": 1, "unlimited": true },
      { "name": "openrouter", "model": "kimi-k2.6", "priority": 2 },
      { "name": "anthropic", "model": "claude-3-5-sonnet", "priority": 3, "safety_only": true }
    ],
    "fallback_rules": {
      "complexity_threshold": 0.8,
      "confidence_threshold": 0.6,
      "max_retries": 2
    }
  }
}
```

**Files:**
- `ILLMProvider` — unified interface (chat, stream, tools)
- `ProviderRouter` — routing logic, fallback decisions
- `FireworksProvider` — unlimited tier implementation
- `OpenRouterProvider` — fallback tier (exists, needs refactor)
- `AnthropicProvider` — safety tier (stub)

**TODO:**
- [ ] Complexity scoring heuristic
- [ ] Response confidence estimation
- [ ] Cost tracking per provider
- [ ] Circuit breaker for failing providers
- [ ] Streaming aggregation across fallback

---

### 7. Self-Improvement Workflow (NOT STARTED)
Recursive improvement across 7 layers — inspired by Superada's rebuild after Hermes gap analysis.

**Core Principle:**
> "A system can be powerful and still lose the comparison if the recursive loop is hidden, fragmented, or hard to inspect."

**7 Improvement Layers:**

| Layer | What | Example |
|-------|------|---------|
| Soul | Behavior, prompts, persona | Response tone, default behaviors |
| Skills | Capability definitions | SKILL.md improvements |
| Crons | Scheduled learning | Daily reflection, weekly consolidation |
| Memory/Rules | Durable operating memory | Promoted lessons, hard guardrails |
| Workflows | Cross-cutting orchestration | How reflection becomes candidate |
| Scripts/Tooling | Enforcement automation | Recidivism trackers, validators |
| Process/Routing | Operational behavior | What gets delegated, what needs proof |

**6-Phase Pipeline:**

```
┌─────────────────────────────────────────────────────────────┐
│  PHASE 1: DAILY SOUL REVIEW                                 │
│  - Introspection ritual: friction, corrections, failures     │
│  - Output: reflections.md, promotion candidates            │
├─────────────────────────────────────────────────────────────┤
│  PHASE 2: RECURSIVE BACKBONE                                │
│  - capture → classify → promote → apply → verify            │
│  - Canonical pipeline for any improvement signal            │
├─────────────────────────────────────────────────────────────┤
│  PHASE 3: SKILL EVOLUTION                                   │
│  - SKILL.md patches from recurring issues                   │
│  - Human-gated or auto-apply (configurable)               │
├─────────────────────────────────────────────────────────────┤
│  PHASE 4: VISIBILITY LAYER                                  │
│  - Candidate funnel states: PROPOSED, APPLIED, VERIFIED     │
│  - Recidivism trends, escalated cases, bottlenecks          │
├─────────────────────────────────────────────────────────────┤
│  PHASE 5: RECIDIVISM ENFORCEMENT                            │
│  - Same failure → force promotion to active memory          │
│  - Still fails → escalate to structural candidate         │
│  - Teeth: soft reminder → hard guardrail → real patch       │
├─────────────────────────────────────────────────────────────┤
│  PHASE 6: CROSS-LAYER OPTIMIZATION                          │
│  - Workflow improvements (not just skill/soul)              │
│  - Cron tuning, process changes, routing adjustments        │
└─────────────────────────────────────────────────────────────┘
```

**Design Decisions:**
- **No auto-commit** — all changes via PR, human review required
- **Benchmark gating** — TBLite/YC-Bench style regression checks
- **Legibility first** — every phase inspectable, metrics visible
- **.NET implementation** — ML.NET for optimization, or Python RPC bridge

**Files:**
- `IImprovementWorkflow.cs` — workflow interface
- `DailyReviewCron.cs` — Phase 1 implementation
- `PromotionPipeline.cs` — Phase 2 backbone
- `RecidivismTracker.cs` — Phase 5 enforcement

## Configuration

```yaml
# appsettings.json
{
  "memory": {
    "db_path": "store/memory.db",
    "file_path": "store/MEMORY.md",
    "ephemeral_token_limit": 4000,
    "durable_char_limit": 2500,
    "min_confidence": 0.7,
    "min_evidence": 3
  },
  "llm": {
    "provider": "openrouter",
    "model": "anthropic/claude-3-5-sonnet",
    "api_key": "${OPENROUTER_API_KEY}"
  },
  "tools": {
    "allowlist": ["read", "write", "edit", "exec", "search"],
    "require_approval": ["exec"]
  }
}
```

## Multi-Agent Support (NOT STARTED)

Two models under consideration:

**A. Gateway Routing (OpenClaw-style)**
- Single gateway, multiple named agents
- Agent selected by channel/config
- Shared infrastructure, isolated contexts

**B. Profile Isolation (Hermes-style)**
- Each agent = separate process
- Full isolation: memory, skills, config
- Heavier but true multi-tenancy

## Implementation Status

| Component | Status | Notes |
|-----------|--------|-------|
| Tool Registry | ✅ Skeleton | Basic interfaces, DI wired |
| Tool Executor | ✅ Skeleton | Error handling, async |
| Memory System | ✅ Skeleton | 3-layer design, bounded durable |
| Skill System | ❌ Not started | SKILL.md parser, loader, triggers |
| Gateway | ❌ Not started | WebSocket primary |
| AIAgent Core | ❌ Not started | Prompt builder, LLM calls |
| LLM Provider | ✅ Skeleton | Multi-provider with fallback chain |
| Self-Improvement | ❌ Not started | 7-layer workflow, 6-phase pipeline |
| Multi-Agent | ❌ Not started | Decision pending |

## Dependencies

```xml
<!-- Aether.csproj additions needed -->
<PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.0" />
<PackageReference Include="System.Text.Json" Version="9.0.0" />
<!-- Optional for schema validation -->
<PackageReference Include="NJsonSchema" Version="11.0.0" />
```

## Design Principles

1. **Bounded over unbounded** — Force prioritization via hard limits
2. **Explicit over implicit** — All memory changes auditable
3. **Human-in-the-loop** — No auto-commit, PR review required
4. **Fail fast** — Skeletons throw NotImplementedException, not silent bugs
5. **Steal wisely** — Learn from Hermes/OC, implement with .NET strengths

---

*Spec version: 0.1.0*
*Last updated: 2026-04-26*
