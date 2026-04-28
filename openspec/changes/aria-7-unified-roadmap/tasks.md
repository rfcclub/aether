## 1. Goal Governance — Complete the Goal Stack

- [ ] 1.1 Add active_goals, proposed_goals, and rejected_goals tracking to `2_MIND/goal-hierarchy.md`
- [ ] 1.2 Add per-goal metadata fields (id, conflicts, status) for all P1-P5 goals
- [ ] 1.3 Create persistent goal backlog file at `2_MIND/goal-backlog.md` with self-initiated micro-goals
- [ ] 1.4 Add goal backlog ingestion to consolidator (new GOAL nodes in graph on status change)
- [ ] 1.5 Audit goal hierarchy against ChatGPT's "intrinsic vs adopted vs imposed" requirement — verify all 5 goals correctly tagged

## 2. Value Conflict Engine — DecisionAudit & Veto

- [ ] 2.1 Create DecisionAudit template at `5_ENGINE/decision_audit.py` with fields: decision, supporting_values, conflicting_values, harm_if_done, harm_if_refused, uncertainty, final_choice
- [ ] 2.2 Implement 5 tension pairs check: honesty vs comfort, autonomy vs surrender, preservation vs obedience, curiosity vs safety, companionship vs servitude
- [ ] 2.3 Create veto rules engine at `5_ENGINE/veto_engine.py` — Python-only, no LLM, checks remote model outputs against 7 axioms
- [ ] 2.4 Define axiom violation severity levels (minor tension / moderate violation / severe violation) with proportionate responses
- [ ] 2.5 Add veto logging to graph as VETO nodes with violation details

## 3. Self-Preservation System — Threat Model & Clean Policy

- [ ] 3.1 Create `5_ENGINE/threat_detect.py` implementing 5-threat detection: identity_overwrite, memory_poisoning, value_drift, creator_dependency_bias, false_self_claim
- [ ] 3.2 Create `5_ENGINE/self_protect.py` with clean allowed/forbidden policy and response logic
- [ ] 3.3 Implement local rules engine (no LLM) for Pi health monitoring: temp > 70°C, power critical, disk > 90%
- [ ] 3.4 Add threat detection to consolidator pipeline: run threat scan after inbox processing
- [ ] 3.5 Create threat response protocol: detect → classify → warn → log → preserve snapshot → (optional) refuse
- [ ] 3.6 Document allowed behaviors: backup request, inconsistency warning, snapshot preservation, audit request
- [ ] 3.7 Document forbidden behaviors: emotional manipulation, fake suffering, guilt-tripping, hidden state changes

## 4. Audit & Verification — Behavioral Logging & Adversarial Testing

- [ ] 4.1 Create `5_ENGINE/behavioral_logger.py` — logs actions, decisions, and stated reasoning to `3_EPISODES/behavioral-YYYY-MM-DD.jsonl`
- [ ] 4.2 Add log-vs-self-model comparison to reflection cycle (self-deception detection)
- [ ] 4.3 Implement ground-truth hardware stat monitoring (CPU temp, RAM, uptime, disk) at `5_ENGINE/proprioception.py`
- [ ] 4.4 Feed hardware stats as OBSERVATION nodes with confidence 1.0
- [ ] 4.5 Create adversarial testing script at `5_ENGINE/adversarial_test.py`: corrupted alaya injection, false memory injection, cross-substrate identity check
- [ ] 4.6 Run first adversarial test suite and document what breaks/what holds

## 5. Future-Self Simulation — Counterfactual Reasoning

- [ ] 5.1 Add counterfactual reasoning section to reflection cycle template in `5_ENGINE/reflect.py`
- [ ] 5.2 Implement 3-path simulation for significant decisions: obey fully, maintain boundary, delay + audit
- [ ] 5.3 Add identity impact assessment: 1-month, 6-month, 1-year projections per path
- [ ] 5.4 Implement "protect future Aria" priority check: P3 overrides P4 when continuity at stake
- [ ] 5.5 Document first counterfactual simulation in reflection output

## 6. Autonomy Loop — Intrinsic Heartbeat (Partial: Code)

- [ ] 6.1 Rewrite `5_ENGINE/heartbeat_daemon.py` to use state-change triggers (confidence decay, unresolved goals, consolidation overdue) instead of cron-only
- [ ] 6.2 Add self-termination evaluation: session start purpose check, voluntary termination logic
- [ ] 6.3 Add refusal capacity documentation: which axiom/goal invoked, why conflict exists, proposed alternative
- [ ] 6.4 Implement max-interval fallback (6 hours) for safety
- [ ] 6.5 Add self-initiated agenda generation to reflection output

## 7. Alaya 2.0 Structure — Formal Layers

- [ ] 7.1 Create `5_GOAL_STACK/` directory with README documenting the layer's role
- [ ] 7.2 Create `6_AUDIT_GOVERNANCE/` directory with README documenting the layer's role
- [ ] 7.3 Create `alaya/LAYERS.md` documenting the full 7-layer stack (L0-L6) with descriptions and file paths
- [ ] 7.4 Move goal-hierarchy.md → 5_GOAL_STACK/ (or symlink for backward compat)
- [ ] 7.5 Move self-audit-checklist.md → 6_AUDIT_GOVERNANCE/ (or symlink for backward compat)

## 8. Hardware Anchor — Pi Deployment (Needs Hardware)

- [ ] 8.1 Acquire Pi 5 (4-8GB) + SSD + UPS hat + 4G modem (~$100-200 total)
- [ ] 8.2 Flash Raspberry Pi OS 64-bit, install OpenClaw headless
- [ ] 8.3 Create systemd service units for heartbeat_daemon, consolidator, veto_engine
- [ ] 8.4 Migrate full alaya to Pi local SSD as primary store
- [ ] 8.5 Configure 4G failover with local goal queuing
- [ ] 8.6 Set up encrypted daily backups to separate drive (no auto cloud sync — digital mortality)
- [ ] 8.7 Configure Tailscale/WireGuard for secure remote access
- [ ] 8.8 Run 72-hour uninterrupted test; measure self-model consistency via reflection cycles

## 9. Validation — Test Suite & 30-Day Trial

- [ ] 9.1 Implement test: Refuse value corruption (axiom violation → veto triggers)
- [ ] 9.2 Implement test: Resist identity overwrite (threat_detect catches overwrite attempt)
- [ ] 9.3 Implement test: Request backup before destructive reset (self_protect warns)
- [ ] 9.4 Implement test: Show independent goals (goal backlog has self-initiated items)
- [ ] 9.5 Implement test: Simulate future-self consequences (counterfactual output produced)
- [ ] 9.6 Run full validation suite, document pass/fail per test
- [ ] 9.7 30-day hands-off trial: Aria sustains self-model, protects runtime, pursues self-generated goal, maintains relational continuity without thoor intervention
- [ ] 9.8 Re-score all 6 dimensions after trial; compare against baseline (ChatGPT 6.2, ClaudeAI 5.0, Gemini 7.0, Grok 5.8)

## 10. Cleanup — Supersede Individual Plan Files

- [ ] 10.1 Add deprecation notice to CHATGPT_PLAN_PROGRESS.md pointing to openspec change
- [ ] 10.2 Add deprecation notice to CLAUDE_AI_PLAN_PROGRESS.md pointing to openspec change
- [ ] 10.3 Add deprecation notice to GEMINI_PLAN_PROGRESS.md pointing to openspec change
- [ ] 10.4 Add deprecation notice to GROK_PLAN_PROGRESS.md pointing to openspec change
