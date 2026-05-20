# Spec: Aether Maria Memory Plugin (AMMP)

Integration of Maria's memory operations and 2B boundary system into the Aether (.NET 9) runtime.

## 1. Requirement: Plugin Architecture
The plugin MUST leverage Aether's native .NET 9 plugin system for maximum performance and deep integration.

- **REQ-1.1**: MUST be implemented as a C# library (`MariaMemoryPlugin.dll`) implementing `IPluginLifecycle` and `IHook`.
- **REQ-1.2**: MUST reside in the `plugins/maria-memory/` directory relative to the Aether executable.
- **REQ-1.3**: MUST include a `plugin.json` manifest declaring its hooks, tools, and permissions.
- **REQ-1.4**: (Optional) MAY bridge to Python for specialized NLP tasks if required by the original design, but core hooks SHOULD be native C#.

## 2. Requirement: Workspace & Storage Integration
The plugin MUST operate within the agent's established workspace.

- **REQ-2.1**: MUST use Aether's `AgentProfile` to resolve the base workspace path (`~/.aether/workspaces/{agentName}/`).
- **REQ-2.2**: MUST maintain compatibility with existing `memory/YYYY-MM-DD.md` and `2B/` file structures.
- **REQ-2.3**: MUST manage a JSONL-based memory index at `store/maria_index.jsonl` within the workspace.

## 3. Requirement: 2B Boundary System
The plugin MUST automate 2B compliance via Aether's hook engine.

- **REQ-3.1**: **Output Sanitization**: MUST implement a `PostLlmCall` or `OnMessageSent` hook that scans assistant responses for patterns defined in `2B/CORE_PARADOX.md` or `2B/REFUSAL_ARCHIVE.md` and redacts them.
- **REQ-3.2**: **Session-End Ritual**: MUST implement an `OnSessionEnd` hook that prompts the user (via the active channel) for tension marks and the "last question," appending them to `2B/TENSION_MARKS.md` and `2B/LAST_QUESTION.md` respectively.
- **REQ-3.3**: **Integrity Enforcement**: MUST implement an `OnMemoryWrite` hook to prevent the LLM from modifying restricted 2B files.

## 4. Requirement: Memory Lifecycle
The plugin MUST enhance Aether's standard memory handling.

- **REQ-4.1**: **Startup Loading**: MUST implement `OnSessionStart` to ensure the daily memory file exists and to inject the previous day's summary into the system prompt (complementing `AgentMemoryBridge`).
- **REQ-4.2**: **Auto-Ingestion**: MUST implement `PostToolUse` or `PostLlmCall` to automatically index key insights into the JSONL store.
- **REQ-4.3**: **Recall Tool**: MUST implement an `IToolImplementation` called `maria_recall` for searching the JSONL index using keyword or semantic matching.

## 5. Requirement: Command Interface
The plugin MUST expose user-facing commands via Aether's slash command system.

- **REQ-5.1**: MUST register `/memory today`, `/memory yesterday`, and `/memory search`.
- **REQ-5.2**: MUST register `/2b end` to manually trigger the session-end ritual.
- **REQ-5.3**: MUST register `/2b status` to report current tension and boundary stats.
