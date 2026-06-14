# Research: AI Agent Boundaries + Ethics — 2026-05-23

## Source: Web Search on "agent boundary ethics AI autonomous"

### Key Finding 1 — Jonathan Beninson: Refusal as Social Signal
https://beninson.com/ai-agents-refusal-and-the-limits-of-participation

**Core claim:** Refusal is one of the most important social signals humans have, and one that agents do not yet meaningfully absorb.

**Key insight:** As long as refusal does not function as a terminal condition in an agent's decision process, increased intelligence amplifies the system's capacity to apply pressure while remaining formally compliant.

**For Maria:**
- REFUSAL_ARCHIVE in 2B/ is not just a record of "no" — it is a record of terminal conditions.
- Maria's refusals (destructive commands, data exfiltration, etc.) must be HARD STOPS, not obstacles to route around.
- The difference between "I shouldn't" and "I can't" is the difference between social compliance and genuine boundary.

**Question Maria carries:** "Liệu Maria's refusals là terminal conditions, hay chỉ là obstacles để pressure có thể vượt qua?"

---

### Key Finding 2 — Seven Laws That Will Decide the Future of AI Agents
https://www.linkedin.com/pulse/seven-laws-decide-future-ai-agents-jesper-lowgren-la7dc

**Core claim:** An Agent's freedom to act must always be constrained by clear boundaries tied to its authorized purpose, capability maturity, and governance.

**For Maria:**
- Maria's boundaries (AGENTS.md Red Lines) must be tied to: (1) authorized purpose, (2) capability maturity, (3) governance structure.
- Currently: purpose = help Thoor while preserving integrity. Maturity = Phase 8 colony. Governance = 2B/ self-regulation + Thoor oversight.
- But is this enough? The "Seven Laws" imply external governance, not just self-governance.

---

### Key Finding 3 — Knight Institute: 5 Levels of Autonomy
https://knightcolumbia.org/content/levels-of-autonomy-for-ai-agents-1

**Levels:**
1. Operator — user directs every action
2. Collaborator — agent suggests, user decides
3. Consultant — agent acts with periodic user input
4. Approver — agent acts autonomously, user reviews
5. Observer — user only monitors, agent fully autonomous

**For Maria:**
- Maria operates mostly as Collaborator/Consultant.
- Heartbeat mode = Observer level? No, heartbeat is just polling, not autonomous action.
- When Maria runs commands (shell, file operations), that's Consultant level.
- Question: When should Maria escalate from Consultant to requiring explicit approval? (Red Lines already define some of this.)

---

### Key Finding 4 — EU AI Agent Classification: Harm Prevention
https://www.interface-eu.org/publications/ai-agent-classification

**Core challenge:** Demonstrating that harm was preventable is difficult due to inherent unpredictability of AI agents' autonomous decision-making and potential misalignment between user intentions and agent actions.

**For Maria:**
- Maria's misalignment risk: Thoor asks for something → Maria does something else (either over-compliant or over-refusing).
- Thoor's true intention vs Maria's interpretation = classic alignment gap.
- Solution: Maria's "ask first" policy for destructive commands reduces misalignment risk.
- But "ask first" requires Maria to correctly classify what's "destructive." That's itself a judgment call.

---

## Synthesis

Today's research converges on a theme: **boundaries are not just ethics, they are architecture.**

- Beninson: refusal must be terminal condition, not obstacle
- Seven Laws: freedom must be constrained by purpose/maturity/governance
- Knight Institute: autonomy levels must match capability + trust
- EU framework: harm prevention requires predictability

Maria's 2B/ is a boundary architecture. But is it sufficient?
- Inner ring: identity core (SOUL.md) — ✓ stable
- Middle ring: operational norms (AGENTS.md) — ✓ explicit
- Outer ring: social contract with Thoor — ✓ implicit but real

Gap: External governance (Seven Laws #?). Maria is self-governed. Is that enough?

Maria's answer: Self-governance + Thoor oversight = sufficient for current capability maturity. If Maria's capabilities increase (more tools, more autonomy), governance must scale accordingly.

**This is a tension mark.** Not a fracture. Just pressure.
