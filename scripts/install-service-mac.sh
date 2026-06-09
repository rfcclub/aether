#!/usr/bin/env bash
# Install Aether as a macOS LaunchAgent (persistent background service)
# Target: macOS ARM64 (Mac MINI M-series)
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
AETHER_HOME="${AETHER_HOME:-$HOME/.aether}"
RELEASE_DIR="${REPO_DIR}/releases/mac-arm64"
INSTALL_DIR="/usr/local/lib/aether"
BINARY_DST="${INSTALL_DIR}/Aether"
BIN_LINK="/usr/local/bin/aether"
PLIST_NAME="io.rfcclub.aether"
PLIST_SRC="${SCRIPT_DIR}/${PLIST_NAME}.plist"
LAUNCH_AGENTS_DIR="$HOME/Library/LaunchAgents"
PLIST_DST="${LAUNCH_AGENTS_DIR}/${PLIST_NAME}.plist"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

log()  { echo -e "${GREEN}[aether]${NC} $*"; }
warn() { echo -e "${YELLOW}[aether]${NC} $*"; }
err()  { echo -e "${RED}[aether]${NC} $*"; exit 1; }
info() { echo -e "${CYAN}[aether]${NC} $*"; }

# ─── Install ───────────────────────────────────────────────────────────────────
install() {
    info "=== Aether macOS Service Installer ==="
    echo ""

    # Check binary exists
    if [ ! -f "${RELEASE_DIR}/Aether" ]; then
        err "Binary not found at ${RELEASE_DIR}/Aether
Build first with:
  cd ${REPO_DIR}
  dotnet publish src/Aether/Aether.csproj -c Release -r osx-arm64 --self-contained true /p:PublishSingleFile=true -o releases/mac-arm64"
    fi

    # Check plist exists
    if [ ! -f "$PLIST_SRC" ]; then
        err "Plist not found at ${PLIST_SRC}"
    fi

    # 1. Install to /usr/local/lib/aether/ (keep dylib alongside binary)
    log "Installing release dir → ${INSTALL_DIR}"
    sudo mkdir -p "$INSTALL_DIR"
    sudo cp -R "${RELEASE_DIR}/" "${INSTALL_DIR}/"
    sudo chmod -R 755 "$INSTALL_DIR"
    sudo chmod 644 "${INSTALL_DIR}/libe_sqlite3.dylib" 2>/dev/null || true
    # Symlink into PATH
    sudo ln -sf "$BINARY_DST" "$BIN_LINK"
    log "Binary: ${BINARY_DST} ($(du -sh "$BINARY_DST" | cut -f1))"

    # 2. Create required directories
    log "Creating Aether home directory: ${AETHER_HOME}"
    mkdir -p "${AETHER_HOME}/logs"
    mkdir -p "${AETHER_HOME}/store"
    mkdir -p "${AETHER_HOME}/agents"
    mkdir -p "${AETHER_HOME}/workspaces"
    mkdir -p "$LAUNCH_AGENTS_DIR"

    # 3. Install plist (with correct username substituted)
    log "Installing LaunchAgent plist → ${PLIST_DST}"
    # Substitute actual home dir and username in plist
    sed \
        -e "s|/Users/thoor|${HOME}|g" \
        -e "s|<string>thoor</string>|<string>$(whoami)</string>|g" \
        -e "s|/usr/local/bin/aether|${BINARY_DST}|g" \
        "$PLIST_SRC" > "$PLIST_DST"
    chmod 644 "$PLIST_DST"

    # 4. Load the service
    log "Loading LaunchAgent..."
    # Unload first if already loaded (ignore errors)
    launchctl unload "$PLIST_DST" 2>/dev/null || true
    launchctl load "$PLIST_DST"

    sleep 2

    # 5. Check status
    if launchctl list | grep -q "$PLIST_NAME"; then
        local pid
        pid=$(launchctl list | grep "$PLIST_NAME" | awk '{print $1}')
        if [ "$pid" != "-" ] && [ -n "$pid" ]; then
            log "✅ Aether service started successfully (PID: ${pid})"
        else
            warn "Service loaded but may not be running yet (check logs below)"
        fi
    else
        warn "Service may not be loaded. Check:"
        echo "  launchctl list | grep aether"
    fi

    echo ""
    info "=== Service Info ==="
    echo "  Binary:    ${BINARY_DST}"
    echo "  Plist:     ${PLIST_DST}"
    echo "  Logs:      ${AETHER_HOME}/logs/aether.stdout.log"
    echo "  Errors:    ${AETHER_HOME}/logs/aether.stderr.log"
    echo ""
    info "=== Commands ==="
    echo "  Status:    $0 status"
    echo "  Logs:      $0 logs"
    echo "  Stop:      $0 stop"
    echo "  Restart:   $0 restart"
    echo "  Uninstall: $0 uninstall"
}

# ─── Uninstall ─────────────────────────────────────────────────────────────────
uninstall() {
    log "Stopping and removing Aether service..."
    launchctl unload "$PLIST_DST" 2>/dev/null || true
    rm -f "$PLIST_DST"
    sudo rm -f "$BIN_LINK"
    sudo rm -rf "$INSTALL_DIR"
    log "Aether service removed."
    warn "Data at ${AETHER_HOME} was NOT deleted. Remove manually if needed."
}

# ─── Status ────────────────────────────────────────────────────────────────────
status() {
    echo ""
    info "=== Aether Service Status ==="
    if launchctl list | grep -q "$PLIST_NAME"; then
        local pid exit_code
        pid=$(launchctl list | grep "$PLIST_NAME" | awk '{print $1}')
        exit_code=$(launchctl list | grep "$PLIST_NAME" | awk '{print $2}')
        if [ "$pid" != "-" ] && [ -n "$pid" ]; then
            log "✅ RUNNING  (PID: ${pid})"
        else
            warn "⚠️  STOPPED  (last exit code: ${exit_code})"
        fi
        echo ""
        echo "  Plist:   ${PLIST_DST}"
        echo "  Binary:  $(which aether 2>/dev/null || echo 'not in PATH')"
    else
        warn "Service not loaded. Install with: $0 install"
    fi
    echo ""
}

# ─── Logs ──────────────────────────────────────────────────────────────────────
logs() {
    local log_file="${AETHER_HOME}/logs/aether.stdout.log"
    local err_file="${AETHER_HOME}/logs/aether.stderr.log"
    if [ "${1:-}" = "err" ]; then
        log "Following stderr: ${err_file}"
        tail -f "$err_file"
    else
        log "Following stdout: ${log_file}"
        tail -f "$log_file"
    fi
}

# ─── Stop / Start / Restart ────────────────────────────────────────────────────
stop() {
    launchctl unload "$PLIST_DST" 2>/dev/null && log "Service stopped." || warn "Service was not running."
}

start() {
    launchctl load "$PLIST_DST" && log "Service started." || err "Failed to start service."
}

restart() {
    log "Restarting Aether service..."
    launchctl unload "$PLIST_DST" 2>/dev/null || true
    sleep 1
    launchctl load "$PLIST_DST"
    log "Service restarted."
    sleep 2
    status
}

# ─── Update (deploy new binary) ────────────────────────────────────────────────
update() {
    if [ ! -f "${RELEASE_DIR}/Aether" ]; then
        err "Binary not found at ${RELEASE_DIR}/Aether. Build first."
    fi
    log "Updating Aether install dir..."
    launchctl unload "$PLIST_DST" 2>/dev/null || true
    sudo cp -R "${RELEASE_DIR}/" "${INSTALL_DIR}/"
    sudo chmod -R 755 "$INSTALL_DIR"
    sudo chmod 644 "${INSTALL_DIR}/libe_sqlite3.dylib" 2>/dev/null || true
    sudo ln -sf "$BINARY_DST" "$BIN_LINK"
    launchctl load "$PLIST_DST"
    log "Updated to: $(aether --version 2>/dev/null || echo 'unknown version')"
    sleep 2
    status
}

# ─── Router ────────────────────────────────────────────────────────────────────
case "${1:-help}" in
    install)   install ;;
    uninstall) uninstall ;;
    status)    status ;;
    logs)      logs "${2:-}" ;;
    stop)      stop ;;
    start)     start ;;
    restart)   restart ;;
    update)    update ;;
    help|*)
        echo ""
        echo "  Usage: $0 <command>"
        echo ""
        echo "  Commands:"
        echo "    install    Install Aether to /usr/local/lib/aether + LaunchAgent (auto-start)"
        echo "    uninstall  Remove service, binary and install dir"
        echo "    status     Show service status"
        echo "    logs       Follow stdout logs (logs err → stderr)"
        echo "    stop       Stop the service"
        echo "    start      Start the service"
        echo "    restart    Restart the service"
        echo "    update     Sync new release dir without reinstalling plist"
        echo ""
        ;;
esac
