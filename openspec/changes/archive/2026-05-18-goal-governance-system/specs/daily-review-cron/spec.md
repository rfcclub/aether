## MODIFIED Requirements

### Requirement: Daily review writes reflections to patches directory
The daily review SHALL write its findings to `patches/reflections-<YYYY-MM-DD>.md`. 
Ngoài các điểm ma sát (friction points), báo cáo SHALL bao gồm section **"Goal Progress"** tóm tắt trạng thái của tất cả mục tiêu Active và các mục tiêu đã hoàn thành trong ngày.

#### Scenario: Reflections file written successfully
- **WHEN** the daily review completes with identified friction points and goal updates
- **THEN** a file SHALL exist at `patches/reflections-<date>.md` containing both friction analysis and goal progress summary
