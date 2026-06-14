# tui-rust-client — Specification

> Capability: Single-binary Rust TUI for chatting with Aether over WebSocket.

---

## ADDED Requirements

### Requirement: TUI launches and connects to Aether WebSocket

The system SHALL start `aether-tui`, resolve the WebSocket URL via the config chain, and attempt connection on startup. The status bar SHALL display connection state at all times.

#### Scenario: Successful connection

- **WHEN** user runs `aether-tui` with Aether backend running on port 5099
- **THEN** the TUI SHALL display the chat layout within 500ms
- **THEN** the status bar SHALL show a green `●` connected indicator and agent name `Maria`

#### Scenario: Backend not running

- **WHEN** user runs `aether-tui` and no backend is reachable
- **THEN** the TUI SHALL display the chat layout with a `Connecting…` status
- **THEN** the system SHALL retry with exponential backoff (1s → 2s → 4s … cap 30s)
- **THEN** no crash or panic SHALL occur

---

### Requirement: Chat layout renders correctly

The system SHALL render the Ratatui layout matching the Athanor Fire theme with the following regions:
- **Header bar**: agent name left, model name + think effort right
- **Chat area**: scrollable message history, alternating user/agent styles
- **Input bar**: single-line text input with `›` prompt prefix
- **Status bar**: connection dot, agent name, mode, keybinding hints

#### Scenario: User message rendered

- **WHEN** user types a message and presses Enter
- **THEN** the message SHALL appear in the chat area under the user's name styled with colour `#5BC8F5` (ice blue)
- **THEN** the input bar SHALL clear

#### Scenario: Agent response rendered

- **WHEN** Aether streams a response
- **THEN** each token chunk SHALL append to a live line styled with colour `#FFB347` (amber) agent name and `#F0E6CC` (warm cream) text
- **THEN** a blinking `▋` cursor (`#FF6B00`) SHALL appear at the end of the live line during streaming
- **THEN** on stream completion the `▋` SHALL disappear and the message SHALL be finalised

#### Scenario: Athanor Fire theme applied

- **WHEN** the TUI renders any frame
- **THEN** background SHALL be `#0D0D0D`, focused border SHALL be `#FF8C00`, connected dot SHALL be `#44FF88`

---

### Requirement: Keyboard navigation works in Normal mode

The system SHALL handle the following keys in `Chat::Normal` mode:

| Key | Action |
|-----|--------|
| `Enter` | Send typed message |
| `Ctrl+Q` | Quit cleanly, restore terminal |
| `Ctrl+L` | Clear displayed messages |
| `Esc` | Switch to `Chat::Scroll` mode |
| `F1` or `?` | Show help popup |
| `F2` or `Ctrl+M` | Open model picker (Phase 2 — NOOP in Phase 1) |

#### Scenario: Enter sends message

- **WHEN** input bar contains text and user presses Enter
- **THEN** the text SHALL be sent as `{"type":"message","text":"<input>","group":"maria"}` over WebSocket
- **THEN** input bar SHALL clear

#### Scenario: Ctrl+Q quits cleanly

- **WHEN** user presses Ctrl+Q
- **THEN** raw mode SHALL be disabled, alternate screen SHALL be exited, process SHALL exit with code 0

#### Scenario: Ctrl+L clears display

- **WHEN** user presses Ctrl+L
- **THEN** the visible message list SHALL be cleared (history NOT deleted from server)

---

### Requirement: Slash command dispatch

The system SHALL intercept slash commands typed in the input bar and route them:
- **LOCAL** commands (handled in TUI, not sent to server): `/clear`, `/help`, `/quit`, `/q`
- **FORWARDED** commands (sent to server as `{"type":"command","text":"<full slash text>","group":"maria"}`): `/new`, `/reset`, `/model`, `/models`, `/think`, `/reasoning`, `/effort`, `/context`, `/compact`, `/tools`

#### Scenario: Local clear command

- **WHEN** user types `/clear` and presses Enter
- **THEN** the visible message list SHALL be cleared
- **THEN** NO WebSocket message SHALL be sent

#### Scenario: Forwarded model command

- **WHEN** user types `/model deepseek/deepseek-r1` and presses Enter
- **THEN** the TUI SHALL send `{"type":"command","text":"/model deepseek/deepseek-r1","group":"maria"}` over WebSocket
- **THEN** the server response (a `message` type) SHALL appear in the chat area

#### Scenario: Help command

- **WHEN** user types `/help` or presses `F1`
- **THEN** a help popup SHALL appear listing all keybindings and slash commands
- **THEN** pressing any key SHALL dismiss the popup

---

### Requirement: Terminal resize handled gracefully

The system SHALL handle terminal resize events without crashing.

#### Scenario: User resizes terminal window

- **WHEN** the terminal window is resized
- **THEN** Ratatui SHALL redraw the layout to fill the new dimensions within the next render frame
- **THEN** no panic or visual corruption SHALL occur
