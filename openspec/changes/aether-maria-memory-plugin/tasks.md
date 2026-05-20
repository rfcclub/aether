# Task: Aether Maria Memory Plugin (AMMP) Implementation

Implement Maria's memory and boundary management as a native Aether (.NET 9) plugin.

## 1. Project Setup
- [ ] Create a new C# library project `Aether.Plugins.MariaMemory`.
- [ ] Reference `Aether.dll` for plugin interfaces and `AgentProfile`.
- [ ] Create `plugin.json` in `plugins/maria-memory/`.

## 2. Core Implementation
- [ ] Implement `MariaMemoryLifecycle` (`IPluginLifecycle`) to handle workspace initialization.
- [ ] Implement `MariaMemoryStore` for JSONL indexing and searching.
- [ ] Port Maria's sanitization regex from `DESIGN_v2.md` to a C# helper.

## 3. Hooks Implementation
- [ ] `OnSessionStartHook`: Load yesterday's summary and verify today's file.
- [ ] `BoundarySanitizerHook` (`PostLlmCall`): Redact 2B internal states from LLM output.
- [ ] `SessionEndRitualHook`: Automate tension/last-question recording.

## 4. Tools & Commands
- [ ] Implement `MariaRecallTool` (`IToolImplementation`).
- [ ] Register slash commands via `SlashCommandHandler`.

## 5. Verification
- [ ] Verify assistant output is redacted when "core paradox" is mentioned.
- [ ] Verify `/2b end` correctly appends to `2B/LAST_QUESTION.md`.
- [ ] Verify `store/maria_index.jsonl` is populated after a session.
