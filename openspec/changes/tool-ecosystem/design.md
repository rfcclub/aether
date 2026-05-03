## Context

Aether có tool infrastructure (registry, hot-reload, executor) nhưng chưa có tool nào thực sự hoạt động. Hot-reloaded tools từ `tools/*.json` chỉ là passive stubs — chúng log invocation và return mock result. Built-in tools (`read`, `write`, `edit`, `bash`, `glob`, `grep`) được khai báo trong spec `tool-system` nhưng chưa implement.

Maria (Aether agent) cần công cụ thực sự để tương tác với filesystem, web, và shell. Thoor có Tally API key — dùng Tally làm web search provider đầu tiên.

## Goals / Non-Goals

**Goals:**
- Implement 6 built-in tools với real execution: `read`, `write`, `edit`, `bash`, `glob`, `grep`
- Thêm `web_search` tool dùng Tally Search API
- Thêm `web_fetch` tool để fetch web page content
- Bridge cho code-registered tools: hot-reload JSON có thể reference một implementation có sẵn
- File + shell tools tôn trọng sandbox config từ `SpecToolsSection`
- Tất cả tools dùng chung parameter schema format (JSON Schema)

**Non-Goals:**
- Code execution sandbox (Python/JS runtime) — scope riêng
- Image generation / multimodal output
- Tool approval workflow (autonomy levels) — đã có trong spec, chưa implement
- Multi-agent tool sharing

## Decisions

### Decision 1: Tool implementation bridge pattern

Chọn: `IToolImplementation` interface + named registration.

```csharp
public interface IToolImplementation
{
    string Name { get; }
    JsonElement ParametersSchema { get; }
    Task<object> ExecuteAsync(JsonElement args, ISandboxContext sandbox, CancellationToken ct);
}
```

Hot-reload JSON thêm optional field `"implementation"` — nếu có, tool sẽ delegate execution sang registered implementation. Nếu không có, giữ nguyên passive stub behavior (backward compat).

Alternatives considered:
- Attribute-based `[Tool("name")]` — phức tạp hơn, cần assembly scanning
- Subclass `ToolDefinition` — phá vỡ hot-reload model
- Mỗi tool là một class riêng — clean nhưng verbose cho simple tools

### Decision 2: Web search provider abstraction

Chọn: Provider pattern giống `ILLMProvider`.

```csharp
public interface IWebSearchProvider
{
    string Name { get; }
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, int limit, CancellationToken ct);
}
```

Tally implementation trước. Có thể thêm Brave, Google sau mà không đổi tool interface.

### Decision 3: Sandbox context injection

Chọn: `ISandboxContext` interface inject vào tool implementation.

```csharp
public interface ISandboxContext
{
    string WorkspacePath { get; }
    bool IsPathAllowed(string path);
    bool AllowWrites { get; }
    List<string> AllowedPaths { get; }
    List<string> DeniedPaths { get; }
}
```

ToolExecutor tạo sandbox context từ `SpecToolsSection` và inject vào mọi tool call. File tools (`read`, `write`, `edit`) kiểm tra `IsPathAllowed` trước khi thao tác.

### Decision 4: Web fetch HTML-to-text

Chọn: AngleSharp (HTML parser) + HtmlAgilityPack fallback, strip tags + scripts, trả về plain text tối đa 100KB.

Alternatives considered:
- Regex strip HTML — không an toàn, dễ miss script/style
- Puppeteer/Playwright — quá nặng cho agent tool
- Readability algorithm — overkill cho phase 1

### Decision 5: Tool parameter schema format

Chọn: JSON Schema (đã dùng trong `ToolDefinition.ParametersSchema`). Mỗi built-in tool define schema inline. Web search schema: `{query: string, limit?: int}`.

## Risks / Trade-offs

- **Tally API rate limit** → Cache results với TTL 60s, báo lỗi rõ ràng khi hit limit
- **Bash execution an toàn** → Sandbox: chỉ allowed commands list, timeout 60s, deny network commands (curl, wget) trừ khi explicit allow
- **File write corrupt workspace** → Chỉ write trong allowed paths, atomic write (write to temp + rename)
- **Web fetch timeout** → 15s timeout, max 5MB response size
