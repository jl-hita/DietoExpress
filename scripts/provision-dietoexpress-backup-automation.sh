#!/usr/bin/env bash
set -euo pipefail

# Ejecutado como root, de forma idempotente. Los despliegues de GitHub Actions
# no ejecutan como root código que reside en /opt/dietoexpress.
[[ "$(id -u)" -eq 0 ]] || { echo "ERROR: este provisioning debe ejecutarse como root." >&2; exit 1; }

APP_DIR="${DIETOEXPRESS_APP_DIR:-/opt/dietoexpress}"
SYSTEMD_DIR=/etc/systemd/system
ROOT_SBIN=/usr/local/sbin
SUDOERS_FILE=/etc/sudoers.d/dietoexpress-restore

for required in \
  "$APP_DIR/scripts/dietoexpress-backup.sh" \
  "$APP_DIR/scripts/dietoexpress-pg-backup-root.sh" \
  "$APP_DIR/scripts/dietoexpress-restore-user.sh" \
  "$APP_DIR/scripts/dietoexpress-pg-restore-root.sh" \
  "$APP_DIR/scripts/dietoexpress-restore-web.sh" \
  "$APP_DIR/scripts/dietoexpress-restore-post.sh" \
  "$APP_DIR/scripts/systemd/dietoexpress-backup.service" \
  "$APP_DIR/scripts/systemd/dietoexpress-backup.timer" \
  "$APP_DIR/scripts/systemd/dietoexpress-restore@.service"; do
  [[ -f "$required" ]] || { echo "ERROR: falta el archivo requerido: $required" >&2; exit 1; }
done

install -d -m 0755 "$ROOT_SBIN"
install -o root -g root -m 0750 "$APP_DIR/scripts/dietoexpress-restore-web.sh" "$ROOT_SBIN/dietoexpress-restore-web"
install -o root -g root -m 0750 "$APP_DIR/scripts/dietoexpress-pg-backup-root.sh" "$ROOT_SBIN/dietoexpress-pg-backup"
install -o root -g root -m 0750 "$APP_DIR/scripts/dietoexpress-pg-restore-root.sh" "$ROOT_SBIN/dietoexpress-pg-restore"
install -o root -g root -m 0750 "$APP_DIR/scripts/dietoexpress-restore-post.sh" "$ROOT_SBIN/dietoexpress-restore-post"

install -o root -g root -m 0755 "$APP_DIR/scripts/systemd/dietoexpress-restore@.service" "$SYSTEMD_DIR/dietoexpress-restore@.service"
install -o root -g root -m 0644 "$APP_DIR/scripts/systemd/dietoexpress-backup.service" "$SYSTEMD_DIR/dietoexpress-backup.service"
install -o root -g root -m 0644 "$APP_DIR/scripts/systemd/dietoexpress-backup.timer" "$SYSTEMD_DIR/dietoexpress-backup.timer"

cat > "$SUDOERS_FILE" <<'EOF'
# joso solo puede invocar el helper de backup con --backup y los helpers de
# restauración con nombres de backup validados.
joso ALL=(root) NOPASSWD: /usr/local/sbin/dietoexpress-pg-backup ^--backup$
joso ALL=(root) NOPASSWD: /usr/local/sbin/dietoexpress-restore-web ^dietoexpress-postgresql-[A-Za-z0-9_.-]+\.dump --confirm$
joso ALL=(root) NOPASSWD: /usr/local/sbin/dietoexpress-pg-restore ^--restore dietoexpress-postgresql-[A-Za-z0-9_.-]+\.dump --confirm$
EOF
chown root:root "$SUDOERS_FILE"
chmod 0440 "$SUDOERS_FILE"
visudo -cf "$SUDOERS_FILE"

systemctl daemon-reload
systemctl enable --now dietoexpress-backup.timer

echo "Provisioning de backups/restauración completado."
systemctl is-enabled dietoexpress-backup.timer
systemctl is-active dietoexpress-backup.timer
