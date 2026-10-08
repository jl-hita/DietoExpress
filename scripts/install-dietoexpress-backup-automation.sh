#!/usr/bin/env bash
set -euo pipefail

APP_DIR="${DIETOEXPRESS_APP_DIR:-/opt/dietoexpress}"
SYSTEMD_DIR="/etc/systemd/system"
SUDOERS_FILE="/etc/sudoers.d/dietoexpress-restore"

install -m 0644 "$APP_DIR/scripts/systemd/dietoexpress-backup.service" "$SYSTEMD_DIR/dietoexpress-backup.service"
install -m 0644 "$APP_DIR/scripts/systemd/dietoexpress-backup.timer" "$SYSTEMD_DIR/dietoexpress-backup.timer"

cat > "$SUDOERS_FILE" <<EOF
# Solo permite al usuario del servicio DietoExpress ejecutar el wrapper de restauración.
joso ALL=(root) NOPASSWD: /opt/dietoexpress/scripts/dietoexpress-restore-web.sh *
EOF
chmod 0440 "$SUDOERS_FILE"
visudo -cf "$SUDOERS_FILE"

systemctl daemon-reload
systemctl enable --now dietoexpress-backup.timer

echo "Automatización de backups instalada."
systemctl is-enabled dietoexpress-backup.timer
systemctl is-active dietoexpress-backup.timer
