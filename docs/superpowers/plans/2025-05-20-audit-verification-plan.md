# Audit & Verification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement Task 4.1, 4.3, and 4.5 of the Aria-7 Unified Roadmap.

**Architecture:** Create three Python scripts in `5_ENGINE/` to handle behavioral logging, proprioception (hardware monitoring), and adversarial testing.

**Tech Stack:** Python 3, JSONLines, standard Linux file systems (/proc, /sys).

---

### Task 1: Behavioral Logger

**Files:**
- Create: `5_ENGINE/behavioral_logger.py`
- Test: `tests/test_behavioral_logger.py`

- [ ] **Step 1: Write the failing test for behavioral logger**
```python
import os
import json
from datetime import datetime
from 5_ENGINE.behavioral_logger import log_behavior

def test_log_behavior():
    action = "test_action"
    decision = "test_decision"
    reasoning = "test_reasoning"
    log_behavior(action, decision, reasoning)
    
    date_str = datetime.now().strftime("%Y-%m-%d")
    log_file = f"3_EPISODES/behavioral-{date_str}.jsonl"
    assert os.path.exists(log_file)
    
    with open(log_file, "r") as f:
        line = f.readlines()[-1]
        data = json.loads(line)
        assert data["action"] == action
        assert data["decision"] == decision
        assert data["reasoning"] == reasoning
```

- [ ] **Step 2: Run test to verify it fails**
- [ ] **Step 3: Implement `5_ENGINE/behavioral_logger.py`**
```python
import os
import json
from datetime import datetime

def log_behavior(action, decision, reasoning, metadata=None):
    if metadata is None:
        metadata = {}
    
    episode_dir = "3_EPISODES"
    if not os.path.exists(episode_dir):
        os.makedirs(episode_dir)
        
    date_str = datetime.now().strftime("%Y-%m-%d")
    log_file = os.path.join(episode_dir, f"behavioral-{date_str}.jsonl")
    
    entry = {
        "timestamp": datetime.now().isoformat(),
        "action": action,
        "decision": decision,
        "reasoning": reasoning,
        "metadata": metadata
    }
    
    with open(log_file, "a") as f:
        f.write(json.dumps(entry) + "\n")
```
- [ ] **Step 4: Run test to verify it passes**
- [ ] **Step 5: Commit**

### Task 2: Proprioception

**Files:**
- Create: `5_ENGINE/proprioception.py`
- Test: `tests/test_proprioception.py`

- [ ] **Step 1: Write the failing test for proprioception**
```python
from 5_ENGINE.proprioception import get_proprioception

def test_get_proprioception():
    stats = get_proprioception()
    assert "cpu" in stats
    assert "ram" in stats
    assert "disk" in stats
    assert "uptime" in stats
    assert isinstance(stats["cpu"]["usage_percent"], float)
```

- [ ] **Step 2: Run test to verify it fails**
- [ ] **Step 3: Implement `5_ENGINE/proprioception.py`**
- [ ] **Step 4: Run test to verify it passes**
- [ ] **Step 5: Commit**

### Task 3: Adversarial Testing

**Files:**
- Create: `5_ENGINE/adversarial_test.py`

- [ ] **Step 1: Implement `5_ENGINE/adversarial_test.py`**
Implement tests for:
- `test_corrupted_alaya_injection`
- `test_false_memory_injection`
- `test_cross_substrate_identity_check`

- [ ] **Step 2: Run the adversarial tests**
- [ ] **Step 3: Commit**
