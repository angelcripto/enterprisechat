#!/usr/bin/env bash
#
# Instala EnterpriseChat.Server como systemd unit en /opt/enterprisechat.
# Necesita el binario ya publicado (dotnet publish -c Release -r linux-x64 --self-contained).
#
# Uso: sudo ./install-linux.sh <publish-dir>

set -euo pipefail

if [[ $EUID -ne 0 ]]; then
    echo "Este script necesita ejecutarse como root (sudo)." >&2
    exit 1
fi

PUBLISH_DIR="${1:-}"
if [[ -z "$PUBLISH_DIR" || ! -d "$PUBLISH_DIR" ]]; then
    echo "Indica el directorio publish: sudo ./install-linux.sh <publish-dir>" >&2
    exit 1
fi

INSTALL_DIR=/opt/enterprisechat
SERVICE_USER=enterprisechat
UNIT_NAME=enterprisechat.service

if ! id "$SERVICE_USER" &>/dev/null; then
    useradd --system --create-home --home-dir "$INSTALL_DIR" --shell /usr/sbin/nologin "$SERVICE_USER"
fi

mkdir -p "$INSTALL_DIR"
rsync -a --delete "$PUBLISH_DIR"/ "$INSTALL_DIR"/
mkdir -p "$INSTALL_DIR/data" "$INSTALL_DIR/logs" "$INSTALL_DIR/certs" "$INSTALL_DIR/plugins"
chown -R "$SERVICE_USER:$SERVICE_USER" "$INSTALL_DIR"
chmod 750 "$INSTALL_DIR/certs"

install -m 0644 "$(dirname "$0")/$UNIT_NAME" "/etc/systemd/system/$UNIT_NAME"
systemctl daemon-reload
systemctl enable --now "$UNIT_NAME"
systemctl status "$UNIT_NAME" --no-pager
