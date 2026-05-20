## MODIFIED Requirements

### Requirement: Unified CompleteAsync contract across all providers
All `ILLMProvider` implementations SHALL expose `CompleteAsync(LlmRequest, CancellationToken)`. 
Ngoài ra, Router SHALL hỗ trợ **Skill-based Routing**: Nếu một request yêu cầu một kỹ năng chuyên biệt (ví dụ: `code-fixing`), Router có thể ưu tiên điều hướng tới Agent profile có điểm kỹ năng cao nhất cho lĩnh vực đó.

#### Scenario: Route by skill
- **WHEN** một yêu cầu đến chứa metadata `required_skill: "technical-forge"`
- **THEN** Router SHALL ưu tiên chọn Agent 'Vesta' thay vì 'Maria'
