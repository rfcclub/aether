#!/usr/bin/env bash
# Install Aether as a WSL systemd service
set -euo pipefail

REPO_DIR="$(cd "$(dirname "$0")/.." && pwd)"
AETHER_HOME="${AETHER_HOME:-$HOME/.aether}"
SERVICE_NAME="aether"
SERVICE_FILE="/etc/systemd/system/${SERVICE_NAME}.service"
BINARY="${REPO_DIR}/releases/latest/Aether"

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

log()  { echo -e "${GREEN}[aether-service]${NC} $*"; }
warn() { echo -e "${YELLOW}[aether-service]${NC} $*"; }
err()  { echo -e "${RED}[aether-service]${NC} $*"; }

needs_sudo() {
    if [ "$EUID" -ne 0 ]; then
        echo ""
        echo "This command needs sudo to manage systemd services."
        echo "Run: sudo $0 $*"
        exit 1
    fi
}

install() {
    needs_sudo install

    if ! systemctl --version &>/dev/null; then
        err "systemd not available. WSL2 with systemd required."
        err "Enable in /etc/wsl.conf: [boot] systemd=true"
        exit 1
    fi

    if [ ! -f "$BINARY" ]; then
        warn "Binary not found at $BINARY"
        warn "Build first: dotnet publish -c Release -o releases/latest src/Aether/Aether.csproj"
        exit 1
    fi

    log "Installing Aether systemd service..."

    cat > "$SERVICE_FILE" << SERVICEEOF
[Unit]
Description=Aether Agent Framework
Documentation=https://github.com/rfcclub/aether
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
User=${SUDO_USER:-$USER}
WorkingDirectory=${REPO_DIR}
Environment=AETHER_HOME=${AETHER_HOME}
Environment=DOTNET_ENVIRONMENT=Production
ExecStart=${BINARY} serve
Restart=on-failure
RestartSec=10
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=default.target
SERVICEEOF

    systemctl daemon-reload
    systemctl enable "${SERVICE_NAME}.service"
    systemctl start "${SERVICE_NAME}.service"

    sleep 2
    if systemctl is-active --quiet "${SERVICE_NAME}.service"; then
        log "Aether service started successfully."
        echo ""
        systemctl status "${SERVICE_NAME}.service" --no-pager --lines=8
    else
        err "Service failed to start. Check logs:"
        echo "  journalctl -u ${SERVICE_NAME}.service -n 50"
        systemctl status "${SERVICE_NAME}.service" --no-pager --lines=8 || true
        exit 1
    fi
}

uninstall() {
    needs_sudo uninstall

    log "Stopping and removing Aether service..."
    systemctl stop "${SERVICE_NAME}.service" 2>/dev/null || true
    systemctl disable "${SERVICE_NAME}.service" 2>/dev/null || true
    rm -f "$SERVICE_FILE"
    systemctl daemon-reload
    log "Aether service removed."
}

status() {
    if systemctl is-active --quiet "${SERVICE_NAME}.service" 2>/dev/null; then
        systemctl status "${SERVICE_NAME}.service" --no-pager
    else
        warn "Aether service is not running."
        if [ -f "$SERVICE_FILE" ]; then
            echo "Service is installed but not active."
        else
            echo "Service is not installed. Run: $0 install"
        fi
    fi
}

logs() {
    journalctl -u "${SERVICE_NAME}.service" -f
}

restart() {
    needs_sudo restart
    systemctl restart "${SERVICE_NAME}.service"
    log "Service restarted."
}

case "${1:-}" in
    install)   install ;;
    uninstall) uninstall ;;
    status)    status ;;
    logs)      logs ;;
    restart)   restart ;;
    *)
        echo "Usage: $0 {install|uninstall|status|logs|restart}"
        echo ""
        echo "  install   — Install Aether as a systemd service (WSL auto-start)"
        echo "  uninstall — Remove the systemd service"
        echo "  status    — Show service status"
        echo "  logs      — Follow service logs (Ctrl+C to exit)"
        echo "  restart   — Restart the service"
        exit 1
        ;;
esac
