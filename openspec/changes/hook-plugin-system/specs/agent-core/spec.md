## ADDED Requirements

### Requirement: HookEngine Registered in DI Container

`Program.cs` SHALL register `HookEngine` as a singleton service in the DI container. `HookEngine` SHALL resolve all `IHook` implementations from DI (built-in hooks) and from `PluginLoader` (plugin-discovered hooks), sorted by priority.

#### Scenario: HookEngine resolves with mixed hooks
- **WHEN** a built-in hook is registered in DI with priority 5
- **AND** a plugin hook is discovered with priority 10
- **THEN** `HookEngine` SHALL contain both hooks, ordered by priority

### Requirement: PluginLoader Registered in DI Container

`Program.cs` SHALL register `PluginLoader` as a singleton service. On construction, `PluginLoader` SHALL scan `plugins/`, load manifests, resolve dependencies, and discover all extension implementations from plugin assemblies.

#### Scenario: PluginLoader runs at host startup
- **WHEN** the host starts and plugins are present in `plugins/`
- **THEN** `PluginLoader` SHALL discover and load all valid plugins before `ChannelMessageProcessor` begins processing messages

### Requirement: PluginPermissionGate Registered in DI Container

`Program.cs` SHALL register `PluginPermissionGate` as a singleton service. It SHALL wrap `IServiceProvider` and enforce capability-based service access for plugins.

#### Scenario: PluginPermissionGate accessible to plugins
- **WHEN** a plugin's `IPluginLifecycle.OnLoadAsync` is called
- **THEN** `PluginContext.Services` SHALL be the `PluginPermissionGate` filtered provider

### Requirement: OnAgentStart Hook Fired After Boot

`Program.cs` SHALL fire `HookPoint.OnAgentStart` after the DI container is built, boot files are integrity-checked, and all services are initialized, but before `host.RunAsync()` begins message processing.

#### Scenario: Agent start hook fires once
- **WHEN** the host completes boot sequence
- **THEN** `OnAgentStart` SHALL fire exactly once
- **AND** `OnAgentStartContext.IsFirstBoot` SHALL be true on the first run ever, false on subsequent runs

### Requirement: OnAgentStop Hook Fired on Shutdown

`Program.cs` SHALL fire `HookPoint.OnAgentStop` when the host receives a shutdown signal (Ctrl+C or SIGTERM), before disposing services.

#### Scenario: Agent stop hook fires on shutdown
- **WHEN** the host begins graceful shutdown
- **THEN** `OnAgentStop` SHALL fire and all subscribed hooks SHALL execute
