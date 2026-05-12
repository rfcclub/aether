## ADDED Requirements

### Requirement: Two-level model browsing

The `ModelSelectionHandler` SHALL present a two-level navigation flow: provider list (Screen 1) → model list (Screen 2).

Screen 1 (Provider List) SHALL:
- Display each provider as a tappable item with its emoji, name, and model count
- Mark the provider containing the currently active model with an indicator
- Include a "Reset to Default" button when a model override is active

Screen 2 (Model List) SHALL:
- Display the provider name and total model count in the header text
- List each model as a tappable item
- Mark the currently active model with `Selected = true`
- Include a "Back" button to return to Screen 1

#### Scenario: User opens model selection

- **WHEN** user sends `/model` command
- **THEN** Screen 1 SHALL display with all providers listed as buttons
- **AND** the provider containing the active model SHALL be marked

#### Scenario: User taps a provider

- **WHEN** user taps a provider button on Screen 1
- **THEN** the message SHALL be edited in-place to show Screen 2 with that provider's models
- **AND** the active model SHALL have `Selected = true`

#### Scenario: User taps Back on Screen 2

- **WHEN** user taps "Back" on a model list
- **THEN** the message SHALL be edited in-place to show Screen 1

### Requirement: Model switching with persistence

Tapping a non-current model SHALL immediately switch the agent's model and persist the change.

The switch sequence SHALL:
1. Validate the model resolves to a registered provider via `ProviderRouter.ResolveModelToProvider()`
2. Update `ProviderRouter.ModelChain` with the new model as primary
3. Persist via `ConfigLoader.UpdateAgentModelAsync()`
4. Return updated UiDocument with `Selected` moved to the new model

#### Scenario: Successful model switch

- **WHEN** user taps a non-current model on Screen 2
- **THEN** the agent's model SHALL change to the selected model
- **AND** the change SHALL be persisted to `~/.aether/config.json`
- **AND** the message SHALL be edited to show the new model with `✅`
- **AND** the change SHALL survive restart

#### Scenario: Tapping current model is no-op

- **WHEN** user taps the currently active model (marked `✅`)
- **THEN** the model SHALL NOT change
- **AND** the callback SHALL be acknowledged with no message edit

#### Scenario: Invalid model selection fails gracefully

- **WHEN** user taps a model that no longer exists in the provider registry
- **THEN** the callback SHALL be acknowledged with an error text
- **AND** the agent's current model SHALL remain unchanged

### Requirement: Model list pagination

When a provider has more than 8 models, models SHALL be split into pages of 8.

The pagination row SHALL include:
- `◀️` button (hidden or replaced with `·` on first page)
- `Page N/Total` indicator (non-clickable)
- `▶️` button (hidden or replaced with `·` on last page)

#### Scenario: Pagination for 20 models

- **WHEN** a provider has 20 models
- **THEN** Screen 2 SHALL show page 1 of 3 with 8 models and a pagination row
- **AND** navigating pages SHALL edit the message in-place
- **AND** the `Back` button SHALL remain on every page

#### Scenario: No pagination for 8 or fewer models

- **WHEN** a provider has 8 or fewer models
- **THEN** Screen 2 SHALL show all models without a pagination row

### Requirement: Reset to default model

The "Reset to Default" action SHALL clear any model override and return the agent to its configured default model.

#### Scenario: User resets to default

- **WHEN** user taps "Reset to Default" on Screen 1 while a model override is active
- **THEN** the model override SHALL be cleared from config
- **AND** the agent SHALL use the default model from config hierarchy
- **AND** the message SHALL be edited to show Screen 1 with the default model's provider marked active
