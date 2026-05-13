## ADDED Requirements

### Requirement: Per-Agent Plugin Configuration Schema

The system SHALL support a `plugins` section in `.aether.json` with fields: `enabled` (array of plugin name strings), `disabled` (array of plugin name strings), `config` (dictionary of plugin name → arbitrary JSON object), and `hookOverrides` (dictionary of "pluginName/HookClassName" → object with optional `priority` int).

#### Scenario: Agent enables specific plugins
- **WHEN** `.aether.json` contains `"plugins": { "enabled": ["guard-rails", "persona-injector"] }`
- **THEN** only "guard-rails" and "persona-injector" plugins SHALL be active for that agent
- **AND** all other installed plugins SHALL be inactive

#### Scenario: No plugins section means all plugins disabled
- **WHEN** `.aether.json` has no `plugins` section
- **THEN** no plugins SHALL be active for that agent (secure by default)

### Requirement: Per-Plugin Configuration Override

The `config` field in the `plugins` section SHALL allow arbitrary JSON objects keyed by plugin name. These objects SHALL be passed to the plugin via `PluginContext.Config` at load time and via `IPluginLifecycle.OnAgentEnabledAsync`.

#### Scenario: Plugin receives custom config
- **WHEN** `.aether.json` has `"plugins": { "config": { "guard-rails": { "blockedCommands": ["rm -rf"] } } }`
- **THEN** `PluginContext.Config` for guard-rails SHALL contain `{ "blockedCommands": ["rm -rf"] }`

### Requirement: Hook Priority Override Per-Agent

The `hookOverrides` field SHALL allow overriding a specific hook's priority per agent. The key format SHALL be `"pluginName/ClassName"` and the value SHALL be an object with a `priority` int field.

#### Scenario: Agent overrides hook priority
- **WHEN** `.aether.json` has `"plugins": { "hookOverrides": { "guard-rails/BashBlocker": { "priority": 1 } } }`
- **THEN** the `BashBlocker` hook from `guard-rails` plugin SHALL execute at priority 1 regardless of its default

### Requirement: Plugin CLI Install Command

The system SHALL provide `aether plugin install <path>` command that copies a plugin directory to `plugins/<name>/`, validates its `plugin.json`, and reports success or failure.

#### Scenario: Plugin installed from path
- **WHEN** user runs `aether plugin install ./my-plugin/`
- **THEN** the directory SHALL be copied to `plugins/my-plugin/`
- **AND** `plugin.json` SHALL be validated
- **AND** a success message SHALL be displayed with the plugin name and version

### Requirement: Plugin CLI List Command

The system SHALL provide `aether plugin list` command that displays all installed plugins with name, version, status (ACTIVE/DISABLED/ERROR), and summary of provided components (hooks count, tools count, skills count, channels count).

#### Scenario: List installed plugins
- **WHEN** user runs `aether plugin list`
- **THEN** a table SHALL display with columns: name, version, status, and component summary

### Requirement: Plugin CLI Show Command

The system SHALL provide `aether plugin show <name>` command that displays the full plugin manifest, declared permissions, registered hooks with their priorities, provided tools/skills/channels, and dependency graph.

#### Scenario: Show plugin details
- **WHEN** user runs `aether plugin show guard-rails`
- **THEN** the output SHALL include: manifest fields, hook names with priorities, tool names, skill names, permissions block

### Requirement: Plugin CLI Enable/Disable Per-Agent

The system SHALL provide `aether plugin enable <name> --agent <agent>` and `aether plugin disable <name> --agent <agent>` commands that update the agent's `.aether.json` to add or remove the plugin from the `enabled` list.

#### Scenario: Enable plugin for specific agent
- **WHEN** user runs `aether plugin enable guard-rails --agent maria`
- **THEN** "guard-rails" SHALL be added to `maria`'s `.aether.json` plugins.enabled array

### Requirement: Plugin CLI Uninstall Command

The system SHALL provide `aether plugin uninstall <name> [--force]` command that removes the plugin directory from `plugins/`. By default, it SHALL prompt for confirmation unless `--force` is used.

#### Scenario: Uninstall removes plugin directory
- **WHEN** user runs `aether plugin uninstall guard-rails --force`
- **THEN** `plugins/guard-rails/` directory SHALL be deleted
- **AND** the plugin SHALL no longer appear in `aether plugin list`
