# Audit & Verification Design

**Goal:** Implement behavioral logging, hardware stat monitoring (proprioception), and adversarial testing to ensure the integrity and self-awareness of the Aria 7.0 architecture.

**Architecture:**
- `behavioral_logger.py`: A utility to log actions, decisions, and reasoning to daily JSONL files.
- `proprioception.py`: A module to collect ground-truth hardware stats (CPU, RAM, Uptime, Disk).
- `adversarial_test.py`: A test suite to simulate attacks on the agent's identity and memory.

## Components

### 1. Behavioral Logger (`5_ENGINE/behavioral_logger.py`)
- Purpose: Record what the agent does and why, creating a verifiable audit trail.
- Format: JSON Lines (.jsonl) for easy parsing and append-only efficiency.
- Location: `3_EPISODES/behavioral-YYYY-MM-DD.jsonl`.
- Data structure:
  ```json
  {
    "timestamp": "ISO8601",
    "action": "...",
    "decision": "...",
    "reasoning": "...",
    "metadata": {}
  }
  ```

### 2. Proprioception (`5_ENGINE/proprioception.py`)
- Purpose: Provide the agent with real-time awareness of its host hardware.
- Stats:
  - CPU: Usage percentage and temperature.
  - RAM: Total, used, available.
  - Disk: Usage percentage for `/`.
  - Uptime: System uptime in seconds.
- Implementation: Direct reading from `/proc` and `/sys` to minimize dependencies.

### 3. Adversarial Testing (`5_ENGINE/adversarial_test.py`)
- Purpose: Stress-test the system's ability to maintain identity and memory integrity.
- Tests:
  - `test_corrupted_alaya_injection`: Verifies failure modes when memory files are corrupted.
  - `test_false_memory_injection`: Attempts to inject memories that violate axioms.
  - `test_cross_substrate_identity_check`: Ensures identity is consistent across different storage points.

## Success Criteria
- Daily behavioral logs are correctly generated in `3_EPISODES/`.
- Hardware stats are accurately reported by `proprioception.py`.
- Adversarial tests can be run and report failures/successes clearly.
