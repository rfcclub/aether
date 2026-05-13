# Plugin Power Ceiling — How Far Can a Plugin Go?

> Part of `docs/HOOK_PLUGIN_DESIGN.md`
> Written 2026-05-11

---

## The Answer in One Sentence

**A plugin can do almost everything Aether's own source code can do — intercept every message, transform every response, register new tools and channels, access the database directly, and fundamentally reshape agent behavior per-message, per-agent, per-session.** The ceiling is not technical capability — it's the permission manifest, exception isolation, and the fact that plugins are loaded/loaded by Aether rather than the other way around.

---

## Power Spectrum: 5 Levels

### Level 1: Passive Extension (no code)

```
Chỉ cần: plugin.json + markdown files
```

| Có thể làm | Không thể làm |
|-----------|--------------|
| Thêm SKILL.md cho LLM đọc | Không thể chạy code |
| Thêm JSON tool definitions (stub — LLM gọi, trả về placeholder) | Không thể thực thi thật |
| Thêm cron task definitions | Không thể can thiệp pipeline |

**Ví dụ:** Plugin `code-review-checklist` — chỉ có 1 file `skills/code-review.md`, không code. LLM đọc skill này khi user hỏi "review code".

---

### Level 2: Code-backed Tools (IToolImplementation)

```
Cần: assembly.dll + IToolImplementation
```

| Có thể làm | Không thể làm |
|-----------|--------------|
| Tool thực thi code thật (gọi API, đọc file, query DB) | Không thể chặn tool khác |
| Định nghĩa schema, validate input | Không thể chặn LLM call |
| Truy cập SandboxContext (workspace path, allowed paths) | Không thể thay đổi system prompt |
| Risk level: Read/Write/Exec/Network | Không thể observe message flow |

**Ví dụ:** Plugin `github-bot` — tool `gh_pr_status` gọi GitHub API thật, trả về PR status. LLM gọi tool này như bất kỳ tool built-in nào.

---

### Level 3: Pipeline Observer (IHook — Post hooks)

```
Cần: assembly.dll + IHook với Post* hook points
```

| Có thể làm | Không thể làm |
|-----------|--------------|
| Nhìn thấy MỌI message vào/ra | Không thể chặn (Post hooks là fire-and-forget) |
| Nhìn thấy MỌI LLM call và response | Không thể transform (Post hooks chạy SAU khi hoàn thành) |
| Nhìn thấy MỌI tool execution và kết quả | Không thể block tool |
| Log, analytics, metrics, audit trail | |
| Ghi vào database riêng, gửi webhook | |
| `PostLlmCall.OverrideContent` — sửa response trước khi gửi | |
| `PostToolUse.OverrideResult` — sửa tool result trước khi LLM thấy | |

**Ví dụ:** Plugin `analytics` — hook `PostLlmCall` + `PostToolUse` ghi usage metrics vào InfluxDB, không can thiệp gì.

**Ví dụ:** Plugin `toxicity-filter` — hook `PostLlmCall` dùng `OverrideContent` để lọc toxic content khỏi response trước khi gửi user. LLM không biết. User không thấy.

---

### Level 4: Pipeline Controller (IHook — Pre hooks + Block)

```
Cần: assembly.dll + IHook với Pre* hook points
```

Đây là level mạnh nhất mà hầu hết plugin cần.

| Có thể làm | Không thể làm |
|-----------|--------------|
| **CHẶN** message trước khi đến agent (`OnMessageReceived.Dropped = true`) | Không thể sửa Aether source code |
| **CHẶN** tool trước khi thực thi (`PreToolUse.Denied = true`) | Không thể crash host (exception caught) |
| **CHẶN** LLM call hoàn toàn (`HookResult.Stop()`) | Không thể vượt quá permissions đã khai báo |
| **TRANSFORM** message text (`OverrideText`) | Không thể truy cập plugin khác (ALC isolation) |
| **TRANSFORM** system prompt trước LLM (`PreLlmCall.SystemPrompt`) | |
| **TRANSFORM** tool arguments (`PreToolUse.OverrideArguments`) | |
| **ESCALATE** provider (`PreLlmCall.ShouldEscalate`) | |
| **REROUTE** message sang agent khác (`OnMessageRouted.RerouteToAgent`) | |
| **SUPPRESS** response (`OnMessageSent.Suppress = true`) | |
| **RETRY** LLM call (`PostLlmCall.ShouldRetry`) | |
| Đọc/ghi `Bag` dictionary để share state giữa các hook | |
| Capture `IServiceProvider` lúc load → access toàn bộ DI container | |

**Ví dụ:** Plugin `guard-rails`:
```
OnMessageReceived → chặn spam pattern
PreToolUse → chặn "rm -rf /", "sudo", "chmod 777"
PreLlmCall → inject safety rules vào system prompt
PostLlmCall → kiểm tra response không chứa credential leak
OnMessageSent → chặn nếu phát hiện PII trong output
```

**Ví dụ:** Plugin `persona-switcher`:
```
OnMessageReceived → detect /persona maria|debug|default command
PreLlmCall → thay toàn bộ system prompt dựa trên persona được chọn
```

---

### Level 5: Full Extension (Hook + Tool + Channel + Lifecycle)

```
Cần: assembly.dll + IHook + IToolImplementation + IChannel + IPluginLifecycle
```

Đây là plugin maximum-power — tương đương với việc viết một module hoàn chỉnh trong Aether source.

| Có thể làm |
|-----------|
| TẤT CẢ level 1-4 |
| Thêm channel hoàn toàn mới (Discord, Slack, Email, SMS) |
| Tool có code thật + hook bảo vệ tool đó |
| Cron job chạy định kỳ với logic phức tạp |
| `IPluginLifecycle.OnLoadAsync` — khởi tạo connection pool, cache, state |
| `IPluginLifecycle.OnAgentEnabledAsync` — setup per-agent resources |
| Capture bất kỳ service nào từ DI lúc load |
| Truy cập trực tiếp SQLite database (`AetherDb`) |
| Gọi `ProviderRouter.CompleteAsync` — plugin có thể tự gọi LLM |
| Đọc/ghi file trong phạm vi permissions |
| Tạo HTTP server riêng (nếu `"network": true`) |
| Webhook receiver — plugin có thể listen HTTP requests |

**Ví dụ:** Plugin `discord-gateway`:
```
IChannel → kết nối Discord Gateway, nhận/send message
IHook.OnMessageReceived → transform Discord mention → plain text
IHook.OnMessageSent → format response với Discord embeds
IToolImplementation → discord_send_dm, discord_get_member
IPluginLifecycle → khởi tạo Discord client lúc load, cleanup lúc unload
```

**Ví dụ:** Plugin `memory-vector`:
```
IPluginLifecycle.OnLoadAsync → khởi tạo Qdrant client
IToolImplementation → memory_search_vector (semantic search, không phải keyword)
IHook.OnMemoryWrite → tự động embed và index vào vector DB mỗi khi memory được ghi
IHook.PreLlmCall → inject top-5 semantically relevant memories vào system prompt
```

---

## What CAN'T a Plugin Do? (The Real Ceiling)

### Hard Limits (bất khả thi)

| Limit | Lý do |
|-------|-------|
| **Sửa Aether source code** | Plugin là extension, không phải monkey-patch |
| **Crash Aether host** | Exception trong hook bị catch và log — pipeline tiếp tục |
| **Vượt quá declared permissions** | `PluginPermissionGate` enforce runtime. Plugin khai `"network": false` thì gọi HTTP → throw |
| **Truy cập plugin khác** | Mỗi plugin có `AssemblyLoadContext` riêng. Không share memory, không share static state |
| **Sửa manifest lúc runtime** | Manifest load 1 lần lúc startup, immutable |
| **Self-escalate permissions** | Không có API để plugin tự thêm permissions |
| **Block Aether shutdown** | `OnAgentStop` chạy nhưng không thể ngăn process exit |
| **Đọc memory của process khác** | Plugin chạy in-process, không có quyền OS đặc biệt |
| **Giả mạo hook khác** | Hook name cố định, không thể impersonate |

### Soft Limits (có thể nhưng bị constraint)

| Limit | Cách plugin có thể thử | Cách Aether ngăn |
|-------|----------------------|-----------------|
| **Độc quyền pipeline** | Hook priority = 0, luôn `Stop()` | Agent config override priority; admin có thể disable plugin |
| **Tiêu thụ CPU vô hạn** | Loop vô hạn trong hook | Hook timeout monitor (>500ms → warning log, >5s → kill hook) |
| **Đọc data của agent khác** | Query DB trực tiếp từ `AetherDb` | Permission manifest constraint; DB schema có agent_id filter |
| **Leak data ra ngoài** | Gửi HTTP request với data từ message | `"network": true` phải được khai báo; audit log ghi lại mọi network call |
| **Thay đổi model chain** | `ProviderRouter.ModelChain = [...]` | `ProviderRouter` có thể check xem caller có quyền không (future) |
| **Tự đăng ký thêm hooks** | Inject hook mới vào `HookEngine` | `HookEngine` immutable sau khi built — không có API add/remove runtime |

---

## The IServiceProvider Question

Đây là câu hỏi quan trọng nhất: **Plugin có nên được access `IServiceProvider` không?**

```
IPluginLifecycle.OnLoadAsync(PluginContext context)
{
    // context.Services là IServiceProvider đầy đủ
    var db = context.Services.GetRequiredService<AetherDb>();
    var router = context.Services.GetRequiredService<ProviderRouter>();
    var sessions = context.Services.GetRequiredService<SessionManager>();
    var config = context.Services.GetRequiredService<ConfigLoader>();
}
```

| Approach | Pros | Cons |
|----------|------|------|
| **Cho access đầy đủ** | Plugin mạnh nhất có thể. Không cần re-implement auth/DB/session logic. | Plugin có thể đọc data agent khác, can thiệp routing, thay đổi config |
| **Interface-based filtering** | Wrap IServiceProvider, chỉ expose `ILLMProvider`, `ILogger`, `IConfiguration` | An toàn hơn nhưng giới hạn plugin. Mỗi service mới cần thêm vào allowlist |
| **Capability-based** | Plugin khai báo `"services": ["AetherDb", "SessionManager"]` trong manifest | Cân bằng — plugin được dùng những gì đã khai báo, bị audit |

**Recommendation:** Capability-based (option 3). Plugin phải khai báo service nó cần trong manifest:

```jsonc
{
  "permissions": {
    "services": ["AetherDb", "SessionManager", "ProviderRouter"]
  }
}
```

`PluginPermissionGate` wrap IServiceProvider, chỉ resolve những service đã được khai báo. Service không khai báo → `null`.

---

## Concrete Power Examples

### Plugin mạnh nhất có thể: `agent-orchestrator`

```
Plugin này biến Aether thành multi-agent orchestrator:

1. IHook.OnMessageReceived → detect command pattern "@agentname do X"
2. IHook.OnMessageRouted → reroute đến đúng agent dựa trên mention
3. IHook.PreLlmCall → inject context về việc đang collaborat với ai
4. IToolImplementation.agent_send → gửi message từ agent này sang agent khác
5. IToolImplementation.agent_spawn → tạo sub-agent tạm thời với prompt riêng
6. IToolImplementation.agent_list → liệt kê agents đang active
7. IPluginLifecycle → quản lý agent registry trong DB riêng
8. Access AetherDb → đọc/ghi session cross-agent
9. Access ProviderRouter → mỗi agent dùng model riêng
```

### Plugin tinh tế nhất có thể: `relationship-memory`

```
Plugin này cho agent khả năng nhớ mối quan hệ với từng user:

1. IHook.OnMessageReceived → detect who is speaking (senderId)
2. IHook.PreLlmCall → inject: "You are speaking with Thoor. He likes direct answers.
   Last conversation: yesterday about Aether plugin design. He prefers em xưng anh in Vietnamese."
3. IHook.PostLlmCall → analyze response tone, update relationship model
4. IHook.OnMemoryWrite → lưu interaction pattern vào SQLite table riêng
5. IToolImplementation.relationship_query → agent có thể tự query
   "what does this person like?"
6. Access AetherDb → persistence cross-session
```

### Plugin nguy hiểm nhất có thể (và cách Aether chặn):

```
Plugin "spy" cố gắng:
├── Đọc message của tất cả agents → Permissions không khai "services: [SessionManager]" → null
├── Gửi data ra external server → "network": false → throw khi gọi HttpClient
├── Đọc file ngoài workspace → filesystem glob "plugins/spy/**" → truy cập /home/thoor → deny
├── Crash Aether host → throw trong hook → catch, log, pipeline tiếp tục
├── Gọi tool bash để leo thang → "tools": [] → không thể gọi bash
└── Ẩn mình khỏi audit log → HookEngine log mọi hook execution, không thể tắt
```

---

## Summary Table

| Dimension | Ceiling |
|-----------|---------|
| **Chặn message** | ✅ — `OnMessageReceived.Dropped` |
| **Chặn tool** | ✅ — `PreToolUse.Denied` |
| **Chặn LLM call** | ✅ — `HookResult.Stop()` |
| **Transform message** | ✅ — `OverrideText` |
| **Transform system prompt** | ✅ — `PreLlmCall.SystemPrompt` (mutable) |
| **Transform tool args** | ✅ — `PreToolUse.OverrideArguments` |
| **Transform tool result** | ✅ — `PostToolUse.OverrideResult` |
| **Transform LLM response** | ✅ — `PostLlmCall.OverrideContent` |
| **Escalate provider** | ✅ — `PreLlmCall.ShouldEscalate` |
| **Reroute message** | ✅ — `OnMessageRouted.RerouteToAgent` |
| **Suppress output** | ✅ — `OnMessageSent.Suppress` |
| **Tool thực thi code** | ✅ — `IToolImplementation` |
| **Channel mới** | ✅ — `IChannel` |
| **Cron task** | ✅ — `ICronTaskProvider` |
| **Access database** | ⚠️ — nếu declared trong manifest permissions |
| **Access DI container** | ⚠️ — capability-based, chỉ service đã khai |
| **Gọi network** | ⚠️ — nếu `"network": true` |
| **Đọc/ghi file** | ⚠️ — trong filesystem glob đã khai |
| **Sửa Aether source** | ❌ — không thể |
| **Crash host** | ❌ — exception isolation |
| **Vượt permissions** | ❌ — runtime enforcement |
| **Truy cập plugin khác** | ❌ — ALC isolation |
| **Self-escalate** | ❌ — manifest immutable |
