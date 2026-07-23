#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
export AETHER_HOME="${AETHER_HOME:-$HOME/.aether}"

# Ensure working directory exists
mkdir -p "$AETHER_HOME"

cd "$SCRIPT_DIR"

# First run: launch interactive wizard
if [ ! -f "$AETHER_HOME/config.json" ]; then
    echo "╔══════════════════════════════════════╗"
    echo "║   First run — Aether setup wizard   ║"
    echo "╚══════════════════════════════════════╝"
    echo ""
    exec dotnet run --project "$SCRIPT_DIR/src/Aether/Aether.csproj" -- "$@"
fi

# Clean up stale port before serve
if [ "${1:-}" = "serve" ]; then
    PID=$(lsof -t -i :5099 || true)
    if [[ -n "$PID" ]]; then
        kill -9 $PID 2>/dev/null || true
    fi
fi

exec dotnet run --project "$SCRIPT_DIR/src/Aether/Aether.csproj" -- "$@"
