## Why

Aether needs procedural capabilities defined declaratively in markdown, not hardcoded. Skills let users add/remove capabilities without code changes. The existing OpenSpec design already specifies the requirements — this change implements the skill system to match that spec.

## What Changes

- New `Aether.Skills` namespace with interfaces and implementations
- SKILL.md parser: YAML frontmatter + markdown body extraction
- Skill loader: scan directory, validate on load, error handling for malformed files
- Trigger detection: explicit (`/<skill-name>`) and auto (keyword overlap, threshold 0.35)
- Skill context injection into AetherSoul system prompt
- Skill evolution tracking: usage records, recidivism detection, PromotionCandidate output for self-improvement pipeline
- DI registration in Program.cs

## Capabilities

### New Capabilities
- `skill-registry`: ISkillRegistry + SkillRegistry implementation — register, unregister, resolve, list, keyword-match auto-detect
- `skill-parser`: ISkillLoader + SkillParser implementation — parse SKILL.md frontmatter + body, load from directory
- `skill-trigger`: ISkillTrigger + SkillTrigger implementation — explicit slash command and auto keyword overlap detection
- `skill-evolution`: ISkillEvolution + SkillEvolution implementation — usage recording, recidivism threshold (3+ unhelpful in 10), PromotionCandidate generation

### Modified Capabilities
- (none — skill system is net-new)

## Impact

- **Code**: New files in `src/Aether/Skills/`
- **Integration**: AetherSoul constructor updated to accept ISkillRegistry + ISkillTrigger; BuildSystemPrompt extended with skill injection
- **DI**: SkillRegistry, SkillParser, SkillTrigger, SkillEvolution registered in Program.cs
- **Design doc**: Matches existing spec at `openspec/changes/aether-full-project/specs/skill-system/spec.md`