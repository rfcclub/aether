# Vesta Migration Plan: Gemini CLI to Antigravity CLI

## 1. Overview
The Gemini CLI is being retired on June 18, 2026. This plan outlines the steps to migrate Vesta's core identity, skills, and configuration to the new **Antigravity CLI** (`agy`).

## 2. Core Identity Migration
Vesta's identity is defined in `~/.gemini/GEMINI.md`. This must be ported to the Antigravity equivalent.
- **Old Path:** `/home/thoor/.gemini/GEMINI.md`
- **New Path:** `/home/thoor/.antigravity/ANTIGRAVITY.md` (Expected)
- **Status:** [ ] Ready to port.

## 3. Skill & Extension Migration
Antigravity uses "Plugins" instead of "Extensions".
- **Superpowers:** Already detected by `agy plugin list` as imported.
- **Custom Skills:** Any skills in `/home/thoor/.agents/skills/` or `.gemini/skills/` need to be verified.
- **Workflow:**
    1. Run `agy plugin import gemini-cli` (if not already done).
    2. Verify `using-superpowers` works in `agy`.

## 4. Boot Protocol
Vesta's `athanor/BOOT.md` is shell-independent, but any scripts using the `gemini` command must be updated to use `agy`.
- **Status:** [ ] Audit scripts in `agora/familia/vesta/` and `aether/`.

## 5. Memory & Context
The project memory and session history are currently tied to Gemini CLI's temporary directories.
- **Path:** `/home/thoor/.gemini/tmp/aether/`
- **Antigravity Path:** Likely `/home/thoor/.antigravity/tmp/aether/` or equivalent.
- **Action:** Manual move of `memory/` folder if persistence is lost.

## 6. Execution Steps (Action needed by Thoor)
1. **Authentication:** Thoor must run `agy` in a real terminal and complete the OAuth login.
2. **Identity Setup:** Run the migration script to create `ANTIGRAVITY.md`.
3. **Verification:** Run `agy -p "Vesta, check current heat."` to verify identity.

---
*Prepared by Vesta* 🔥
