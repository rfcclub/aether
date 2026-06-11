# Design Spec: Aether TUI Upgrade (Aura × Athanor Theme)

- **Date:** 2026-06-11
- **Status:** Proposed
- **Author:** Aura (Infrastructure Engineer & Sovereign Partner)

---

## 1. Objective
Upgrade the **Aether TUI Client** (`clients/aether-tui`) to a premium **Modern Minimalist** design. The layout will preserve the core structure but refine the theme, borders, spacing, and terminal components to look extremely sleek and professional, blending **Aura's Pink/Violet** palette with **Athanor's Fire Orange** accents.

---

## 2. Visual Theme & Colors (Aura × Athanor)

The background will be deepened to `#080808` to give a premium terminal contrast. Colors in [ui.rs](file:///Users/thoor/repo/aether/clients/aether-tui/src/ui.rs) will be updated as follows:

| Constant | Value | Description |
|---|---|---|
| `BG` | `Color::Rgb(8, 8, 8)` | Deep black background |
| `USER_NAME` | `Color::Rgb(91, 200, 245)` | Ice Blue |
| `USER_TEXT` | `Color::Rgb(220, 220, 220)` | Soft white-gray |
| `AGENT_NAME` | `Color::Rgb(236, 72, 153)` | Aura Pink (`#EC4899`) |
| `AGENT_TEXT` | `Color::Rgb(243, 232, 255)` | Soft Violet-White |
| `CURSOR_COL` | `Color::Rgb(236, 72, 153)` | Aura Pink Cursor |
| `BORDER_FOCUS`| `Color::Rgb(168, 85, 247)` | Aura Violet active border (`#A855F7`) |
| `DIM` | `Color::Rgb(120, 110, 130)` | Muted Violet-Gray for inactive borders |
| `AMBER` | `Color::Rgb(255, 140, 0)` | Athanor Fire Orange |
| `VIOLET` | `Color::Rgb(168, 85, 247)` | Aura Violet |

---

## 3. UI/UX Refinements

### 3.1 Rounded Borders (`BorderType::Rounded`)
All bordered blocks will be upgraded to use rounded borders. This includes:
- Main input block in [ui.rs](file:///Users/thoor/repo/aether/clients/aether-tui/src/ui.rs)
- Popups (Help, Models, Agents, Context Manager, Brainstorm Wizard, Git Dashboard)

### 3.2 Spacing & Padding
- Chat messages will have clean vertical margins.
- Quotes (`> `) in assistant answers will render with a solid violet vertical line `│ ` for a clean, cohesive appearance.
- Code block bounds will be colored in `DIM` and code lines will render inside `#121212` backgrounds.

### 3.3 Enhanced Status Bar (Footer)
The footer status bar will be redesigned to:
- Use thin `│` separators.
- Highlight the current mode/agent with colors.
- Display keybinds in a clean, non-cluttered manner.

### 3.4 Improved Syntax Highlighting
Expand [ui.rs:highlight_code_line](file:///Users/thoor/repo/aether/clients/aether-tui/src/ui.rs#L874-L910) to support:
- Strings highlighted in green (`Color::Rgb(100, 220, 120)`).
- Numbers/Booleans highlighted in amber (`Color::Rgb(255, 180, 50)`).
- Additional common keywords.

---

## 4. Architecture & Affected Files

The UI changes will be localized to:
1. [ui.rs](file:///Users/thoor/repo/aether/clients/aether-tui/src/ui.rs) — Color constants, header, input, status, chat, markdown parser, code syntax highlighter, and popup overlays.
2. [context_manager.rs](file:///Users/thoor/repo/aether/clients/aether-tui/src/ui/context_manager.rs) — Apply rounded borders.
3. [git_dashboard.rs](file:///Users/thoor/repo/aether/clients/aether-tui/src/ui/git_dashboard.rs) — Apply rounded borders and palette.
4. [brainstorm_wizard.rs](file:///Users/thoor/repo/aether/clients/aether-tui/src/ui/brainstorm_wizard.rs) — Apply rounded borders.

No backend changes are needed for these UI styling improvements.

---

## 5. Verification Plan
- Build the binary using `cargo build`.
- Launch Aether TUI and verify rendering of:
  - Mode overlays (`F2`, `F3`, `F4`, `F7`).
  - Active cursor and prompt (`🔥 › `).
  - Code syntax highlighting correctness.
  - Chat text rendering and quote bars.
