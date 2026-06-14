# first-run-wizard Specification

## Purpose

Define the interactive first-run experience that detects a fresh Aether installation, guides the user through initial configuration, and produces a working `~/.aether/config.json` with at least one agent.

## ADDED Requirements

### Requirement: First run detected by missing config.json

Aether SHALL detect first-run state by checking for the existence of `~/.aether/config.json`. If the file does not exist (after working directory initialization), Aether SHALL enter the first-run wizard.

#### Scenario: No config triggers wizard

- **WHEN** `~/.aether/` exists but `~/.aether/config.json` does not
- **THEN** Aether SHALL display the first-run wizard welcome message

#### Scenario: Existing config skips wizard

- **WHEN** `~/.aether/config.json` exists and is valid JSON
- **THEN** Aether SHALL skip the wizard and proceed to normal startup

#### Scenario: Non-interactive flag skips wizard

- **WHEN** `--non-interactive` flag is passed and `~/.aether/config.json` does not exist
- **THEN** Aether SHALL create a minimal `config.json` with framework defaults and exit with message: "Non-interactive mode: minimal config created at ~/.aether/config.json. Edit this file to configure providers and agents."

### Requirement: Wizard prompts for provider selection

The wizard SHALL prompt the user to select an LLM provider from a list: OpenRouter, Anthropic, Fireworks, or "Other (OpenAI-compatible)". The selection SHALL set `llm.provider` in the resulting config.

#### Scenario: User selects OpenRouter

- **WHEN** the user selects "OpenRouter" during the wizard
- **THEN** `~/.aether/config.json` SHALL contain `"llm": { "provider": "openrouter", "base_url": "https://openrouter.ai/api/v1" }`

#### Scenario: User selects Anthropic

- **WHEN** the user selects "Anthropic" during the wizard
- **THEN** `~/.aether/config.json` SHALL contain `"anthropic": { "enabled": true, "base_url": "https://api.anthropic.com" }`

### Requirement: Wizard prompts for API key

After provider selection, the wizard SHALL prompt for an API key (masked input). The key SHALL be written to the provider's `api_key` field in `~/.aether/config.json`.

#### Scenario: API key stored in config

- **WHEN** the user enters API key `sk-or-abc123` during wizard
- **THEN** `config.json` SHALL contain `"llm": { "api_key": "sk-or-abc123" }`

#### Scenario: Skipped API key allowed

- **WHEN** the user presses Enter without typing an API key
- **THEN** the wizard SHALL warn that no key was provided, store an empty `api_key`, and note that the user can set `AETHER_llm__api_key` as an environment variable instead

### Requirement: Wizard prompts for first agent name

The wizard SHALL prompt for an initial agent name (default: "default"). This name SHALL be used to create the first agent workspace via the same scaffolding logic as `aether agent add`.

#### Scenario: Default agent created

- **WHEN** the user accepts the default agent name "default"
- **THEN** `~/.aether/workspaces/default/` SHALL be scaffolded with all personality files and `config.json` SHALL contain `agents.default`

#### Scenario: Custom agent name

- **WHEN** the user enters agent name "aria"
- **THEN** `~/.aether/workspaces/aria/` SHALL be scaffolded and `config.json` SHALL contain `agents.aria`

### Requirement: Wizard offers optional Telegram setup

After agent creation, the wizard SHALL ask: "Configure Telegram channel? (y/n)". If yes, it SHALL prompt for a bot token and enable the Telegram channel in config.

#### Scenario: Telegram configured

- **WHEN** user answers "y" and provides bot token `123:abc`
- **THEN** `config.json` SHALL contain `"channels": { "telegram": { "enabled": true, "bot_token": "123:abc" } }`

#### Scenario: Telegram skipped

- **WHEN** user answers "n"
- **THEN** the wizard SHALL skip Telegram configuration and proceed to completion

### Requirement: Wizard completion writes metadata

When the wizard completes, `config.json` SHALL include `wizard.lastRunAt` (ISO 8601 timestamp), `wizard.lastRunVersion` (Aether version), and `wizard.lastRunCommand` ("configure"). This matches OpenClaw's wizard metadata format.

#### Scenario: Wizard metadata recorded

- **WHEN** the first-run wizard completes
- **THEN** `config.json` SHALL contain `"wizard": { "lastRunAt": "<timestamp>", "lastRunVersion": "<version>", "lastRunCommand": "configure" }`

#### Scenario: Completion message shown

- **WHEN** the wizard completes successfully
- **THEN** Aether SHALL print a summary: "Aether is ready. Agent '<name>' created. Run 'aether serve' to start, or 'aether run --prompt \"hello\"' for a test."
