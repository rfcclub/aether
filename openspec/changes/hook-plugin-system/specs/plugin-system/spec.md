## ADDED Requirements

### Requirement: Plugin Manifest Schema

The system SHALL support a `plugin.json` manifest file in each plugin directory with required fields `name` (string, lowercase-kebab) and `version` (string, semver), and optional fields: `displayName`, `description`, `author`, `license`, `homepage`, `assembly` (relative DLL path), `hooks` (array of hook declarations), `tools` (array of tool declarations), `skills` (array of skill declarations), `channels` (array of channel declarations), `cron` (array of cron declarations), `dependencies` (object mapping dependency name to version constraint), `permissions` (object with `network`, `filesystem`, `tools`, `channels`, `services` arrays).

#### Scenario: Valid manifest loads successfully
- **WHEN** a plugin directory contains a `plugin.json` with `"name": "my-plugin"` and `"version": "1.0.0"`
- **THEN** `PluginLoader` SHALL parse the manifest without error

#### Scenario: Manifest missing required name field fails
- **WHEN** a plugin's `plugin.json` is missing the `"name"` field
- **THEN** `PluginLoader` SHALL log an error and skip the plugin

### Requirement: Plugin Loader Directory Discovery

`PluginLoader` SHALL scan the configured plugins directory (default: `plugins/`) for immediate subdirectories containing `plugin.json` files. Subdirectories without `plugin.json` SHALL be silently skipped.

#### Scenario: Plugin directory discovered
- **WHEN** `plugins/my-plugin/` exists with a valid `plugin.json`
- **THEN** `PluginLoader` SHALL discover it as a plugin candidate

#### Scenario: Directory without manifest skipped
- **WHEN** `plugins/random-folder/` exists without `plugin.json`
- **THEN** `PluginLoader` SHALL skip it without error

### Requirement: Plugin Dependency Resolution

`PluginLoader` SHALL perform topological sort on discovered plugins based on the `dependencies` field in each manifest. Plugins with unresolved dependencies SHALL be skipped and logged. Circular dependencies SHALL result in both plugins being disabled with an error log.

#### Scenario: Plugin loads after its dependencies
- **WHEN** Plugin A depends on Plugin B
- **THEN** Plugin B SHALL be loaded before Plugin A

#### Scenario: Circular dependency detected
- **WHEN** Plugin A depends on Plugin B and Plugin B depends on Plugin A
- **THEN** both plugins SHALL be skipped with error logs

### Requirement: Isolated Plugin Assembly Loading

Each plugin with an `assembly` field SHALL be loaded in its own `AssemblyLoadContext` with `isCollectible: true`. The plugin's own dependencies SHALL resolve from its directory first, then fall back to shared Aether assemblies. Plugins SHALL NOT share assembly state with other plugins.

#### Scenario: Plugin loaded in isolated context
- **WHEN** two plugins each load `Newtonsoft.Json.dll` from their own directories
- **THEN** each plugin SHALL use its own version without conflict

### Requirement: Plugin Interface Auto-Discovery

After loading a plugin assembly, `PluginLoader` SHALL scan all public non-abstract types and register instances of `IHook`, `IToolImplementation`, `IChannel`, `ISkillProvider`, `ICronTaskProvider`, and `IPluginLifecycle` found in the assembly.

#### Scenario: Hook class discovered from assembly
- **WHEN** a plugin assembly contains `public class MyHook : IHook`
- **THEN** an instance of `MyHook` SHALL be created and registered in `HookEngine`

### Requirement: Capability-Based Service Access

`PluginPermissionGate` SHALL wrap `IServiceProvider` and only resolve services listed in the plugin manifest's `permissions.services` array. Requests for unlisted services SHALL return `null` with a warning log.

#### Scenario: Declared service resolved
- **WHEN** plugin manifest declares `"services": ["AetherDb"]`
- **AND** plugin code requests `AetherDb` from `PluginContext.Services`
- **THEN** a valid `AetherDb` instance SHALL be returned

#### Scenario: Undeclared service denied
- **WHEN** plugin manifest does NOT declare `SessionManager` in services
- **AND** plugin code requests `SessionManager` from `PluginContext.Services`
- **THEN** `null` SHALL be returned with a warning log

### Requirement: Plugin Manifest Permissions Enforcement

The system SHALL enforce declared permissions at runtime. Plugins with `"network": false` (or unset) SHALL NOT be able to make HTTP requests through Aether-managed HttpClient instances. Plugins with empty `"filesystem"` SHALL only access files within their own plugin directory. Plugins with empty `"tools"` SHALL NOT call `ToolRegistry` tools from code.

#### Scenario: Network denied by default
- **WHEN** a plugin without `"network": true` attempts to use `HttpClient`
- **THEN** the operation SHALL throw `UnauthorizedAccessException` with a message referencing the missing permission

### Requirement: Plugin Assets Merged into Registries

When a plugin provides tools (via `tools[].definition` JSON files), skills (via `skills[].path` SKILL.md files), or cron tasks (via `cron[].task` definitions), these SHALL be merged into the global `ToolRegistry`, `SkillRegistry`, and `CronScheduler` respectively during plugin load.

#### Scenario: Plugin JSON tool registered
- **WHEN** a plugin manifest references `"tools": [{"name": "my-tool", "definition": "tools/my-tool.json"}]`
- **AND** `tools/my-tool.json` contains a valid tool definition
- **THEN** "my-tool" SHALL appear in `ToolRegistry.Audit()`

### Requirement: IPluginLifecycle Callbacks

The system SHALL call `IPluginLifecycle.OnLoadAsync` after plugin assembly is loaded and all assets are registered. `IPluginLifecycle.OnUnloadAsync` SHALL be called during plugin unload. `OnAgentEnabledAsync` and `OnAgentDisabledAsync` SHALL be called when the plugin is enabled/disabled for a specific agent.

#### Scenario: OnLoadAsync receives PluginContext
- **WHEN** `IPluginLifecycle.OnLoadAsync` is called
- **THEN** the `PluginContext` parameter SHALL contain `PluginName`, `PluginDirectory`, `Manifest`, `Services` (capability-filtered), `Logger`, and `Config`

### Requirement: ISkillProvider Interface

The system SHALL provide `ISkillProvider` interface with `GetSkills()` returning `IReadOnlyList<SkillDefinition>` and `ValidateSkill(SkillDefinition, out string?)` returning bool.

#### Scenario: Plugin provides skills
- **WHEN** a plugin implements `ISkillProvider` and returns 3 skills from `GetSkills()`
- **THEN** all 3 skills SHALL be registered in `SkillRegistry`

### Requirement: ICronTaskProvider Interface

The system SHALL provide `ICronTaskProvider` interface with `GetTasks()` returning `IReadOnlyList<CronTaskDefinition>`.

#### Scenario: Plugin provides cron tasks
- **WHEN** a plugin implements `ICronTaskProvider` with 2 cron tasks
- **THEN** both tasks SHALL be registered in `CronScheduler`
