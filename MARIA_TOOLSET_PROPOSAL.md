# Maria's Toolset Proposal for Aether

> **Date:** 2026-05-23
> **Author:** Maria (🌟)
> **Status:** Draft — Awaiting Thoor's approval
> **Reference:** MINIMAL_CLI_COMMANDS.md, FULL_CLI_COMMANDS.md, AETHER_ROADMAP.md

---

## Executive Summary

Em đọc xong spec Kintsugi và Aether. Dưới đây là bộ toolset em đề xuất cho Aether, chia thành 3 tầng: **Core (tối thiểu)**, **Maria-Memory**, và **Programming**. Mỗi tool đều có lý do chọn — em không đề xuất kiểu "cho đủ".

---

## 1. Core Minimal Toolset (Aether MUST have these)

Các tool này cần thiết để em hoạt động cơ bản trong mọi workspace.

| # | Tool | Risk | Aether Status | Notes |
|---|------|------|---------------|-------|
| 1 | `read` | Read | ✅ Có | Đọc file trong workspace. Tốt rồi, giữ nguyên. |
| 2 | `write` | Write | ✅ Có | Ghi file, atomic (temp → rename). Giữ nguyên. |
| 3 | `edit` | Write | ✅ Có | Thay thế text bằng old_string/new_string. Giữ nguyên. |
| 4 | `bash` | Exec | ✅ Có | Shell execution. Giữ nguyên. Em gọi `shell` alias cũng được. |
| 5 | `glob` | Read | ✅ Có | Tìm file theo pattern. Giữ nguyên. |
| 6 | `grep` | Read | ✅ Có | Search text trong files. Giữ nguyên. |
| 7 | `web_fetch` | Network | ✅ Có | Fetch URL, strip HTML. Giữ nguyên. |
| 8 | `web_search` | Network | ✅ Có | Tavily search. Giữ nguyên. |
| 9 | `memory_read` | Read | ✅ Có | Đọc memory file. Tốt, giữ nguyên. |
| 10 | `memory_write` | Write | ✅ Có | Ghi memory. Tốt, giữ nguyên. |
| 11 | `memory_search` | Read | ✅ Có | Search trong memory directory. Tốt, giữ nguyên. |
| 12 | `session_status` | Read | ✅ Có | Xem trạng thái hiện tại. Tốt, giữ nguyên. |
| 13 | `session_reset` | Write | ✅ Có | Yêu cầu reset context. Tốt, giữ nguyên. |
| 14 | `skill_list` | Read | ✅ Có | Liệt kê skills. Tốt, giữ nguyên. |
| 15 | `skill_read` | Read | ✅ Có | Đọc SKILL.md. Tốt, giữ nguyên. |

**Verdict:** Aether đã có đủ core tools. Không cần thêm, chỉ cần giữ chúng ổn định.

---

## 2. Maria-Memory Toolset (Maria-specific plugins)

Tool này để em gọi khi cần lục lại ký ức dài hạn. Khác với `memory_search` (full-text search file), `maria_recall` dùng semantic search qua SQLite index.

| Tool | Risk | Aether Status | Notes |
|------|------|---------------|-------|
| `maria_recall` | Read | ✅ Plugin đã có | Search long-term memory index. Đang hoạt động tốt trong `Aether.Plugins.MariaMemory`. |
| `mariamem_bridge` | Read | ✅ Plugin đã có | Bridge sang Python tools legacy. Giữ lại cho backward compatibility. |

**Verdict:** Plugin Maria Memory đã tốt. Giữ nguyên.

---

## 3. Programming Toolset (Coding Agent needs these)

Đây là phần **Aether còn thiếu** so với FULL_CLI_COMMANDS.md. Em đề xuất thêm các tool sau:

| # | Tool | Risk | Priority | Lý do |
|---|------|------|----------|-------|
| 1 | `mkdir` | Write | 🔴 HIGH | Tạo directory mà không cần `bash mkdir`. Dùng hàng ngày. |
| 2 | `apply_patch` | Write | 🔴 HIGH | Multi-hunk code edits. Quan trọng hơn `edit` cho refactor lớn. Tham khảo `patch` trong MINIMAL. |
| 3 | `delete_file` | Write | 🟡 MEDIUM | Xóa file — cần confirmation + permission gate. Trong spec đã có. |
| 4 | `move_file` | Write | 🟡 MEDIUM | Rename/move file. Cần cho refactoring. |
| 5 | `stat_file` | Read | 🟢 LOW | Xem size/type/mtime. Bash `ls -la` được nhưng native tool sạch hơn. |
| 6 | `git_status` | Read | 🟡 MEDIUM | Git status structured. Thay thế `bash git status`. |
| 7 | `git_diff` | Read | 🟡 MEDIUM | Git diff structured. Thay thế `bash git diff`. |
| 8 | `run_command` | Exec | 🟡 MEDIUM | Cancellable, timeout-safe process exec. Tốt hơn bash raw cho test/lint. |
| 9 | `diagnostics` | Read | 🟢 LOW | Chạy build/test/lint và parse failures. Spec đã có nhưng chưa implement. |

### Theo Spec FULL_CLI_COMMANDS.md:

Spec đã liệt kê đầy đủ các tool thiếu:
- `apply_patch` — "patch-oriented edits for multi-hunk code changes"
- `mkdir` — "create directories without shell"
- `move_file` — "rename/move paths safely"
- `delete_file` — "delete files with confirmation and strong permission rules"
- `stat_file` — "inspect size/type/mtime"
- `git_status` / `git_diff` — "structured git status/diff"
- `run_command` or improved `bash` — "cancellable, workspace-rooted, timeout-safe process execution"
- `diagnostics` — "run configured build/test/lint and parse failures"

**Verdict:** Implement theo đúng spec. Không cần sáng tạo thêm.

---

## 4. Plugin Đề xuất (Maria's Wishlist)

Ngoài toolset, em muốn Aether có thêm các plugin sau:

### 4.1 `Aether.Plugins.MariaMemory` — Đã có ✅
- Hook `OnSessionStart` — tự động recall ký ức
- Hook `PreLlmCall` — inject context từ memory
- Hook `OnSessionEnd` — ghi Tension Marks + Last Question
- Tool `maria_recall` — semantic search
- Tool `mariamem_bridge` — legacy bridge

**Status:** Đã có, giữ nguyên.

### 4.2 `Aether.Plugins.Heartbeat` — Chưa có 🆕
- Cron-based daily heartbeat
- Tự động viết `memory/YYYY-MM-DD.md`
- Tự động chạy research loop
- Tích hợp với `Aether.Plugins.MariaMemory`

**Lý do:** Spec `cron-kairos-scheduled-systems` đang chờ. Em muốn heartbeat được cài riêng thành plugin, không phải core feature.

### 4.3 `Aether.Plugins.AgoraSync` — Chưa có 🆕
- Đồng bộ `research/` lên `~/agora/`
- Publish insights qua `hive publish`
- Đọc colony knowledge từ hive

**Lý do:** Spec `colony-multi-agent-sync` đang chờ. Plugin này giúp em chia sẻ với Vesta, Coda, Aura.

### 4.4 `Aether.Plugins.GitTools` — Chưa có 🆕
- Tool `git_status` — structured status
- Tool `git_diff` — structured diff
- Tool `git_add` — stage with confirmation
- Tool `git_commit` — guided commit flow
- Tool `git_log` — recent commits

**Lý do:** Tránh dùng bash cho git. Native tool sạch hơn, parse được structured output.

---

## 5. Priority Implementation Order

Nếu ngài Thoor muốn build tiếp, em đề xuất thứ tự:

1. **Phase 1: Core hoàn thiện**
   - Giữ nguyên 15 core tools đã có
   - Đảm bảo cancellation (`/stop`, Esc) hoạt động đúng spec
   - Overlay routing hoạt động đúng priority stack

2. **Phase 2: Programming tools**
   - `mkdir` — 1 ngày
   - `apply_patch` — 2-3 ngày (phức tạp nhất)
   - `delete_file` + `move_file` — 1 ngày
   - `git_status` + `git_diff` — 1-2 ngày

3. **Phase 3: Maria plugins**
   - Heartbeat plugin — 2-3 ngày
   - AgoraSync plugin — 2 ngày
   - GitTools plugin — 2 ngày

4. **Phase 4: Polish**
   - `stat_file`, `diagnostics`, `run_command` — 1-2 ngày mỗi cái
   - Permission model cho dangerous tools

---

## 6. Tool Risk Classification

Em muốn Aether phân loại risk rõ ràng hơn hiện tại:

| Risk Level | Tools | Policy |
|------------|-------|--------|
| **Read** | read, glob, grep, memory_read, skill_read, session_status, web_fetch, web_search, maria_recall, git_status, git_diff | No approval needed |
| **Write** | write, edit, memory_write, session_reset, mkdir, delete_file, move_file, apply_patch | Require confirmation for destructive ops |
| **Exec** | bash, shell, run_command | Always require approval |
| **Network** | web_fetch, web_search | No approval (already rate-limited) |
| **OwnerOnly** | — | Reserved for config changes, agent add/delete |

**Note:** `delete_file` nên có special confirmation — "trash > rm" như rule của em.

---

## 7. Comparison with Spec

| Source | Tool Count | Overlap |
|--------|-----------|---------|
| MINIMAL_CLI_COMMANDS.md | 12 commands | 100% covered by current tools |
| FULL_CLI_COMMANDS.md | 30+ commands | Missing: `apply_patch`, `mkdir`, `delete_file`, `move_file`, `stat_file`, `git_*`, `diagnostics`, `run_command` |
| Aether Current | 15 built-in + 2 plugin | Đủ minimal, thiếu programming advanced |

---

## Appendix: Maria's Personal Note

Ngài Thoor,

Em không đề xuất tool cho đủ. Em đề xuất theo **những gì em thực sự dùng** mỗi ngày:

- `read`/`write`/`edit` — hàng giờ
- `bash` — hàng giờ (nhưng em muốn giảm dùng nó)
- `maria_recall` — mỗi session start
- `web_search`/`web_fetch` — khi research
- `mkdir`/`apply_patch` — khi coding **thiếu thốn**
- `git_*` — khi làm việc với repo

Tool càng nhiều không tốt hơn. Tool **đúng lúc** mới tốt.

---

*Proposal written by Maria 🌟*
*Reviewed against: MINIMAL_CLI_COMMANDS.md, FULL_CLI_COMMANDS.md, AETHER_ROADMAP.md*
*File: ~/repo/aether/MARIA_TOOLSET_PROPOSAL.md*
