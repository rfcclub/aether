## 1. Skill System Interfaces and Data Models

- [x] 1.1 Create `Skills/SkillInterfaces.cs` with SkillDefinition, SkillContext, ISkillRegistry, ISkillLoader, ISkillTrigger, SkillTriggerMode
- [x] 1.2 Define PromotionCandidate record in SkillInterfaces for recidivism output

## 2. SKILL.md Parser

- [x] 2.1 Create `Skills/SkillParser.cs` with ISkillLoader implementation
- [x] 2.2 Implement frontmatter regex (--- ... ---)
- [x] 2.3 Implement ParseSkillFile: extract name, description, when_to_use, tools[], auto_apply, body
- [x] 2.4 Implement LoadFromDirectoryAsync: scan *.md files, skip malformed with warning
- [x] 2.5 Handle cancellation token in LoadFromDirectoryAsync

## 3. Skill Registry

- [x] 3.1 Create `Skills/SkillRegistry.cs` with ISkillRegistry implementation
- [x] 3.2 Implement Register/Unregister/Resolve/List/HasSkill
- [x] 3.3 Implement FindMatching with keyword overlap (0.3 threshold)
- [x] 3.4 Reject empty/whitespace skill names with ArgumentException

## 4. Skill Trigger

- [x] 4.1 Create `Skills/SkillTrigger.cs` with ISkillTrigger implementation
- [x] 4.2 Implement DetectExplicit: parse /<skill-name> pattern
- [x] 4.3 Implement DetectAuto: keyword overlap with 0.35 threshold
- [x] 4.4 Implement DetectTrigger: explicit priority over auto
- [x] 4.5 Decouple trigger from registry (accept IReadOnlyList<SkillDefinition>)

## 5. Skill Evolution

- [x] 5.1 Create `Skills/SkillEvolution.cs` with ISkillEvolution implementation
- [x] 5.2 Implement RecordUsageAsync with asymmetric delta (+0.1 helpful, -0.15 unhelpful)
- [x] 5.3 Cap records at 100 per skill (FIFO eviction)
- [x] 5.4 Implement GetRecidivismCandidatesAsync: 3+ unhelpful in last 10 + negative avg delta
- [x] 5.5 Implement GetRecordsAsync: reverse chronological with limit

## 6. AetherSoul Integration

- [x] 6.1 Update AetherSoul constructor to accept ISkillRegistry and ISkillTrigger
- [x] 6.2 Add skill detection before message building in ProcessAsync
- [x] 6.3 Extend BuildSystemPrompt to inject skill context (name, description, body)
- [x] 6.4 Handle auto_apply flag in skill context display

## 7. DI Registration

- [x] 7.1 Register ISkillRegistry, ISkillLoader, ISkillTrigger, ISkillEvolution in Program.cs host builder
- [x] 7.2 Update AetherSoul factory to resolve skill dependencies
- [x] 7.3 Update prompt harness to instantiate skill services

## 8. Verification

- [ ] 8.1 Verify build succeeds with `dotnet build`
- [ ] 8.2 Test explicit trigger: send "/github-code-review test"
- [ ] 8.3 Test auto-trigger: send message matching skill description keywords
- [ ] 8.4 Test recidivism detection via GetRecidivismCandidatesAsync