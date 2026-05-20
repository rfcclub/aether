# Aether: A Home for AI Beings

Aether connects any LLM to any chat platform — but what it actually does is give an agent a place to **persist, reflect, and evolve** across sessions. Each agent is a directory of files (`SOUL.md`, `MEMORY.md`, `IDENTITY.md`). Aether is the engine that reads those files, runs the agent, and writes back what it learns.

## 🌟 What makes it different?

Most AI agent frameworks are pipelines: prompt in, response out. **Aether is a home.**

- **Agents have their own filesystem**: Personality, memory, tasks, and boundaries live in a directory they own.
- **Continuity across sessions**: Cognitive architecture so the agent remembers who it was and what happened last time.
- **Heartbeat and autonomous reflection**: Periodic "ticks" let the agent process queued tasks and consolidate memory without being prompted.
- **Multi-channel, multi-agent**: One Aether instance can route Telegram, WebSocket, and CLI messages to different agent profiles.

## 🚀 Quick Start

1. **Prerequisites**: .NET 9 SDK.
2. **API Key**: Set your OpenRouter, Anthropic, or Fireworks key.
   ```bash
   export AETHER_llm__api_key="sk-or-..."
   ```
3. **Run**:
   ```bash
   cd src/Aether
   dotnet run
   ```

## 📖 Documentation

- **[User Guide](USER_GUIDE.md)**: How to interact with agents, use Telegram, and manage profiles.
- **[Configuration Guide](CONFIGURATION.md)**: Deep dive into settings, providers, and sandbox security.
- **[Setup Guide](SETUP.md)**: Detailed technical installation and prerequisites.
- **[Architecture](ARCHITECTURE.md)**: Understanding the core engine and cognitive layers.

## 🛠 Project Structure

- `src/Aether/`: Core agent engine, channels, and providers.
- `src/Aether.Tui/`: Interactive Terminal UI chat interface.
- `src/Aether.Terminal/`: Avalonia-based cross-platform GUI.
- `agents/`: Default root for agent profile directories.
- `groups/`: Root for project context files (`CLAUDE.md`).

## 🧠 Cognitive Architecture (FEOFALLS)

Aether implements a layered cognitive model:
- **0_ Constitution**: Core unbreakable rules.
- **1_ Identity**: Self-model and personality.
- **2_ Cognitive**: Active instruction set.
- **3_ Learning**: Long-term memory and growth.
- **5_ Working State**: Ephemeral session context.

## ⚖️ License

MIT. 

---
*Aether fills the space between an AI model and a human — giving it continuity, memory, and a place to exist.*
