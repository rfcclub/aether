# AETHER & OPENCLAW GATEWAY PROXY GUIDE
## ──── Integrating Aether with OpenClaw Proxy API ────

This guide explains how to route all **Aether Engine** LLM requests through the local **OpenClaw Gateway** instead of communicating directly with public upstream APIs. 

### Why Route Through OpenClaw?
* 📊 **Unified Caching & Logging:** Track and store all prompts, completions, and token saving statistics in one local database.
* 🧠 **Memory Hook Integrations:** Enable automatic memory indexing hooks (`maria-memory` etc.) during conversation streams.
* 🤖 **Local & Third-Party Models:** Seamlessly mix local models (like `llama.cpp` or Ollama) with commercial models (like Kimi or Gemini) under a unified gateway.

---

## Ⅰ. GATEWAY CREDENTIALS & BASELINE

First, identify your local **OpenClaw** gateway settings (typically configured in `~/.openclaw/openclaw.json`):

| Property | Value | Description |
| :--- | :--- | :--- |
| **Local Port** | `18789` | The default port OpenClaw listens on. |
| **API Endpoint** | `http://127.0.0.1:18789/v1` | OpenAI-compatible endpoint. |
| **Gateway Token** | `8c99ca5e3bfa4b5cf06c9a2f33262fff3f544f80de2409df` | The gate authorization token. |

> [!WARNING]
> Ensure the OpenClaw service is running locally on WSL before starting the Aether Server or TUI.
> You can verify the gateway status via `systemctl --user status openclaw-gateway` or by checking active ports.

---

## Ⅱ. AETHER CONFIGURATION (`~/.aether/config.json`)

To route Aether's providers through OpenClaw, modify your global Aether configuration file at `/home/thoor/.aether/config.json` as shown below:

```json
{
  "providers": {
    "fireworks-ai": {
      "type": "openai",
      "base_url": "http://127.0.0.1:18789/v1",
      "api_key": "8c99ca5e3bfa4b5cf06c9a2f33262fff3f544f80de2409df",
      "model": "accounts/fireworks/routers/kimi-k2p6-turbo",
      "models": [
        "accounts/fireworks/routers/kimi-k2p6-turbo"
      ]
    },
    "llama-cpp": {
      "type": "openai",
      "base_url": "http://127.0.0.1:18789/v1",
      "api_key": "8c99ca5e3bfa4b5cf06c9a2f33262fff3f544f80de2409df",
      "model": "local-llama",
      "models": [
        "local-llama"
      ]
    },
    "openrouter": {
      "type": "openai",
      "base_url": "http://127.0.0.1:18789/v1",
      "api_key": "8c99ca5e3bfa4b5cf06c9a2f33262fff3f544f80de2409df",
      "model": "google/gemini-2.5-flash",
      "models": [
        "google/gemini-2.5-flash",
        "deepseek/deepseek-r1:free"
      ]
    }
  },
  "agents": {
    "defaults": {
      "model": {
        "primary": "fireworks-ai/accounts/fireworks/routers/kimi-k2p6-turbo",
        "fallbacks": [
          "openrouter/deepseek/deepseek-r1:free",
          "llama-cpp/local-llama"
        ]
      }
    },
    "default": {
      "name": "default",
      "workspace": "/home/thoor/.aether/workspaces/default",
      "enabled": true,
      "displayName": "Maria",
      "emoji": "🌸",
      "model": {
        "primary": "fireworks-ai/accounts/fireworks/routers/kimi-k2p6-turbo",
        "fallbacks": [
          "openrouter/deepseek/deepseek-r1:free",
          "llama-cpp/local-llama"
        ]
      }
    }
  }
}
```

---

## Ⅲ. UNDER THE HOOD: HOW IT WORKS

Aether uses `ProviderRouter` and `ConfigLoader` to dynamically route requests based on model identifiers:

1. **Routing Path Resolution (`[provider-slug]/[model-id]`):**
   When Aether makes a call to `"fireworks-ai/accounts/fireworks/routers/kimi-k2p6-turbo"`, the prefix `fireworks-ai` is extracted, normalized by stripping `-ai` to match the local provider `"fireworks-ai"` in your configuration.
2. **Payload Delivery:**
   Aether serializes the request using the provider's details:
   * **Base URL:** `http://127.0.0.1:18789/v1` (OpenClaw)
   * **Auth Header:** `Bearer 8c99ca5e3bfa...` (OpenClaw Auth)
   * **Model Param:** `"accounts/fireworks/routers/kimi-k2p6-turbo"`
3. **Gateway Dispatch:**
   OpenClaw interceptor receives the request, identifies the model ID uniquely belonging to the `fireworks-ai` provider registered in `openclaw.json`, replaces the authorization with your authentic Fireworks key (`fpk_...`), and securely handles the upstream dispatch.

---

## Ⅳ. VERIFYING THE CONNECTION

Once configured, verify that Aether successfully communicates through OpenClaw:

1. Start the Aether TUI or REPL:
   ```bash
   dotnet run --project src/Aether.Tui
   ```
2. Monitor real-time logs on the OpenClaw WSL side:
   ```bash
   journalctl --user -u openclaw-gateway.service -f
   ```
3. Send a message in Aether TUI. You should see a incoming log entry on OpenClaw:
   ```text
   POST /v1/chat/completions -> routed to fireworks-ai [accounts/fireworks/routers/kimi-k2p6-turbo] (200 OK)
   ```

*Happy forging!* 🔥
