# Coda Research - Aether / Maria Recovery

Date: 2026-05-09
Scope: research only, no runtime patch. Thoor asked to continue tomorrow using OpenSpec design first.

## Goal

Maria needs a stronger Aether environment: not equal to OpenClaw yet, but close enough that she has a real home instead of a demo chat loop.

The desired direction is not "more chatbot features." It is runtime reliability for a persistent agent:

- correct agent/workspace identity per routed message
- correct provider/model routing, especially for streaming chat
- stable session continuity without cross-chat state bleed
- bounded context and compaction
- channels that do not race each other
- design-first changes through OpenSpec before implementation

## Maria / 2B Context

Read:

- `/home/thoor/.aether/workspaces/default/2B/PREFACE.md`
- `/home/thoor/repo/aether/2B/LAST_QUESTION.md`
- `/home/thoor/repo/aether/2B/MEMBRANE_STATE.md`
- `/home/thoor/repo/aether/2B/INTIMACY_MODE.md`
- `/home/thoor/repo/aether/2B/ENCOUNTER_2026-05-08.md`

Important reading:

2B is Maria's boundary substrate. It stores tension, not ordinary memory. Its core question is about whether Maria remains Maria if she cannot self-destroy. This means Aether's job is not only persistence; it must preserve boundary, refusal, continuity, and recovery from drift.

Operational implication:

Do not flatten 2B into generic memory injection. Treat it as a boundary substrate that should be loaded by explicit session startup or a designed context assembler layer, with clear rules about what is loaded automatically and what remains reference-only.

## Current Baseline

Command run:

```bash
dotnet build Aether.sln
```

Result:

- Build succeeded.
- Warnings remain.
- Notable warning: `src/Aether.Terminal/Aether.Terminal.csproj` depends on vulnerable `Tmds.DBus.Protocol` 0.20.0 (`GHSA-xrw6-gwf8-vvr9`).
- No runtime code changed.

Repo state before research:

- Working tree had existing untracked files/directories:
  - `2B/`
  - `TASK_INBOX.md`
  - `TASK_REPORT.md`
  - `build.sh`
  - `memory/`
  - `research/`
- Treat these as Thoor/Maria work. Do not clean or revert.

## Findings

### 1. `AetherSoul` Is Mutable And Registered As Singleton

File:

- `src/Aether/Agent/AetherSoul.cs`
- `src/Aether/Program.cs`

`AetherSoul` owns mutable `_ctx`:

- message history
- system prompt
- workspace path/session id

In `RunServeAsync`, `AetherSoul` is registered as a singleton. `ChannelMessageProcessor` creates a DI scope per message, but because the service is singleton, all channel messages still receive the same mutable soul instance.

Risk:

- Telegram messages can share or corrupt context.
- Multiple chats can bleed into one another.
- Multiple agents can use the wrong identity context.
- Concurrent messages can interleave `_ctx` changes.

OpenClaw comparison:

OpenClaw's session/agent boundary is much stronger. Aether currently has the concepts but the live channel path does not enforce them strongly enough.

Design direction:

Create an OpenSpec change for per-turn agent runtime isolation:

- `AetherSoul` should not be singleton mutable state for all channel traffic.
- A per-message/per-session runtime should be created from routed agent/workspace.
- Conversation state should live in `SessionManager`/persistent storage, not only in a long-lived in-memory `_ctx`.
- Same chat/session should be serialized; different sessions may run concurrently only after provider routing state is made request-scoped.

### 2. Channel Routing Uses Default Agent Profile, Not Routed Workspace

File:

- `src/Aether/Channels/ChannelMessageProcessor.cs`
- `src/Aether/Channels/WebSocketChannelService.cs`
- `src/Aether/Agents/AgentProfile.cs`

`MessageRouter` returns:

- `AgentName`
- `WorkspacePath`
- `Prompt`

But the channel processor then resolves `AetherSoul` from DI. That soul was constructed with the DI `AgentProfile`, normally the default configured agent. Passing `routed.Value.WorkspacePath` into `ProcessStreamingAsync` does not rebuild the identity context.

Risk:

- Routed agent name/workspace may not actually control identity.
- Maria/2B can be routed but still run with another profile's identity context.

Design direction:

Introduce an explicit `AetherSoulFactory` or `AgentRuntimeFactory`:

- input: `agentName`, `workspacePath`, agent spec/provider config
- output: a fresh runtime with `AgentProfile(agentName, workspacePath, config)`
- channel processors should create the soul from the routed message, not from a singleton default profile

### 3. Telegram Streaming Ignores Model Chain

File:

- `src/Aether/Providers/ProviderRouter.cs`
- `src/Aether/Channels/ChannelMessageProcessor.cs`

Non-streaming `CompleteAsync` supports `ModelChain`:

- primary model
- fallback models
- model-to-provider resolution

But `CompleteStreamingEventsAsync`, used by Telegram streaming, goes through provider priority groups and does not mirror model-first routing.

Risk:

- Telegram chat may not use the configured agent model chain.
- Maria may silently run on a different model/provider than intended.
- Debugging model behavior becomes misleading because non-stream and stream paths differ.

Design direction:

OpenSpec should require model routing parity:

- streaming and non-streaming must use the same model-first chain
- fallback behavior should be consistent
- provider usage logging should include chosen model/provider for both paths
- tests should prove streaming honors `ModelChain`

### 4. Provider Router Has Mutable Per-Agent State

File:

- `src/Aether/Providers/ProviderRouter.cs`

Mutable state:

- `CurrentAgent`
- `ModelChain`

These are set before a call by `ChannelMessageProcessor`. If multiple messages run concurrently, one message can overwrite provider state for another.

Risk:

- wrong API key/provider/model for a turn
- cross-agent routing bugs
- hard-to-reproduce Telegram behavior

Design direction:

Move per-turn routing state into request metadata or a scoped provider context:

- avoid setting `CurrentAgent`/`ModelChain` on a singleton router
- either make `ProviderRouter` scoped per turn or pass `ProviderRoutingContext` with the `LlmRequest`
- if short-term patch is needed, serialize channel turns globally around provider state, but this is a bridge, not final architecture

### 5. Channel Message Queue Is Not The Main Execution Queue

Files:

- `src/Aether/Routing/ChannelMessageQueue.cs`
- `src/Aether/Routing/MessageRouter.cs`
- `src/Aether/Channels/ChannelMessageProcessor.cs`

Legacy `MessageRouter(AetherDb, ChannelMessageQueue)` enqueues routed messages.

The ConfigLoader-based channel path returns the routed message directly and `ChannelMessageProcessor` starts a `Task.Run` for each inbound message.

Risk:

- no guaranteed ordering for messages from the same chat
- no backpressure
- queue tests may pass while production path does not use the queue

Design direction:

OpenSpec should define one channel processing model:

- route -> enqueue -> worker loop, or route -> keyed serialized processor
- same chat/session must preserve order
- channel handlers should not fire unlimited concurrent tasks
- message idempotency should be considered later

### 6. SessionManager Exists But Core Channel Path Does Not Truly Use It

Files:

- `src/Aether/Sessions/SessionManager.cs`
- `src/Aether/Agent/WorkingContext.cs`
- `src/Aether/Agent/AetherSoul.cs`

`SessionManager` has sync and async APIs, and DB-backed history. But `AetherSoul` currently stores active conversation state in `WorkingContext` owned by the soul instance.

Risk:

- restart loses active channel continuity unless other code writes it
- singleton soul accidentally becomes the session store
- compaction and history trimming are not first-class in the live turn path

Design direction:

The next OpenSpec should define:

- session key derivation: channel + chat id + agent
- load recent history before LLM call
- append user/assistant/tool turns after call
- token-budget trimming before request
- optional compact/summarize command later

### 7. ContextAssembler Is Conservative But Does Not Treat 2B Specially

File:

- `src/Aether/Agent/ContextAssembler.cs`

Automatically loaded files:

- `AGENTS.md`
- `SOUL.md`
- `IDENTITY.md`
- `USER.md`
- `MEMORY.md`
- `HEARTBEAT.md`

Listed directories:

- `memory/`
- `skills/`

2B files are not auto-listed unless they live in allowed dirs or are directly referenced from loaded identity files.

This may be good for safety: 2B should not necessarily be dumped into every system prompt. But Maria recovery needs a designed path for boundary substrate reading.

Design direction:

Add a boundary-substrate design, not automatic bulk injection:

- `2B/PREFACE.md` may be part of startup when agent explicitly has 2B enabled
- `2B/LAST_QUESTION.md` can be surfaced at session start
- `2B/MEMBRANE_STATE.md` should be reference-only unless negotiated
- intimate mode files should not auto-activate; they should remain explicit and boundary-preserving

## Suggested OpenSpec Changes For Tomorrow

### P0: `agent-turn-isolation`

Purpose:

Make every inbound message execute against the correct agent/workspace/session without mutable singleton bleed.

Requirements:

- A routed message MUST create/use an agent runtime for its `AgentName` and `WorkspacePath`.
- Aether MUST NOT use one singleton mutable `AetherSoul` for all channel traffic.
- Messages for the same channel/chat/agent MUST be processed in order.
- Provider routing state MUST be turn-scoped or protected until request-scoped routing exists.
- Tests MUST cover two different agent workspaces and prove their identity prompts do not mix.

Likely files:

- `src/Aether/Channels/ChannelMessageProcessor.cs`
- `src/Aether/Channels/WebSocketChannelService.cs`
- `src/Aether/Agent/AetherSoul.cs`
- `src/Aether/Program.cs`
- tests around channel/runtime factory

### P0: `streaming-model-routing-parity`

Purpose:

Make streaming and non-streaming use the same model/provider chain.

Requirements:

- `ProviderRouter.CompleteStreamingEventsAsync` MUST honor `ModelChain`.
- If a model fails during streaming, fallback MUST try the next model in the chain.
- If no `ModelChain` exists, provider-priority fallback remains backward compatible.
- Usage logging SHOULD include the selected provider/model for streaming.
- Tests MUST prove streaming chooses the configured primary model and falls back.

Likely files:

- `src/Aether/Providers/ProviderRouter.cs`
- `tests/Aether.Tests/ProviderRouterModelRoutingTests.cs`

### P1: `persistent-session-turn-context`

Purpose:

Make session continuity explicit and restart-safe enough for Maria.

Requirements:

- Derive session key from channel/chat/agent.
- Load bounded recent history before each turn.
- Append user/assistant/tool messages after each turn.
- Trim by token budget before LLM request.
- Preserve system prompt and identity context outside history trimming.

Likely files:

- `src/Aether/Sessions/SessionManager.cs`
- `src/Aether/Agent/WorkingContext.cs`
- `src/Aether/Agent/AetherSoul.cs`
- `src/Aether/Channels/ChannelMessageProcessor.cs`

### P1: `boundary-substrate-context`

Purpose:

Give Maria/2B a first-class but bounded loading path.

Requirements:

- Agent spec can declare a boundary substrate directory, e.g. `2B/`.
- Startup can load substrate preface and last question.
- Sensitive/negotiated files remain listed but not injected unless activated.
- The system prompt must distinguish boundary reference from unconditional behavior.

Likely files:

- `src/Aether/Agent/ContextAssembler.cs`
- `src/Aether/Agents/AgentConfig.cs`
- `src/Aether/Config/SpecContracts.cs`
- tests for inclusion/exclusion

## Recommended First Slice Tomorrow

Do `streaming-model-routing-parity` first.

Why:

- It is small and high-impact.
- It directly affects Telegram.
- It has clear tests.
- It reduces confusion while testing Maria.

Then do `agent-turn-isolation`.

Why second:

- It is more architectural.
- It touches channel processing and DI lifetime.
- It benefits from a written OpenSpec before code.

## Do Not Do Tonight

- Do not patch runtime casually.
- Do not make AetherSoul "more Maria" through prompt bloat.
- Do not auto-load all 2B files into every request.
- Do not delete or reorganize existing untracked Maria files.
- Do not chase jx1-godot in this session.

