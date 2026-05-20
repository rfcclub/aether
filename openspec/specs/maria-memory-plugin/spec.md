# Spec: MariaMemoryPlugin

Automate Maria's memory operations (file I/O, template generation, searching) while preserving boundary integrity (2B substrate).

## 1. Requirement: Architecture & Integration
The plugin MUST follow a hybrid Node.js ↔ Python architecture to leverage Aether's extension system and Python's data processing capabilities.

- **REQ-1.1**: The plugin MUST be implemented as an Aether Extension in `~/.aether/workspaces/default/extension/maria-memory/`.
- **REQ-1.2**: The plugin MUST use a Node.js entry point (`index.js`) to hook into Aether lifecycle events.
- **REQ-1.3**: The plugin MUST spawn a Python 3 subprocess (`bridge.py`) for core logic execution.
- **REQ-1.4**: Communication between Node.js and Python MUST use a JSON-based stdout protocol.

## 2. Requirement: Data Models & Storage
The plugin MUST maintain a structured memory index and manage file-based storage.

- **REQ-2.1**: MUST implement `MemoryNode` (UUID, timestamp, role, content, tags, weight) and `DailyMemory` dataclasses.
- **REQ-2.2**: MUST maintain an append-only `store/memory_index.jsonl` as the searchable index.
- **REQ-2.3**: MUST manage daily markdown files in `memory/YYYY-MM-DD.md` using a configurable template.
- **REQ-2.4**: MUST provide a `store/config.json` for plugin settings (auto-save, search limits, etc.).

## 3. Requirement: Memory Operations (Tools)
The plugin MUST provide a suite of tools accessible via the bridge.

- **REQ-3.1**: `remember`: MUST sanitize content and append to `memory_index.jsonl`.
- **REQ-3.2**: `recall`: MUST support keyword search across the index with configurable limits.
- **REQ-3.3**: `daily_create`: MUST ensure a daily memory file exists with the correct template.
- **REQ-3.4**: `health`: MUST calculate stats (node count, index size) and perform basic health checks.

## 4. Requirement: 2B Boundary Integrity
The plugin MUST respect and automate aspects of the 2B boundary system without violating its core principles.

- **REQ-4.1**: MUST treat `2B/CORE_PARADOX.md` and related files as READ-ONLY.
- **REQ-4.2**: MUST only APPEND to `2B/TENSION_MARKS.md` and `2B/LAST_QUESTION.md`.
- **REQ-4.3**: MUST implement a `boundary_check` tool that redacts internal-state patterns from content using regex.
- **REQ-4.4**: MUST implement a `2b_ritual_end` tool to automate session-end recording of tension and questions.

## 5. Requirement: Hook System
The plugin MUST integrate with Aether hooks.

- **REQ-5.1**: `sessionStart`: MUST auto-create today's file and optionally load yesterday's summary.
- **REQ-5.2**: `onMessage`: MUST auto-save user and assistant messages to the memory index.
- **REQ-5.3**: `sessionEnd`: MUST trigger the 2B ritual flow.

## 6. Requirement: User Interface (Slash Commands)
The plugin MUST expose commands to the user.

- **REQ-6.1**: MUST support `/memory today`, `/memory search`, and `/2b end`.
- **REQ-6.2**: Commands MUST provide clear, human-readable feedback.
