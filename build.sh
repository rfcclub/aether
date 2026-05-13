#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PUBLISH_DIR="/tmp/aether-publish"
RELEASE_BIN="/home/thoor/repo/aether/releases/latest/Aether"
SERVICE="aether.service"

echo "=== 1/5 Clean ==="
cd "$SCRIPT_DIR"
rm -rf src/Aether/bin src/Aether/obj

echo "=== 2/5 Build ==="
dotnet build

echo "=== 3/5 Test ==="
dotnet test tests/Aether.Tests/Aether.Tests.csproj

echo "=== 4/5 Publish ==="
rm -rf "$PUBLISH_DIR"
dotnet publish src/Aether/Aether.csproj -c Release -r linux-x64 --self-contained true -o "$PUBLISH_DIR"

echo "=== 5/5 Deploy ==="
echo "Binary: $(ls -lh "$PUBLISH_DIR/Aether" | awk '{print $5}')"
echo ""
echo "Run to deploy:"
echo "  sudo sh -c 'systemctl stop $SERVICE && cp $PUBLISH_DIR/Aether $RELEASE_BIN && systemctl start $SERVICE'"
echo ""
echo "Or deploy now? (sudo password required)"
read -rp "Deploy now? [y/N] " answer
if [[ "$answer" =~ ^[Yy]$ ]]; then
    sudo sh -c "systemctl stop $SERVICE && cp $PUBLISH_DIR/Aether $RELEASE_BIN && systemctl start $SERVICE"
    echo "Deployed. Checking status..."
    systemctl status "$SERVICE" --no-pager | head -10
fi
