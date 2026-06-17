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

    echo "🚀 Writing launcher wrapper to ~/.local/bin/aether-tui..."
    cat << EOF > "$HOME/.local/bin/aether-tui"
#!/usr/bin/env bash
set -euo pipefail
# Get repository root
REPO_DIR="$SCRIPT_DIR"
exec "\$REPO_DIR/clients/aether-tui/tui.sh" "\$@"
EOF

    chmod +x "$HOME/.local/bin/aether-tui"

    ln -sf "$SCRIPT_DIR/clients/aether-tui/target/release/aether-tui" "$HOME/.local/bin/aether-tui-rs"
    echo "✅ Installed Rust TUI symlink to ~/.local/bin/aether-tui-rs"
    echo "✅ Global TUI installation complete! Run with: aether-tui"
fi
