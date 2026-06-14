# LoomKit — Claude Cowork Integration (Future)

⚠️ **Not yet implemented.** This adapter is reserved for future Claude Cowork (desktop app) support.

## Planned Integration

Claude Cowork is a multi-agent desktop environment. LoomKit would integrate via:

1. Shared context files that all agents read
2. Workflow state tracked in `loomkit/` directory
3. Each agent assigned a phase (brainstorm/spec/design/plan/tdd/verify/archive)

## Status

- [ ] Investigate Cowork API/plugin system
- [ ] Design multi-agent coordination protocol
- [ ] Implement shared context adapter
- [ ] Test with real Cowork session

---

*Until then, use the Claude Code adapter for single-agent flow, or the Codex adapter for Codex users.*
