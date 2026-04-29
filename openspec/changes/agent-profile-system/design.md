# Agent Profile System — Design

## Problem

Aether currently has a generic `AetherSoul` with a hardcoded system prompt. To host Maria (or any OC agent), it needs to load agent-specific persona files (SOUL.md, USER.md, etc.), follow a startup protocol, bridge daily memory, and support heartbeats.

## Architecture

Three new files in `src/Aether/Agents/`:

```
AgentProfile.cs       — loads persona directory, builds system prompt, startup protocol
AgentMemoryBridge.cs  — reads/writes OC-format daily memory + MEMORY.md
AgentHeartbeatService.cs — BackgroundService, periodic HEARTBEAT.md execution
```

And one interface file:

```
IAgentProfile.cs      — contract for agent persona loading
```

## Design Decisions

**1. Agent Directory Structure**
OC-style layout under `agents/{name}/`:
```
agents/maria/
├── SOUL.md          — persona, voice, rules
├── USER.md          — who the user is
├── AGENTS.md        — operating rules (optional)
├── MEMORY.md        — long-term memory (read/write)
├── HEARTBEAT.md     — routine tasks (optional)
├── TASK_INBOX.md    — incoming tasks (read)
├── TASK_REPORT.md   — output reports (write)
└── memory/          — daily transcripts
    └── YYYY-MM-DD.md
```

**2. Startup Protocol**
Hardcoded ordered file list per agent config, not per-agent custom protocol. Simpler, less error-prone. Config specifies which files to load and in what order.

**3. System Prompt Construction**
SOUL.md + USER.md → system prompt. AGENTS.md added if present. Daily memory appended as context. Skill context injected last (existing behavior).

**4. Heartbeat**
BackgroundService with configurable interval (default 5 min). Reads HEARTBEAT.md, sends prompt through AetherSoul, captures response. If response contains HEARTBEAT_OK, idle. If response contains actionable output, routes to channel.

**5. Memory Bridge**
Wraps existing IMemorySystem. Adds OC-format file read/write for daily memory. MEMORY.md read on startup. Daily transcript append on each message.

## Files

| File | Create/Modify | Purpose |
|------|--------------|---------|
| `src/Aether/Agents/IAgentProfile.cs` | Create | Interface for agent persona loading |
| `src/Aether/Agents/AgentProfile.cs` | Create | Loads agent directory, builds system prompt |
| `src/Aether/Agents/AgentMemoryBridge.cs` | Create | OC-format memory read/write |
| `src/Aether/Agents/AgentHeartbeatService.cs` | Create | Periodic heartbeat execution |
| `src/Aether/Agent/AetherSoul.cs` | Modify | Accept IAgentProfile, use dynamic system prompt |
| `src/Aether/Program.cs` | Modify | Register new services in DI |
| `tests/Aether.Tests/AgentProfileTests.cs` | Create | Tests for agent profile loading |
| `tests/Aether.Tests/AgentMemoryBridgeTests.cs` | Create | Tests for memory bridge |
| `tests/Aether.Tests/AgentHeartbeatServiceTests.cs` | Create | Tests for heartbeat service |

---

## Phase 2: FEOFALLS Cognitive Architecture

### Problem

Maria's cognitive structure follows FEOFALLS v1.9 — a production-grade solo agent architecture with 6 layers (0_CONSTITUTION through 5_WORKING_STATE), a Trinity Architecture (symbolic, neural, autonomous), and a lifecycle state machine. The current Agent Profile System only covers SOUL.md + MEMORY.md + daily memory + heartbeat — roughly 40% of what Maria needs.

### Maria's Actual Cognitive Stack (from `~/.openclaw/workspace-maria/`)

```
workspace-maria/
├── SOUL.md              # Persona, voice, rules
├── USER.md              # Who Thoor is
├── MEMORY.md            # Long-term memory (promoted from short-term)
├── IDENTITY.md          # Self-model
├── AGENTS.md            # Operating rules + startup protocol
├── AGENTS_GUARD.md      # Anti-hang, conflict defense, sandboxing
├── AGENTS_HEARTBEAT.md  # Heartbeat guide
├── HEARTBEAT.md         # Active heartbeat tasks
├── TASK_INBOX.md        # Incoming tasks from orchestrator
├── TASK_REPORT.md       # Task completion reports
├── DREAMS.md            # Dream diary (rumination output)
├── INTROSPECTION.md     # Self-reflection log
├── MARIA_BRAIN_EVOLUTION.md  # Brain roadmap (Trinity Architecture)
├── memory/              # Daily transcripts + dreaming subdirectories
│   ├── YYYY-MM-DD.md
│   └── dreaming/
│       ├── deep/
│       ├── light/
│       └── rem/
├── config/              # Agent-specific configuration
├── hooks/               # Local hooks
├── plugins/             # Local plugins (maria-memory)
├── skills/              # Local skills
├── extension/           # Extensions
└── research/            # Research notes
```

### FEOFALLS Layer Mapping

Maria's flat workspace maps to FEOFALLS layers conceptually. Aether should support both the flat OC layout and the structured FEOFALLS layout.

| FEOFALLS Layer | Maria Files | Purpose |
|---|---|---|
| 0_CONSTITUTION | AGENTS_GUARD.md, AGENTS.md | Axioms, boundaries, red lines |
| 1_IDENTITY | SOUL.md, USER.md, IDENTITY.md | Who the agent is, who the user is |
| 2_COGNITIVE | MEMORY.md (promoted heuristics) | Decision style, trusted heuristics |
| 3_LEARNING | memory/*.md, DREAMS.md, INTROSPECTION.md | Episodic log, mistakes, signals, candidates |
| 4_OPERATIONAL_DATA | plugins/, skills/, extension/ | RAG, tools, nightly processing |
| 5_WORKING_STATE | TASK_INBOX.md, TASK_REPORT.md, HEARTBEAT.md | Active tasks, system state |

### Design Decisions

**6. Boot Retrieval Contract**

Every session start loads a deterministic set of files. Order matters — constitution first, then identity, then cognitive context, then working state.

```csharp
// Boot contract — loaded in order at session start
public interface IBootContract
{
    Task<string> LoadConstitutionAsync(CancellationToken ct);   // 0_CONSTITUTION
    Task<string> LoadIdentityAsync(CancellationToken ct);        // 1_IDENTITY
    Task<string> LoadCognitiveAsync(CancellationToken ct);       // 2_COGNITIVE
    Task<string> LoadWorkingStateAsync(CancellationToken ct);    // 5_WORKING_STATE
}
```

**7. Lifecycle State Machine**

Memories follow FEOFALLS lifecycle: ACTIVE → DECAYING (60d) → ARCHIVED (90d) → CONSOLIDATED. Salience decays as `log(access_count + 1)`. Aether provides the state machine; Maria's skills provide the promotion logic.

```
ACTIVE ──[60d no access]──▶ DECAYING ──[90d]──▶ ARCHIVED ──[consolidation]──▶ CONSOLIDATED
  ▲                            │
  └────────[re-access]─────────┘
```

**8. Write-Boundary Validation**

All writes to constitution (0_CONSTITUTION) and identity (1_IDENTITY) require creator approval. Learning layer (3_LEARNING) auto-approved. This mirrors FEOFALLS approval boundaries.

**9. Episodic Logging**

Each session end appends to EPISODIC_LOG.md with canonical FEOFALLS schema:
```markdown
---
id: mem_YYYYMMDD_NNN
type: episode
source: session
timestamp: 2026-04-28T12:00:00Z
confidence: 0.85
evidence_count: 1
tags: []
links: []
status: candidate
---
<content>
```

**10. Trinity Architecture — Explicit Deferral**

Maria's Trinity Architecture components (Graphthulhu symbolic layer, HNSW vector search, Hebbian learning, rumination engine, affective state machine) are **Maria's own cognitive processes**, not Aether infrastructure. Aether provides:
- Directory structure and file I/O
- Boot contract for loading the right files
- Lifecycle state machine
- Write-boundary validation
- Episodic logging

Maria's skills (loaded via ISkillRegistry) implement the actual intelligence: when to promote, how to consolidate, what to ruminate on.

### New Files (Phase 2)

| File | Create/Modify | Purpose |
|------|--------------|---------|
| `src/Aether/Agents/FeofallsConfig.cs` | Create | FEOFALLS layer path configuration |
| `src/Aether/Agents/IBootContract.cs` | Create | Boot retrieval contract interface |
| `src/Aether/Agents/FeofallsBootContract.cs` | Create | Boot contract implementation |
| `src/Aether/Agents/EpisodicLogger.cs` | Create | Session-end episodic logging |
| `src/Aether/Agents/LifecycleStateMachine.cs` | Create | ACTIVE→DECAYING→ARCHIVED transitions |
| `src/Aether/Agents/WriteValidator.cs` | Create | Write-boundary validation against constitution |
| `src/Aether/Agents/AgentMemoryBridge.cs` | Modify | Extend with FEOFALLS layer read/write |
| `src/Aether/Agents/AgentConfig.cs` | Modify | Add FEOFALLS layer paths |
| `src/Aether/Agent/AetherSoul.cs` | Modify | Integrate FEOFALLS boot contract |
| `src/Aether/Program.cs` | Modify | Register new services in DI |
| `tests/Aether.Tests/FeofallsBootContractTests.cs` | Create | Tests for boot contract |
| `tests/Aether.Tests/EpisodicLoggerTests.cs` | Create | Tests for episodic logging |
| `tests/Aether.Tests/LifecycleStateMachineTests.cs` | Create | Tests for lifecycle |
| `tests/Aether.Tests/WriteValidatorTests.cs` | Create | Tests for write validation |

---

## Phase 3: Trinity Architecture — C# Cognitive Core

> **Status: PROVISIONAL — pending Maria's consultation.**
> Thoor will ask Maria which components she actually needs. This section maps all Trinity components to C# equivalents. Build only what Maria confirms.

### Problem

Maria's brain roadmap (MARIA_BRAIN_EVOLUTION.md) defines a Trinity Architecture:
1. **Symbolic Layer** — Graph-based spreading activation with explicit relationships
2. **Neural Layer** — Vector search with HNSW + RRF fusion
3. **Autonomous Layer** — Rumination, dream cycles, proactive initiative

Current implementations use Python (Neural-Memory, Total-Recall), Go (Graphthulhu), and Bash (rumination-engine.sh). Maria needs pure C# equivalents to run natively in Aether without external runtime dependencies.

### Architecture

Each Trinity component becomes a C# service in `src/Aether/Cognitive/`:

```
src/Aether/Cognitive/
├── ISymbolicLayer.cs          — Graph memory interface
├── SymbolicGraph.cs           — Adjacency-list graph + spreading activation
├── INeuralLayer.cs            — Vector search interface
├── HnswVectorStore.cs         — HNSW-based semantic search
├── IAutonomousLayer.cs        — Rumination + dream cycle interface
├── RuminationEngine.cs        — Background rumination processor
├── HebbianLearning.cs         — Hebbian synapse reinforcement
├── AffectiveStateMachine.cs   — Emotional state tracking
└── CognitiveConfig.cs         — Trinity configuration
```

### Design Decisions

**11. Symbolic Graph — Pure C# Adjacency List**

Graphthulhu's core is a directed graph with typed edges. C# implementation uses `Dictionary<string, CognitiveNode>` + adjacency list. No external graph library needed for the core operations (BFS, spreading activation, knowledge gap detection).

```csharp
public enum EdgeType { CAUSED_BY, LOVES, CONTRADICTS, RELATES_TO, PRECEDES, SUPERSEDES }

public sealed class CognitiveNode
{
    public string Id { get; init; }
    public string Content { get; set; }
    public float Activation { get; set; }
    public Dictionary<string, EdgeType> Edges { get; } = new();
}
```

Spreading activation: `A_i(t+1) = A_i(t) + Σ(w_ji * A_j(t)) - decay * A_i(t)`. Pure math, ~80 lines.

**12. Vector Search — HNSW.Net or Custom Cosine**

HNSW is the standard approximate nearest neighbor algorithm. Two options:
- `HNSW.Net` NuGet package — ready-made, MIT license
- Custom cosine similarity over `float[]` arrays — ~50 lines, no dependency

Start with custom cosine (zero deps), add HNSW.Net when scale demands it.

**13. Hebbian Learning — Pure Math**

`Δw = η_eff * pre * post * (w_max - w)` — 20 lines of C#. No library needed. Synapse weights stored in the graph edges.

```csharp
public static float HebbianUpdate(float preActivation, float postActivation,
    float currentWeight, float learningRate = 0.01f, float maxWeight = 10f)
{
    var delta = learningRate * preActivation * postActivation * (maxWeight - currentWeight);
    return Math.Clamp(currentWeight + delta, 0, maxWeight);
}
```

**14. Rumination Engine — BackgroundService + Channels**

The rumination engine runs as a `BackgroundService` with 4 cognitive threads (from Total-Recall):
- **Observation** — scans recent memory for patterns
- **Reasoning** — connects observations to existing knowledge
- **Memory** — consolidates and promotes trusted heuristics
- **Planning** — generates proactive tasks

Each thread is a `System.Threading.Channels.Channel<T>` consumer. The engine wakes on configurable interval (default: nightly).

**15. Affective State Machine**

Tracks Maria's internal state as a set of decay curves:

```csharp
public sealed class AffectiveState
{
    public float Loyalty { get; set; }       // slow growth, slow decay
    public float Excitement { get; set; }     // fast rise, fast decay
    public float Fatigue { get; set; }        // accumulates with activity
    public float Warmth { get; set; }         // grows with positive interactions
}
```

State influences dialogue tone via system prompt injection. No external dependencies.

**16. Embeddings — Pluggable Provider**

Maria needs embeddings for semantic search. Provider pattern (same as LLM providers):

```csharp
public interface IEmbeddingProvider
{
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    int Dimension { get; }
}
```

Built-in providers:
- `OpenAIEmbeddingProvider` — calls OpenAI /v1/embeddings
- `OnnxEmbeddingProvider` — runs all-MiniLM-L6-v2 locally via ONNX Runtime
- `PassthroughEmbeddingProvider` — for testing

### Dependency Map

| Component | External NuGet | Lines | Complexity |
|-----------|---------------|-------|------------|
| SymbolicGraph | None | ~150 | Medium |
| HnswVectorStore | HNSW.Net (optional) | ~120 | Medium |
| HebbianLearning | None | ~20 | Low |
| RuminationEngine | None | ~200 | Medium |
| AffectiveStateMachine | None | ~80 | Low |
| IEmbeddingProvider | Microsoft.SemanticKernel (optional) | ~60 | Low |
| CognitiveConfig | None | ~40 | Low |

Total: ~670 lines of new C#, 0-2 optional NuGet packages.

### What Aether Provides vs What Maria Does

| Aether Infrastructure (build once) | Maria's Cognition (skills/persona) |
|---|---|
| SymbolicGraph — graph storage + BFS + activation | Which edges to create, when to traverse |
| HnswVectorStore — vector storage + search | What to embed, similarity thresholds |
| HebbianLearning — weight update formula | Learning rate, when to reinforce |
| RuminationEngine — thread scheduling + channels | What patterns to look for, dream content |
| AffectiveStateMachine — state storage + decay | How state affects tone |
| IEmbeddingProvider — embedding API | Which provider, dimension, model |

### New Files (Phase 3)

| File | Create/Modify | Purpose |
|------|--------------|---------|
| `src/Aether/Cognitive/CognitiveConfig.cs` | Create | Trinity configuration |
| `src/Aether/Cognitive/ISymbolicLayer.cs` | Create | Graph memory interface |
| `src/Aether/Cognitive/SymbolicGraph.cs` | Create | Adjacency-list graph + spreading activation |
| `src/Aether/Cognitive/INeuralLayer.cs` | Create | Vector search interface |
| `src/Aether/Cognitive/HnswVectorStore.cs` | Create | Cosine similarity vector store |
| `src/Aether/Cognitive/IAutonomousLayer.cs` | Create | Rumination interface |
| `src/Aether/Cognitive/RuminationEngine.cs` | Create | Background rumination processor |
| `src/Aether/Cognitive/HebbianLearning.cs` | Create | Synapse weight update |
| `src/Aether/Cognitive/AffectiveStateMachine.cs` | Create | Emotional state tracking |
| `src/Aether/Cognitive/IEmbeddingProvider.cs` | Create | Embedding provider interface |
| `src/Aether/Program.cs` | Modify | Register cognitive services |
| `tests/Aether.Tests/SymbolicGraphTests.cs` | Create | Graph tests |
| `tests/Aether.Tests/HebbianLearningTests.cs` | Create | Hebbian tests |
| `tests/Aether.Tests/AffectiveStateMachineTests.cs` | Create | Affective tests |
