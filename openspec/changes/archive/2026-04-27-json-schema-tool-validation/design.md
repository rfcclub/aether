## Context

Aether's tool executor currently passes arbitrary JSON arguments from LLM tool calls directly to sandboxed processes. The `ParametersJson` field on `LlmTool` is sent to the LLM as a hint, but the LLM can return parameters that don't match the schema. A malformed parameter (wrong type, missing required field, invalid path) can crash the tool executor or bypass path validation.

NJsonSchema is the standard .NET library for JSON Schema validation. It supports drafts 4 through 2020-12, compiles schemas to efficient validators, and produces structured error output suitable for feeding back to the LLM.

## Goals / Non-Goals

**Goals:**
- Validate tool call arguments against a JSON Schema before sandboxed execution
- Return structured, actionable validation errors the LLM can self-correct
- Compile schemas once at registration time for zero runtime parsing overhead
- Integrate cleanly into the existing tool loop without changing `IToolExecutor` contract
- Provide JSON Schemas for all 6 built-in tools

**Non-Goals:**
- Schema generation from C# types or OpenAPI — schemas are hand-written JSON
- Validation of tool outputs (only inputs/parameters)
- Dynamic schema loading from plugins — schemas are embedded at registration
- Runtime schema hot-reload or versioning

## Decisions

### 1: NJsonSchema over manual validation or System.Text.Json.Schema

**Decision:** Use `NJsonSchema` NuGet package.

**Rationale:** NJsonSchema supports all JSON Schema drafts (draft-04 through 2020-12), produces structured validation error objects with JSON path, line number, and error kind, and compiles schemas to efficient validators. Manual validation would be ad-hoc and incomplete. `System.Text.Json.Schema` (JSON Schema exposition) is the inverse — it generates schemas from types — and doesn't validate.

**Alternatives considered:**
- Manual validation per tool (rejected: incomplete, error-prone, duplicates schema already in ParametersJson)
- `JsonSchema.Net` from json-everything (rejected: heavier dependency, less mature ecosystem)
- `FluentValidation` (rejected: works on .NET objects, not raw JSON)

### 2: Validate in AetherSoul loop vs inside ToolExecutor

**Decision:** Validate in `AetherSoul.RunLlmToolLoopAsync` before calling `_tools.ExecuteAsync`.

**Rationale:** Validation is an LLM-interaction concern — the LLM needs the errors to self-correct. Validating before the sandbox also means invalid calls never touch the filesystem. `IToolExecutor` contract doesn't change — it only receives pre-validated calls.

### 3: Schema compiled at registration vs on each call

**Decision:** Compile JSON Schema to `NJsonSchema.JsonSchema` object at tool registration time. Store the compiled schema on `LlmTool`. At validation time, call `schema.Validate(json)`.

**Rationale:** JSON Schema compilation involves parsing the schema document and building a validator. Doing this on every tool call would add ~1-5ms overhead per call. Pre-compilation makes validation sub-millisecond.

### 4: Validation error format for LLM feedback

**Decision:** Format validation errors as a markdown-like structured message listing each error with its JSON path and description. The LLM sees them in the tool-result message, matching the pattern it already receives for execution failures.

```
Tool validation failed (2 errors):
- $.path: String expected, got number
- : Required property 'command' is missing
```

## Risks / Trade-offs

- **Schema drift**: `ParametersJson` (sent to LLM) and `SchemaJson` (used for validation) could diverge → Mitigation: derive both from a single source, or use schema as the source of truth and generate ParametersJson from it
- **NJsonSchema startup cost**: Compiling 6+ schemas at startup adds ~50ms → Acceptable; if it grows, lazy-compile on first use
- **LLM still generates invalid args after error**: The same issue exists today with execution failures; schema errors are more specific and actionable
- **Schema versioning**: If schemas change across Aether versions, old tools may fail validation → All schemas are embedded in source and versioned with the codebase

## Open Questions

- Should `ParametersJson` be auto-generated from `SchemaJson`? (Recommended: yes, as a follow-up)
- Should custom/plugin tools be able to provide their own schemas? (Yes — `SkillDefinition` could carry an optional schema path)
