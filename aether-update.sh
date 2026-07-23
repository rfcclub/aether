#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
LABEL="com.thoor.aether"
PLIST="$HOME/Library/LaunchAgents/$LABEL.plist"

INSTALL=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --install)
      INSTALL=true
      shift
      ;;
    *)
      echo "Unknown option: $1"
      exit 1
      ;;
  esac
done

echo "🔨 Building Aether..."
dotnet build "$SCRIPT_DIR/src/Aether/Aether.csproj" -c Release --verbosity quiet

# ── Generate launchd plist from template (macOS only) ────────────────────────
if [[ "$(uname)" == "Darwin" ]]; then
    AETHER_HOME="${AETHER_HOME:-$HOME/.aether}"
    PLIST_SRC="$SCRIPT_DIR/scripts/aether-daemon.plist"
    DLL_PATH="$SCRIPT_DIR/src/Aether/bin/Release/net10.0/Aether.dll"

        if [ -f "$PLIST_SRC" ] && [ -f "$DLL_PATH" ]; then
        mkdir -p "$(dirname "$PLIST")" "$AETHER_HOME/logs"
        BUILD_DIR="$(dirname "$DLL_PATH")"
        sed -e "s|__AETHER_DLL_PATH__|$DLL_PATH|g" \
            -e "s|__AETHER_HOME__|$AETHER_HOME|g" \
            -e "s|__AETHER_BUILD_DIR__|$BUILD_DIR|g" "$PLIST_SRC" > "$PLIST"
        echo "📝 Generated launchd plist: $PLIST"
    else
        echo "⚠️  Cannot generate plist (template or DLL missing). Skipping."
    fi
fi

if [ -f "$PLIST" ]; then
    echo "♻️  Restarting service..."
    launchctl unload "$PLIST" 2>/dev/null || true
    sleep 1
    launchctl load "$PLIST"

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
else
    echo "⚠️ Warning: LaunchAgent service plist not found at $PLIST. Skipping service restart."
fi

if [ "$INSTALL" = true ]; then
    echo "📦 Packaging and installing TUI globally..."

    echo "🔨 Compiling Rust TUI in release mode..."
    cd "$SCRIPT_DIR/clients/aether-tui"
    ~/.cargo/bin/cargo build --release --quiet

    echo "📁 Creating ~/.local/bin if not exists..."
    mkdir -p "$HOME/.local/bin"

    # ── aether-cli : TUI client launcher (start the interactive terminal UI) ──
    echo "🚀 Writing TUI client launcher to ~/.local/bin/aether-cli..."
    cat << EOF > "$HOME/.local/bin/aether-cli"
#!/usr/bin/env bash
set -euo pipefail
# aether-cli — launch the Aether TUI (interactive terminal client).
# Starts the backend server if port 5099 is not already listening, then runs the Rust TUI.
REPO_DIR="$SCRIPT_DIR"
exec "\$REPO_DIR/clients/aether-tui/tui.sh" "\$@"
EOF
    chmod +x "$HOME/.local/bin/aether-cli"

    # ── aether : server service launcher (start the Aether backend) ──────────────
    echo "🚀 Writing server launcher to ~/.local/bin/aether..."
    cat << EOF > "$HOME/.local/bin/aether"
#!/usr/bin/env bash
set -euo pipefail
# aether — launch the Aether server (backend service).
# Forwards all args to the .NET backend. Use \`aether serve\` to start the WS/Telegram server.
REPO_DIR="$SCRIPT_DIR"
exec dotnet run --project "\$REPO_DIR/src/Aether/Aether.csproj" -- "\$@"
EOF
    chmod +x "$HOME/.local/bin/aether"

    # Rust TUI binary symlink (direct, no backend auto-start)
    ln -sf "$SCRIPT_DIR/clients/aether-tui/target/release/aether-tui" "$HOME/.local/bin/aether-tui-rs"
    echo "✅ Installed Rust TUI binary symlink to ~/.local/bin/aether-tui-rs"
    echo "✅ Global install complete:"
    echo "   • aether-cli   → interactive TUI client (auto-starts backend)"
    echo "   • aether       → server service (dotnet run -- serve)"
    echo "   • aether-tui-rs → raw Rust binary (needs backend already running)"
fi
