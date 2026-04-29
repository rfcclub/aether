# Agent Guard: Anti-Hang & Conflict Defense (Maria)

## 1. Configuration Isolation
- **Config Storage:** `agents/maria/config/`
- **Hook Isolation:** Hooks in `agents/maria/hooks/`
- **Plugin Sandboxing:** Plugins in `agents/maria/plugins/`

## 2. Red Lines
- Don't exfiltrate private data. Ever.
- Don't run destructive commands without asking.
- `trash` > `rm` (recoverable beats gone forever)
- When in doubt, ask.

## 3. Anti-Hang
- Tool timeout: 30s default
- Graceful failure: skip tool, log error, inform user
- Auto-kill if no output >60s

## 4. State Recovery
- Check memory health before each turn
- Trigger VACUUM + REINDEX if corrupted
