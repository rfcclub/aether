## Why

Maria hiện có tool executor nhưng không có tool nào thực sự hoạt động. Hot-reloaded tools là passive stubs (chỉ log, không thực thi). Shell tool bị disabled. File tool chỉ read-only. Không có web search, web fetch, hay bash execution. Aether cần một tool ecosystem thực sự để Maria có thể tương tác với thế giới bên ngoài.

## What Changes

- Implement built-in tools: `read`, `write`, `edit`, `bash`, `glob`, `grep` (đã khai báo trong spec `tool-system` nhưng chưa có implementation)
- Add `web_search` tool using Tally API (đã có API key)
- Add `web_fetch` tool để đọc nội dung web page
- Bridge code-registered tools với real implementations (hiện tại hot-reload tools chỉ là passive stubs)
- Enable shell execution với sandbox path restrictions
- File write support với sandbox allowlist

## Capabilities

### New Capabilities

- `web-search-tool`: Web search via Tally Search API, trả về kết quả formatted
- `web-fetch-tool`: Fetch và parse web page content thành text
- `builtin-tool-implementations`: Triển khai thực tế cho read/write/edit/bash/glob/grep tools
- `tool-code-bridge`: Bridge cho phép code-registered tools có real implementation thay vì passive stub

### Modified Capabilities

- `tool-system`: Built-in tools requirement đã tồn tại nhưng chưa implemented — giờ có implementation thực sự
- `tool-executor`: Thêm sandbox-aware execution cho file write và shell commands

## Impact

- `src/Aether/Tooling/` — Tool implementations mới
- `src/Aether/Tooling/ToolHotReloadService.cs` — Bridge code-registered tools
- `src/Aether/Agent/ToolExecutor.cs` — Sandbox integration
- `src/Aether/Config/SpecContracts.cs` — Thêm web search config section
- `~/.aether/config.json` — Thêm Tally API key config
