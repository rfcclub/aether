# tui-model-picker — Specification

> Capability: Floating model picker panel activated by F2/Ctrl+M, grouped by provider.

---

## ADDED Requirements

### Requirement: Model picker opens and closes via keyboard

The system SHALL enter `Chat::ModelPicker` FSM state when `F2` or `Ctrl+M` is pressed, and return to `Chat::Normal` on `Esc` or after selection.

#### Scenario: F2 opens picker

- **WHEN** user presses `F2` or `Ctrl+M` in `Chat::Normal` mode
- **THEN** the FSM SHALL transition to `Chat::ModelPicker`
- **THEN** a floating overlay panel SHALL appear centered on screen
- **THEN** the first model in the list SHALL be highlighted

#### Scenario: Esc closes picker without selection

- **WHEN** user presses `Esc` in `Chat::ModelPicker` mode
- **THEN** the FSM SHALL transition back to `Chat::Normal`
- **THEN** the overlay SHALL disappear and the active model SHALL be unchanged

---

### Requirement: Model list populated from `list_models` response

The system SHALL send `{"type":"list_models"}` to the Aether backend immediately after WebSocket connection is established and store the response for the picker.

#### Scenario: Models loaded successfully

- **WHEN** the backend responds with `{"type":"models","current":"...","providers":[...]}`
- **THEN** `AppState.models` SHALL be populated with the provider-grouped model list
- **THEN** the current model SHALL be pre-selected (highlighted) when the picker opens

#### Scenario: Models not yet loaded (Phase 3 not deployed)

- **WHEN** the picker is opened but no `models` response has been received
- **THEN** the picker SHALL show `Loading models…` text
- **THEN** no crash or panic SHALL occur

---

### Requirement: Model selection changes the active model

The system SHALL allow navigating the model list with `j`/`k` and selecting with `Enter`.

#### Scenario: User selects a different model

- **WHEN** user navigates to a model using `j`/`k` and presses `Enter`
- **THEN** the TUI SHALL send `{"type":"command","text":"/model <selected_model>","group":"maria"}` over WebSocket
- **THEN** the FSM SHALL transition to `Chat::Normal`
- **THEN** the header bar SHALL update to show the new model name after the server confirms

#### Scenario: Provider group headers are non-selectable

- **WHEN** user navigates to a provider name line (e.g. `[openrouter]`)
- **THEN** `Enter` SHALL skip the header and move to the next model
- **THEN** provider headers SHALL be rendered in a dimmed/italic style

---

### Requirement: Picker layout matches handover design

The system SHALL render the picker as a floating panel with the layout from the handover spec.

#### Scenario: Picker renders correctly

- **WHEN** the picker is open
- **THEN** panel width SHALL be 50% of terminal width (min 40 cols), centered horizontally and vertically
- **THEN** panel height SHALL be `min(number_of_items + 4, 20)` rows
- **THEN** footer hint SHALL read: `j/k Navigate · Enter Select · Esc Close`
- **THEN** current active model SHALL be marked with `← now` suffix
