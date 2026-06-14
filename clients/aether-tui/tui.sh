#!/usr/bin/env bash
# tui.sh — Start Aether backend + launch aether-tui
# Usage: ./clients/aether-tui/tui.sh [--group <name>] [--url <ws-url>] [--build]
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
REPO_DIR="$(cd "$SCRIPT_DIR/../.." && pwd)"
BINARY="$SCRIPT_DIR/target/release/aether-tui"
AETHER_PORT="${AETHER_PORT:-5099}"
WS_PORT_KILL="${WS_PORT_KILL:-$AETHER_PORT}"

# ── colour helpers ────────────────────────────────────────────────────────────
RED='\033[0;31m'; YELLOW='\033[1;33m'; GREEN='\033[0;32m'; CYAN='\033[0;36m'; NC='\033[0m'
info()    { echo -e "${CYAN}▶${NC} $*"; }
success() { echo -e "${GREEN}✓${NC} $*"; }
warn()    { echo -e "${YELLOW}!${NC} $*"; }
die()     { echo -e "${RED}✗${NC} $*" >&2; exit 1; }

# ── parse args ────────────────────────────────────────────────────────────────
BUILD=false
TUI_ARGS=()
while [[ $# -gt 0 ]]; do
    case "$1" in
        --build) BUILD=true; shift ;;
        --group|--url) TUI_ARGS+=("$1" "$2"); shift 2 ;;
        *) TUI_ARGS+=("$1"); shift ;;
    esac
done

# ── (optional) build TUI binary ───────────────────────────────────────────────
if [[ "$BUILD" == true ]]; then
    info "Building aether-tui (release)..."
    cd "$SCRIPT_DIR"
    ~/.cargo/bin/cargo build --release 2>&1 | grep -E "^error|Compiling aether-tui|Finished|warning\[" || true
    success "Build complete"
fi

# ── verify binary exists ──────────────────────────────────────────────────────
[[ -f "$BINARY" ]] || die "Binary not found: $BINARY\n  Run:  ./clients/aether-tui/tui.sh --build"

# ── kill any process already on port 5099 ────────────────────────────────────
if fuser "$WS_PORT_KILL/tcp" &>/dev/null 2>&1; then
    warn "Killing existing process on port $WS_PORT_KILL..."
    fuser -k "$WS_PORT_KILL/tcp" 2>/dev/null || true
    sleep 0.5
    success "Port $WS_PORT_KILL cleared"
else
    info "Port $WS_PORT_KILL is free"
fi

# ── start Aether backend (background) ────────────────────────────────────────
LOGFILE="/tmp/aether-server.log"
info "Starting Aether backend (port $AETHER_PORT)..."
cd "$REPO_DIR"
dotnet run --project src/Aether/Aether.csproj -- serve > "$LOGFILE" 2>&1 &
SERVER_PID=$!

# wait up to 10s for WS port to open
READY=false
for i in $(seq 1 20); do
    sleep 0.5
    if fuser "$AETHER_PORT/tcp" &>/dev/null 2>&1; then
        READY=true; break
    fi
done

if [[ "$READY" != true ]]; then
    kill "$SERVER_PID" 2>/dev/null || true
    die "Aether backend failed to start in 10s.\n  Check logs: tail $LOGFILE"
fi
success "Aether backend running (PID $SERVER_PID, log: $LOGFILE)"

# ── trap: kill server on TUI exit ─────────────────────────────────────────────
cleanup() {
    info "Shutting down Aether backend (PID $SERVER_PID)..."
    kill "$SERVER_PID" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

# ── launch aether-tui ─────────────────────────────────────────────────────────
info "Launching aether-tui..."
echo ""
exec "$BINARY" "${TUI_ARGS[@]+"${TUI_ARGS[@]}"}"
