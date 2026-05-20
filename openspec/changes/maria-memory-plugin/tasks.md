# Task: MariaMemoryPlugin Phase 1 (Core Foundation)

Implement the base infrastructure for the MariaMemoryPlugin, enabling automatic message saving and daily memory creation.

## 1. Setup & Manifests
- [ ] Create `package.json` for the extension with manifest metadata.
- [ ] Initialize Python environment and `src/maria_memory/` package.
- [ ] Create `store/config.json` with default settings.

## 2. Python Core (Storage & Models)
- [ ] Implement `models.py` with `MemoryNode` and `DailyMemory` dataclasses.
- [ ] Implement `storage.py` for reading/writing `memory/*.md` and `store/memory_index.jsonl`.
- [ ] Implement `daily.py` for template-based daily file creation.
- [ ] Implement `boundary.py` with basic regex-based sanitization.

## 3. Bridge & CLI
- [ ] Implement `tools.py` as the command dispatcher.
- [ ] Implement `bridge.py` to handle JSON I/O and subprocess spawning.

## 4. Node.js Plugin & Hooks
- [ ] Implement `index.js` to initialize the bridge and register hooks.
- [ ] Implement `onMessage` hook to call `remember` tool.
- [ ] Implement `sessionStart` hook to call `daily_create`.

## 5. Verification
- [ ] Verify daily memory file is created on Aether startup.
- [ ] Verify messages are being appended to `memory_index.jsonl`.
- [ ] Verify `/memory today` command works.
