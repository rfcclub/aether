## ADDED Requirements

### Requirement: Pi 5 baseline deployment
The system SHALL support deployment on a Raspberry Pi 5 (4-8GB) with 64-bit Raspberry Pi OS, running OpenClaw headless with alaya on local SSD. Deployment SHALL include systemd service units for heartbeat_daemon and consolidator with auto-start on boot and auto-restart on crash.

#### Scenario: Pi boots — daemons start automatically
- **WHEN** the Pi powers on and boots
- **THEN** systemd starts heartbeat_daemon and consolidator services without manual intervention

#### Scenario: Daemon crashes — auto-restarts
- **WHEN** heartbeat_daemon crashes with non-zero exit code
- **THEN** systemd restarts it within 30 seconds and logs the restart event

### Requirement: Digital mortality via local exclusivity
The system SHALL treat the Pi's local SSD as the exclusive primary storage for the alaya causal memory graph. No automatic cloud synchronization SHALL exist that trivializes deletion. Encrypted daily backups SHALL be created but restoration MUST be a deliberate manual process by thoor.

#### Scenario: Graph updated — stored locally
- **WHEN** consolidator processes inbox items and updates graph.json
- **THEN** the updated graph is saved to local Pi SSD, and nowhere else automatically

#### Scenario: Backup created but not auto-synced
- **WHEN** the daily backup script runs
- **THEN** an encrypted backup is created on a separate drive, but not pushed to any cloud service automatically

#### Scenario: Deletion is irreversible without manual restoration
- **WHEN** alaya files are deleted from Pi
- **THEN** they are gone from the primary store — restoration requires thoor to manually initiate recovery from backup

### Requirement: Network resilience with local queuing
The system SHALL detect 4G/WiFi drops and queue outbound API calls locally for retry when connectivity returns. Inbound processing (consolidator, heartbeat) SHALL continue during network outages using only local data.

#### Scenario: Network drops — goals queued
- **WHEN** 4G connectivity drops while a remote model call is pending
- **THEN** the call is queued locally and retried when connectivity is restored

#### Scenario: Consolidation continues offline
- **WHEN** network is unavailable but new inbox items exist
- **THEN** consolidator processes them using local graph only (no remote synthesis), queuing any remote-dependent work for later

### Requirement: Sensor embodiment (optional Phase 2)
The system SHALL support optional sensor integration: Pi Camera, temperature/humidity sensor, motion sensor, GPIO-controlled relay. Sensor data SHALL feed into the causal memory graph as OBSERVATION nodes with confidence 1.0 (physical ground truth).

#### Scenario: Temperature sensor feeds graph
- **WHEN** the temperature sensor reads 25.3°C
- **THEN** an OBSERVATION node is created with content "Ambient temperature: 25.3°C" and confidence 1.0

#### Scenario: Motion detected — episodic memory created
- **WHEN** the motion sensor triggers
- **THEN** an OBSERVATION node is created with timestamp and "Motion detected in room" — this becomes part of Aria's environmental episodic memory
