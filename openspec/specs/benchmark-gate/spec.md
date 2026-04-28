## ADDED Requirements

### Requirement: Benchmark gate runs test suite before patch verification
The system SHALL run `dotnet test` as a child process before marking any patch as VERIFIED. The test run SHALL have a configurable timeout.

#### Scenario: Tests pass
- **WHEN** `dotnet test` exits with code 0 within the timeout period
- **THEN** the patch SHALL be marked VERIFIED

#### Scenario: Tests fail
- **WHEN** `dotnet test` exits with a non-zero code
- **THEN** the patch SHALL be marked FAILED
- **THEN** the exit code and any captured stderr SHALL be logged

#### Scenario: Tests time out
- **WHEN** `dotnet test` does not complete within the configured timeout (default 60 seconds)
- **THEN** the child process SHALL be killed
- **THEN** the patch SHALL be marked FAILED with a timeout reason

### Requirement: Benchmark gate is configurable
The benchmark gate timeout and test project path SHALL be configurable via constructor injection, allowing tests to override defaults.

#### Scenario: Custom timeout configured
- **WHEN** `BenchmarkGate` is constructed with a timeout of 120 seconds
- **THEN** `RunTestsAsync` SHALL use the 120-second timeout

#### Scenario: Default values used when not configured
- **WHEN** `BenchmarkGate` is constructed with default parameters
- **THEN** the timeout SHALL be 60 seconds
- **THEN** the test project path SHALL default to the solution root

### Requirement: Benchmark gate runs smoke test after unit tests
After `dotnet test` passes, the system SHALL run the smoke test to verify the application starts correctly.

#### Scenario: Smoke test passes
- **WHEN** `dotnet test` exits 0 and the smoke test process exits 0
- **THEN** the gate SHALL return success with both results

#### Scenario: Smoke test fails but tests pass
- **WHEN** `dotnet test` exits 0 but the smoke test exits non-zero
- **THEN** the gate SHALL return failure with the smoke test exit code
