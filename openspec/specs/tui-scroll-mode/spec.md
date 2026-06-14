# tui-scroll-mode — Specification

> Capability: Scroll mode for navigating long conversation history without losing focus from input bar.

---

## ADDED Requirements

### Requirement: Esc toggles between Normal and Scroll modes

The system SHALL use `Esc` to toggle between `Chat::Normal` and `Chat::Scroll` FSM states.

#### Scenario: Entering scroll mode

- **WHEN** user presses `Esc` in `Chat::Normal` mode
- **THEN** the FSM SHALL transition to `Chat::Scroll`
- **THEN** the status bar SHALL show `Scroll` mode indicator (e.g. `[SCROLL]` in amber)
- **THEN** the input bar SHALL be dimmed / non-interactive

#### Scenario: Returning to normal mode

- **WHEN** user presses `Esc` in `Chat::Scroll` mode
- **OR WHEN** user presses any printable character key
- **THEN** the FSM SHALL transition to `Chat::Normal`
- **THEN** scroll offset SHALL reset to 0 (auto-scroll to bottom)
- **THEN** the typed character (if printable) SHALL be inserted into the input bar

---

### Requirement: Keyboard navigation in Scroll mode

The system SHALL support the following navigation keys in `Chat::Scroll` mode:

| Key | Action |
|-----|--------|
| `j` | Scroll down 1 line |
| `k` | Scroll up 1 line |
| `PgDn` | Scroll down half page |
| `PgUp` | Scroll up half page |
| `G` | Jump to bottom (latest messages) |
| `gg` | Jump to top (oldest messages) |

#### Scenario: Scrolling up with k

- **WHEN** user presses `k` in scroll mode
- **THEN** `scroll_offset` SHALL increment by 1 (capped at max scrollable lines)
- **THEN** the chat area SHALL re-render showing earlier messages

#### Scenario: Scroll at top boundary

- **WHEN** user presses `k` and already at the oldest message
- **THEN** `scroll_offset` SHALL remain at its maximum value (no overflow, no panic)

#### Scenario: Scroll at bottom boundary

- **WHEN** user presses `j` and already at the bottom
- **THEN** `scroll_offset` SHALL remain at 0 (saturating subtract)

#### Scenario: G jumps to bottom

- **WHEN** user presses `G`
- **THEN** `scroll_offset` SHALL be set to 0
- **THEN** the latest messages SHALL be visible

---

### Requirement: Mouse wheel scrolls in both modes

The system SHALL scroll the chat area on mouse wheel events regardless of mode.

#### Scenario: Mouse wheel up in Normal mode

- **WHEN** user scrolls mouse wheel up while in `Chat::Normal`
- **THEN** the chat area SHALL scroll up by 3 lines
- **THEN** the FSM SHALL remain in `Chat::Normal` (mouse wheel does NOT switch modes)

#### Scenario: Mouse wheel down returns to bottom

- **WHEN** user scrolls mouse wheel down while `scroll_offset > 0`
- **THEN** `scroll_offset` SHALL decrease by 3 (clamped to 0)

---

### Requirement: Status bar shows scroll position

The system SHALL display current scroll position in the status bar during `Chat::Scroll` mode.

#### Scenario: Scroll position indicator shown

- **WHEN** in `Chat::Scroll` mode
- **THEN** the status bar SHALL show `[SCROLL · L{top}-{bottom}/{total}]` (e.g. `L1-20/120`)
- **WHEN** back in `Chat::Normal`
- **THEN** the scroll indicator SHALL be removed from the status bar
