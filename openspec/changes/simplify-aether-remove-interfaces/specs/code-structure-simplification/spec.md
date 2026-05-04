## MODIFIED Requirements

### Requirement: Agent services registered directly without interface wrappers
Services with a single production implementation SHALL be registered directly in DI as their concrete type instead of behind an `I*` interface. Types that vary across multiple implementations (`ILLMProvider`, `IChannel`) retain interfaces.

#### Scenario: DI resolves concrete type directly
- **WHEN** `services.AddSingleton<FileMemory>()` is called and `FileMemory` is resolved
- **THEN** the DI container returns the registered `FileMemory` instance without interface proxy

#### Scenario: Test fakes use virtual method override
- **WHEN** a test needs custom memory behavior
- **THEN** the test can subclass `FileMemory` and override its virtual methods instead of implementing `IMemorySystem`

### Requirement: Boot loading uses static method
Agent boot file loading SHALL use a static `BootLoader.LoadFilesAsync()` method instead of a `BootContract` class instance. `BootConfig` remains as a configuration record.

#### Scenario: Boot files loaded via static method
- **WHEN** agent startup needs to load constitution files
- **THEN** `BootLoader.LoadFilesAsync(agentDir, config.ConstitutionFiles, ct)` returns concatenated file contents

### Requirement: WriteValidator and BootLayer removed
The `WriteValidator` class and `BootLayer` enum SHALL be removed. These were part of the old FEOFALLS write-boundary enforcement not used by current agents.

#### Scenario: WriteValidator no longer in DI
- **WHEN** the application starts
- **THEN** no `WriteValidator` is registered or resolved
