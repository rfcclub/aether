#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

exec dotnet run --project "$SCRIPT_DIR/src/Aether.Tui/Aether.Tui.csproj" -- "$@"
