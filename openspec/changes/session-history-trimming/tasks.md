## 1. Token Estimator

- [ ] 1.1 Add `Agent/ITokenEstimator.cs` with `int Estimate(string text)` method
- [ ] 1.2 Add `Agent/CharTokenEstimator.cs` implementing `ITokenEstimator` using `text.Length / 4 + 1`
- [ ] 1.3 Register `CharTokenEstimator` as `ITokenEstimator` singleton in DI

## 2. History Trimmer

- [ ] 2.1 Add `Agent/HistoryTrimmer.cs` with static `Trim(IReadOnlyList<LlmMessage> messages, int systemPromptTokens, int budget, ITokenEstimator estimator)` returning `IReadOnlyList<LlmMessage>`
- [ ] 2.2 Implement: sum token estimates from newest to oldest; drop oldest until total ≤ effective budget
- [ ] 2.3 Ensure at least one message is always returned when input is non-empty

## 3. AetherSoul Integration

- [ ] 3.1 Add `AgentOptions` record with `HistoryTokenBudget` (default 80000), bind to `appsettings.json` `agent` section
- [ ] 3.2 Add `appsettings.json` `agent` section with `history_token_budget: 80000`
- [ ] 3.3 Inject `ITokenEstimator` into `AetherSoul`
- [ ] 3.4 After loading history, estimate system prompt tokens and call `HistoryTrimmer.Trim` before building `LlmRequest`

## 4. Tests

- [ ] 4.1 Smoke test: `CharTokenEstimator.Estimate` returns expected value for known string
- [ ] 4.2 Smoke test: `HistoryTrimmer.Trim` returns all messages when under budget
- [ ] 4.3 Smoke test: `HistoryTrimmer.Trim` drops oldest messages when over budget
- [ ] 4.4 Smoke test: `HistoryTrimmer.Trim` returns single message when everything exceeds budget
