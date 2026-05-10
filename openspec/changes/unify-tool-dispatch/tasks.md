## 1. Registry-backed tool exposure

- [x] 1.1 Add a descriptor/list API to `ToolRegistry` that returns enabled tool definitions with name, description, schema, and risk metadata
- [x] 1.2 Update `AetherSoul` constructor to accept registry-backed tool catalog/executor dependencies
- [x] 1.3 Replace hardcoded `BuiltInTools` request exposure with registry descriptors
- [x] 1.4 Preserve hardcoded tools only as a temporary fallback for harness tests, if needed
- [x] 1.5 Test: `AetherSoul` provider request includes `web_search` and `web_fetch`

## 2. Unified dispatch

- [x] 2.1 Route AetherSoul tool calls through `Aether.Tooling.ToolExecutor`
- [x] 2.2 Normalize JSON argument conversion from provider `Dictionary<string,string>` into `JsonElement`
- [x] 2.3 Normalize registry tool results into provider tool-result message strings
- [x] 2.4 Keep schema validation before execution for all registry-backed tools
- [x] 2.5 Test: a `web_fetch` tool call executes through the unified path
- [x] 2.6 Test: unknown tool returns `Tool '<name>' not found` via `ToolExecutor`
- [x] 2.7 Test: invalid JSON args returns model-readable error via `ToolExecutor`
- [x] 2.8 Test: missing required args on a registered tool returns model-readable error

## 3. Compatibility aliases

- [x] 3.1 Register `shell` alias for `bash`
- [x] 3.2 Register `exec` alias for `bash`, disabled unless policy permits command execution aliases
- [x] 3.3 Test: `shell` executes same implementation as `bash` and produces identical output
- [x] 3.4 Test: `exec` is omitted or denied by default when policy disables it

## 4. OpenClaw migration baseline tools

- [x] 4.1 Implement `skill_list`
- [x] 4.2 Implement `skill_read`
- [x] 4.3 Implement `memory_read`
- [x] 4.4 Implement `memory_write` with append-first daily memory behavior
- [x] 4.5 Implement `memory_search` using file/grep-backed search first
- [x] 4.6 Implement `session_status`
- [x] 4.7 Implement `session_reset`
- [x] 4.8 Test: `skill_read` rejects path traversal (`..`, `/`, `\`)
- [x] 4.9 Test: `memory_read` rejects path outside memory/ directory
- [x] 4.10 Test: `memory_write` rejects writes when `AllowWrites` is false
- [x] 4.11 Test: `memory_write` rejects path outside memory/ directory
- [x] 4.12 Test: `memory_search` skips files where `IsPathAllowed` returns false

## 5. Runtime audit

- [x] 5.1 Add a `ToolAudit` service or helper that reports visible, disabled, and missing OpenClaw-parity tools
- [x] 5.2 Add `/tools` slash command or CLI surface for the audit
- [x] 5.3 Test: audit output includes visible count and disabled policy reasons

## 6. Verification

- [x] 6.1 Run targeted tool tests
- [x] 6.2 Run AetherSoul tests
- [x] 6.3 Run full test suite
- [ ] 6.4 Manual smoke: prompt Maria to list visible tools and use `web_fetch`, `skill_read`, and `memory_write`
