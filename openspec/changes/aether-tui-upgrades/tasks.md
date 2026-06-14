# Implementation Plan: aether-tui-upgrades

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement dynamic agent selection (`--agent <name>` / `-a`) for both C# and Rust TUI clients, fix C# TUI background/display name bugs, and support global installation via `./aether-update.sh --install`.

**Architecture:** 
- Expose a `TuiArgs` parser class in the core `Aether` assembly to handle client command-line arguments and unit-test it in isolation.
- Inject the parsed agent name into the `IConfiguration` container under `agent:name` on C# TUI startup.
- Update C# TUI path resolution (`ResolvePath`) to trace parent directories for the solution file `Aether.sln` to obtain a stable repository root when executed globally from any current working directory.
- Fix terminal background leakage by fully defining the input prompt color scheme and display the friendly display name.
- Map `--agent` and `-a` command-line arguments to `group` in the Rust client and configure WebSocket handshake routing.
- Add `--install` compilation and symlink logic in `aether-update.sh`.

**Tech Stack:** C# (NET 10.0, Terminal.Gui, Microsoft.Extensions.Configuration), Rust (Cargo, Clap, Ratatui, Tokio), Bash.

---

### Task 1: Core TuiArgs Parser & Unit Tests

**Files:**
- Create: `src/Aether/Config/TuiArgs.cs`
- Create: `tests/Aether.Tests/TuiStartupTests.cs`

- [x] **Step 1: Write the failing test**

  Create `tests/Aether.Tests/TuiStartupTests.cs` with test cases verifying argument parsing:
  ```csharp
  using Xunit;
  using Aether.Config;

  namespace Aether.Tests;

  public class TuiStartupTests
  {
      [Fact]
      public void ParseAgentName_ShouldReturnAgentName_WhenLongFlagProvided()
      {
          var args = new[] { "--agent", "aura" };
          var name = TuiArgs.ParseAgentName(args);
          Assert.Equal("aura", name);
      }

      [Fact]
      public void ParseAgentName_ShouldReturnAgentName_WhenShortFlagProvided()
      {
          var args = new[] { "-a", "vesta" };
          var name = TuiArgs.ParseAgentName(args);
          Assert.Equal("vesta", name);
      }

      [Fact]
      public void ParseAgentName_ShouldReturnNull_WhenFlagMissing()
      {
          var args = new[] { "--url", "ws://localhost:5099/ws" };
          var name = TuiArgs.ParseAgentName(args);
          Assert.Null(name);
      }

      [Fact]
      public void ParseAgentName_ShouldReturnNull_WhenValueIsMissing()
      {
          var args = new[] { "--agent" };
          var name = TuiArgs.ParseAgentName(args);
          Assert.Null(name);
      }
  }
  ```

  Run: `dotnet test --filter TuiStartupTests`
  Expected: FAIL (compilation error, type `TuiArgs` not found)

- [x] **Step 2: Write minimal implementation**

  Create `src/Aether/Config/TuiArgs.cs`:
  ```csharp
  namespace Aether.Config;

  public static class TuiArgs
  {
      public static string? ParseAgentName(string[] args)
      {
          for (int i = 0; i < args.Length - 1; i++)
          {
              if (args[i] == "--agent" || args[i] == "-a")
              {
                  var val = args[i + 1];
                  if (!string.IsNullOrEmpty(val) && !val.StartsWith("-"))
                      return val;
              }
          }
          return null;
      }
  }
  ```

- [x] **Step 3: Run test to verify it passes**

  Run: `dotnet test --filter TuiStartupTests`
  Expected: PASS

- [x] **Step 4: Commit**

  Run:
  ```bash
  git add src/Aether/Config/TuiArgs.cs tests/Aether.Tests/TuiStartupTests.cs
  git commit -m "feat(tui): add core TuiArgs parser and unit tests"
  ```

---

### Task 2: Support DisplayName in AgentProfile

**Files:**
- Modify: `src/Aether/Agents/AgentProfile.cs`

- [x] **Step 1: Modify AgentProfile properties and constructor**

  Open `src/Aether/Agents/AgentProfile.cs` and add `DisplayName` property, and update the constructor signature to accept it:
  ```csharp
      /// <summary>
      /// Tên hiển thị thân thiện của Agent (ví dụ: Maria, Aura, Vesta).
      /// </summary>
      public string DisplayName { get; }
  ```
  Update constructor (line 39-46):
  ```csharp
      public AgentProfile(string name, string agentDirectory, AgentConfig config, AgentModelConfig model, string? displayName = null)
      {
          Name = name;
          AgentDirectory = agentDirectory;
          _config = config;
          Model = model;
          DisplayName = displayName ?? name;
      }
  ```

- [x] **Step 2: Update FromConfigLoader in AgentProfile**

  Update `FromConfigLoader` method in `src/Aether/Agents/AgentProfile.cs` (around line 57) to read `DisplayName` from config and pass it to the constructor:
  ```csharp
      public static AgentProfile FromConfigLoader(
          string name,
          ConfigLoader configLoader,
          AgentConfig config,
          ILogger? logger = null)
      {
          logger ??= NullLogger.Instance;

          var agentConfig = configLoader.GetAgentConfig(name);
          var newPath = agentConfig?.Workspace;
          var model = agentConfig?.Model ?? new AgentModelConfig();
          var displayName = agentConfig?.DisplayName;

          if (!string.IsNullOrEmpty(newPath) && Directory.Exists(newPath))
          {
              return new AgentProfile(name, newPath, config, model, displayName);
          }

          // Tương thích ngược: <cwd>/agents/<name>/ (legacy layout)
          var legacyPath = Path.Combine(Environment.CurrentDirectory, "agents", name);
          if (Directory.Exists(legacyPath))
          {
              logger.LogWarning("Agent '{Name}' using legacy path {Path}. Migrate to ~/.aether/workspaces/{Name}/",
                  name, legacyPath, name);
              return new AgentProfile(name, legacyPath, config, model, displayName);
          }

          throw new DirectoryNotFoundException(
              $"Agent directory not found for '{name}'. " +
              $"Tried: {newPath ?? "<no workspace in config>"} and {legacyPath}");
      }
  ```

- [x] **Step 3: Verify build and compatibility**

  Run: `dotnet build src/Aether/Aether.csproj`
  Expected: Build successful. Existing unit tests compile perfectly due to the optional `displayName` parameter.

- [x] **Step 4: Commit**

  Run:
  ```bash
  git add src/Aether/Agents/AgentProfile.cs
  git commit -m "feat(agent): support DisplayName property in AgentProfile"
  ```

---

### Task 3: Implement Dynamic Argument Parsing & Path Resolution in C# TUI

**Files:**
- Modify: `src/Aether.Tui/Program.cs`

- [x] **Step 1: Implement repository root tracing and update path resolver**

  Modify the `GetRepositoryRoot` and `ResolvePath` helpers at the bottom of `src/Aether.Tui/Program.cs` (around line 623):
  ```csharp
  static string GetRepositoryRoot()
  {
      var dir = AppContext.BaseDirectory;
      while (!string.IsNullOrEmpty(dir))
      {
          if (File.Exists(Path.Combine(dir, "Aether.sln")))
          {
              return dir;
          }
          dir = Path.GetDirectoryName(dir);
      }
      return AppContext.BaseDirectory;
  }

  static string ResolvePath(string path)
  {
      if (Path.IsPathRooted(path)) return path;

      var repoRoot = GetRepositoryRoot();
      var repoPath = Path.GetFullPath(Path.Combine(repoRoot, path));
      if (File.Exists(repoPath) || Directory.Exists(repoPath)) return repoPath;

      var cwdPath = Path.GetFullPath(path);
      if (File.Exists(cwdPath)) return cwdPath;

      return Path.Combine(AppContext.BaseDirectory, path);
  }
  ```

- [x] **Step 2: Update database registrations to use ResolvePath**

  Update database-related registrations in `BuildServices` in `src/Aether.Tui/Program.cs` to resolve `databasePath`:
  ```csharp
      services.AddSingleton(provider =>
      {
          var config = provider.GetRequiredService<IConfiguration>();
          var databasePath = ResolvePath(config["database:path"] ?? "store/aether.db");
          var schemaPath = ResolvePath(config["database:schema"] ?? Path.Combine("Data", "Schema.sql"));
          return new AetherDb(databasePath, schemaPath);
      });
  ```
  And `SqliteMemorySystem` (around line 561):
  ```csharp
      services.AddSingleton<SqliteMemorySystem>(provider =>
      {
          var configuration = provider.GetRequiredService<IConfiguration>();
          var profile = provider.GetRequiredService<AgentProfile>();
          var databasePath = ResolvePath(configuration["database:path"] ?? "store/aether.db");
          var memoryFilePath = Path.Combine(profile.AgentDirectory, "MEMORY.md");
          var logger = provider.GetRequiredService<ILogger<SqliteMemorySystem>>();
          return new SqliteMemorySystem(databasePath, memoryFilePath, logger, null);
      });
  ```

- [x] **Step 3: Update LoadConfiguration to parse CLI arguments**

  Update `LoadConfiguration` to accept `string[] args`, load `appsettings.json` relative to `AppContext.BaseDirectory`, and override `agent:name` from arguments:
  ```csharp
  static IConfiguration LoadConfiguration(string[] args)
  {
      var aetherHome = Environment.GetEnvironmentVariable("AETHER_HOME")
          ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aether");
      var globalConfigPath = Path.Combine(aetherHome, "config.json");

      var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

      var builder = new ConfigurationBuilder()
          .AddJsonFile(appSettingsPath, optional: true, reloadOnChange: false)
          .AddJsonFile(globalConfigPath, optional: true, reloadOnChange: false)
          .AddEnvironmentVariables("AETHER_");

      var agentName = TuiArgs.ParseAgentName(args);
      if (!string.IsNullOrEmpty(agentName))
      {
          builder.AddInMemoryCollection(new[]
          {
              new KeyValuePair<string, string?>("agent:name", agentName)
          });
      }

      return builder.Build();
  }
  ```
  Update bootstrap call (around line 35):
  ```csharp
  var configuration = LoadConfiguration(args);
  ```

- [x] **Step 4: Update display names and userName checks**

  Update the user name resolver in `SendMessage` (around line 318) to replace `"default"` with `"Thoor"`:
  ```csharp
      // User message
      var userName = configuration["user:name"];
      if (string.IsNullOrEmpty(userName) || userName.Equals("default", StringComparison.OrdinalIgnoreCase))
      {
          userName = "Thoor";
      }
      AppendChat($"  {ts}  {userName}  \u2502  {message}");
  ```
  Update all references displaying the agent name to use `profile.DisplayName`:
  - Line 114:
    ```csharp
    var headerLabel = new Label($" Aether \u00b7 {profile.DisplayName}")
    ```
  - Line 200 (in `UpdateScrollIndicator`):
    ```csharp
    statusLabel.Text = $" \u25cf Aether \u00b7 {profile.DisplayName}{scrollInfo}";
    ```
  - Line 408 (welcome message):
    ```csharp
    AppendChat($"  \u2502  Connected to agent: {profile.DisplayName}");
    ```

- [x] **Step 5: Fix input prompt background leakage**

  Specify all four color properties for `inputPrompt` (around line 153):
  ```csharp
  var schemeInputPrompt = new ColorScheme
  {
      Normal    = new Terminal.Gui.Attribute(ACCENT, INPUT_BG),
      HotNormal = new Terminal.Gui.Attribute(ACCENT, INPUT_BG),
      Focus     = new Terminal.Gui.Attribute(ACCENT, INPUT_BG),
      HotFocus  = new Terminal.Gui.Attribute(ACCENT, INPUT_BG),
  };

  var inputPrompt = new Label(" \u276f ")
  {
      X = 0, Y = Pos.Bottom(separator) + 1,
      Width = 2,
      Height = 1,
      ColorScheme = schemeInputPrompt,
  };
  ```

- [x] **Step 6: Handle invalid agent configuration gracefully**

  Add validation in C# TUI startup when resolving `profile` (around line 41):
  ```csharp
  AgentProfile profile;
  try
  {
      profile = provider.GetRequiredService<AgentProfile>();
  }
  catch (Exception ex)
  {
      Console.ForegroundColor = ConsoleColor.Red;
      var requestedAgent = configuration["agent:name"] ?? "default";
      Console.Error.WriteLine($"Agent '{requestedAgent}' is not configured or enabled");
      Console.ResetColor();
      Environment.Exit(1);
      return;
  }
  ```

- [x] **Step 7: Run app locally and verify**

  Run: `./aether-tui.sh --agent aura`
  Expected: Starts up, displays `Aether · aura` in the header, loads the `aura` agent, displays `Thoor` when chatting, and uses a sleek dark mode.

  Run: `./aether-tui.sh --agent invalidagent`
  Expected: Prints "Agent 'invalidagent' is not configured or enabled" to standard error and exits with code 1.

- [x] **Step 8: Commit**

  Run:
  ```bash
  git add src/Aether.Tui/Program.cs
  git commit -m "feat(tui): C# TUI startup improvements, dynamic agent, and dark mode background fixes"
  ```

---

### Task 4: Support `--agent` in Rust Client

**Files:**
- Modify: `clients/aether-tui/src/main.rs`

- [x] **Step 1: Add `--agent` / `-a` arguments to Args struct**

  Open `clients/aether-tui/src/main.rs` and update the `Args` struct (around line 21) to define `agent` and make it map to `group`:
  ```rust
  #[derive(Parser)]
  #[command(name = "aether-tui", about = "Terminal UI for Aether AI")]
  struct Args {
      /// WebSocket URL override (e.g. ws://localhost:5099/ws)
      #[arg(long)]
      url: Option<String>,

      /// Agent group to connect to (legacy flag)
      #[arg(long, default_value = "maria")]
      group: String,

      /// Agent name to connect to (alias for group)
      #[arg(long, short = 'a')]
      agent: Option<String>,
  }
  ```

- [x] **Step 2: Map parsed agent argument to group**

  In `main()` (around line 33), map `args.agent` to `args.group` if specified:
  ```rust
      let mut args = Args::parse();
      if let Some(agent) = args.agent {
          args.group = agent;
      }
      let config = Config::resolve(args.url.clone(), args.group.clone());
  ```

- [x] **Step 3: Run Rust client locally and verify**

  Run: `./clients/aether-tui/tui.sh --build --agent aura`
  Expected: Compiles and starts the Rust TUI, connecting to Aura.

- [x] **Step 4: Commit**

  Run:
  ```bash
  git add clients/aether-tui/src/main.rs
  git commit -m "feat(tui-rs): add --agent/-a flag support to Rust client"
  ```

---

### Task 5: Compilation and Installation in aether-update.sh

**Files:**
- Modify: `aether-update.sh`

- [x] **Step 1: Support compilation and global installation**

  Open `aether-update.sh` and update it to accept `--install` argument, build release packages for both C# and Rust clients, and install a symlink to `~/.local/bin/aether-tui`:
  ```bash
  #!/usr/bin/env bash
  set -euo pipefail

  SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
  LABEL="com.thoor.aether"
  PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

  INSTALL=false
  while [[ $# -gt 0 ]]; do
      case "$1" in
          --install) INSTALL=true; shift ;;
          *) echo "Unknown argument: $1"; exit 1 ;;
      esac
  done

  echo "🔨 Building Aether Server..."
  dotnet build "$SCRIPT_DIR/src/Aether/Aether.csproj" -c Release --verbosity quiet

  if [ -f "$PLIST" ]; then
      echo "♻️  Restarting service..."
      launchctl unload "$PLIST" 2>/dev/null || true
      sleep 1
      launchctl load "$PLIST"
  else
      echo "⚠️  LaunchAgent plist not found at $PLIST. Skipping service restart."
  fi

  sleep 3

  # Check status
  PID=$(launchctl list | grep "$LABEL" | awk '{print $1}')
  STATUS=$(launchctl list | grep "$LABEL" | awk '{print $2}')

  if [ "$STATUS" = "0" ] && [ "$PID" != "-" ]; then
      echo "✅ Aether running (PID: $PID)"
  else
      echo "❌ Aether failed to start. Check logs:"
      echo "   tail -20 ~/.aether/logs/aether.stderr.log"
      exit 1
  fi

  if [ "$INSTALL" = true ]; then
      echo "📦 Compiling C# TUI in Release mode..."
      dotnet publish "$SCRIPT_DIR/src/Aether.Tui/Aether.Tui.csproj" -c Release -o "$SCRIPT_DIR/src/Aether.Tui/bin/Release/net8.0/publish/" --no-self-contained --verbosity quiet

      echo "📦 Compiling Rust TUI in Release mode..."
      cd "$SCRIPT_DIR/clients/aether-tui"
      ~/.cargo/bin/cargo build --release --quiet

      echo "🔗 Creating global symlink at ~/.local/bin/aether-tui..."
      mkdir -p "$HOME/.local/bin"

      # Write wrapper script at ~/.local/bin/aether-tui
      cat << EOF > "$HOME/.local/bin/aether-tui"
  #!/usr/bin/env bash
  set -euo pipefail
  # Get repository root
  REPO_DIR="$SCRIPT_DIR"
  exec "\$REPO_DIR/src/Aether.Tui/bin/Release/net8.0/publish/Aether.Tui" "\$@"
  EOF
      chmod +x "$HOME/.local/bin/aether-tui"
      echo "✅ Installed aether-tui wrapper to ~/.local/bin/aether-tui"

      ln -sf "$SCRIPT_DIR/clients/aether-tui/target/release/aether-tui" "$HOME/.local/bin/aether-tui-rs"
      echo "✅ Installed Rust TUI symlink to ~/.local/bin/aether-tui-rs"
  fi
  ```

- [x] **Step 2: Verify installation**

  Run: `./aether-update.sh --install`
  Expected: Compiles both C# and Rust, creates wrapper executable at `~/.local/bin/aether-tui`.

  Run from home folder: `~/.local/bin/aether-tui --agent aura`
  Expected: Successfully connects to the Aura agent with local database resolution working perfectly.

- [x] **Step 3: Commit**

  Run:
  ```bash
  git add aether-update.sh
  git commit -m "feat(install): support --install in aether-update.sh to package and install TUI globally"
  ```

---

## Verification

- [ ] Running `aether-tui --agent aura` from an arbitrary directory correctly connects to Aura.
- [ ] Running `aether-tui --agent default` connects to Maria and displays "Maria" in the header and status bar, and "Thoor" as the sender when chatting.
- [ ] Running `aether-tui --agent invalid` exits gracefully with "Agent 'invalid' is not configured or enabled".
- [ ] Running `./clients/aether-tui/tui.sh --agent aura` launches Rust client connected to Aura.
- [ ] All unit and integration tests are passing green.
