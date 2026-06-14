## ADDED Requirements

### Requirement: Built-in tools registered at startup

The following tools SHALL have real implementations registered at startup: `read`, `write`, `edit`, `bash`, `glob`, `grep`. Each tool SHALL execute actual filesystem or shell operations, not return passive stub responses.

#### Scenario: read tool reads file
- **WHEN** agent calls `read` with `{"path": "/workspace/notes.txt"}`
- **THEN** system SHALL read the file at the resolved path and return its content as string

#### Scenario: write tool writes file
- **WHEN** agent calls `write` with `{"path": "/workspace/output.txt", "content": "hello"}`
- **THEN** system SHALL write the content to the resolved path atomically

#### Scenario: edit tool replaces text in file
- **WHEN** agent calls `edit` with `{"path": "/workspace/file.txt", "old": "foo", "new": "bar"}`
- **THEN** system SHALL replace first occurrence of "foo" with "bar" in the file

#### Scenario: bash tool executes command
- **WHEN** agent calls `bash` with `{"command": "ls -la"}`
- **THEN** system SHALL execute the command via `/bin/bash -c`, capture stdout/stderr, and return result with exit code

#### Scenario: glob tool finds files
- **WHEN** agent calls `glob` with `{"pattern": "*.cs"}`
- **THEN** system SHALL return list of matching file paths relative to workspace root

#### Scenario: grep tool searches files
- **WHEN** agent calls `grep` with `{"pattern": "TODO", "path": "."}`
- **THEN** system SHALL search files recursively and return matching lines with file:line info

### Requirement: Built-in tools respect sandbox

All file and shell tools SHALL validate operations against the sandbox configuration before executing.

#### Scenario: Read outside allowed paths denied
- **WHEN** agent calls `read` with path outside sandbox allowed paths
- **THEN** system SHALL return error "Path not permitted" without reading

#### Scenario: Empty allowed paths allows workspace only
- **WHEN** sandbox config has empty allowed paths list
- **THEN** file tools SHALL only allow access within workspace directory
