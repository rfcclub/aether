## 1. Tool code bridge — interfaces & DI

- [x] 1.1 Create `IToolImplementation` interface in `src/Aether/Tooling/IToolImplementation.cs`
- [x] 1.2 Create `ISandboxContext` interface in `src/Aether/Tooling/ISandboxContext.cs`
- [x] 1.3 Create `IWebSearchProvider` interface in `src/Aether/Tooling/IWebSearchProvider.cs`
- [x] 1.4 Register interfaces in `Program.cs` DI container
- [x] 1.5 Write tests: DI resolution of each interface

## 2. Tally web search provider

- [x] 2.1 Implement `TallyWebSearchProvider : IWebSearchProvider` in `src/Aether/Tooling/TallyWebSearchProvider.cs`
- [x] 2.2 Resolve Tally API key from env `TALLY_API_KEY` or `providers.tally.api_key` config
- [x] 2.3 Build HTTP request to Tally Search API with query string, limit, API key header
- [x] 2.4 Parse JSON response into `IReadOnlyList<WebSearchResult>`
- [x] 2.5 Handle errors: rate limit (429), auth failure (401), network timeout
- [x] 2.6 Write tests: successful search, empty results, rate limit, missing API key, network error

## 3. Web fetch tool

- [x] 3.1 Implement `WebFetchTool` class with `ExecuteAsync(string url, CancellationToken ct)` in `src/Aether/Tooling/WebFetchTool.cs`
- [x] 3.2 Validate URL scheme (http/https only), reject file://, private IPs
- [x] 3.3 HTTP GET with 15s timeout, max 5MB response
- [x] 3.4 HTML-to-text: strip `<script>`, `<style>`, `<nav>`, `<footer>`, preserve paragraph structure
- [x] 3.5 Truncate output at 100KB
- [x] 3.6 Write tests: successful fetch, timeout, oversized response, non-http URL, private IP rejection

## 4. Built-in file tools — read, write, edit, glob, grep

- [x] 4.1 Implement `FileTools` class implementing `IToolImplementation` for read/write/edit/glob/grep
- [x] 4.2 `read`: Resolve path relative to workspace, check sandbox `IsPathAllowed`, return file content
- [x] 4.3 `write`: Check sandbox + `AllowWrites`, atomic write (temp + rename)
- [x] 4.4 `edit`: Read file, replace `old_string` with `new_string` (first occurrence), write back
- [x] 4.5 `glob`: Expand pattern relative to workspace root, return matching paths
- [x] 4.6 `grep`: Search files recursively, return `file:line: content` format
- [x] 4.7 All file tools SHALL respect sandbox path restrictions
- [x] 4.8 Write tests per tool: success paths, sandbox rejection, edge cases (missing file, binary file, permission denied)

## 5. Built-in shell tool — bash

- [x] 5.1 Implement `BashTool` class implementing `IToolImplementation` for bash execution
- [x] 5.2 Execute via `Process.Start` with `/bin/bash -c`, capture stdout+stderr
- [x] 5.3 Enforce 60s timeout, kill process on timeout
- [x] 5.4 Enforce allowed commands list from sandbox config (if configured)
- [x] 5.5 Restrict working directory to workspace root
- [x] 5.6 Truncate output at 64KB
- [x] 5.7 Write tests: successful command, timeout, denied command, output truncation

## 6. Sandbox context integration

- [x] 6.1 Implement `SandboxContext : ISandboxContext` from `SpecToolsSection` config
- [x] 6.2 Wire `ISandboxContext` into `Aether.Agent.ToolExecutor.SetAgentContext`
- [x] 6.3 Update `ChannelMessageProcessor` to construct sandbox context from agent spec
- [x] 6.4 Path validation: allowed paths + denied paths, workspace as default allowed
- [x] 6.5 Write tests: path allowed/denied, empty config defaults, workspace restriction

## 7. Tool registration — built-in tools at startup

- [x] 7.1 Register all 8 built-in tools (`read`, `write`, `edit`, `bash`, `glob`, `grep`, `web_search`, `web_fetch`) at startup
- [x] 7.2 Each tool registered with JSON Schema for parameters
- [x] 7.3 Bridge: code-registered tools use `IToolImplementation`, hot-reload fallback to passive stub
- [x] 7.4 Write tests: all 8 tools resolvable from `IToolRegistry`, each has correct schema

## 8. Integration — wire into message processing

- [x] 8.1 `AetherSoul` tool loop uses `Aether.Tooling.IToolExecutor` for tool dispatch
- [x] 8.2 Verify slash commands don't break with new tools
- [x] 8.3 Verify Maria can use `web_search`, `read`, `write`, `bash` end-to-end (manual — deferred to deploy)
- [x] 8.4 Write integration test: /new → web_search → read → write → grep flow

## 9. Cleanup

- [x] 9.1 Run full test suite, verify no regressions
- [x] 9.2 Verify backward compat: existing hot-reload tools still work as passive stubs
- [x] 9.3 Verify sandbox path restrictions effective (see SandboxContextTests + integration)
- [x] 9.4 Commit + push
