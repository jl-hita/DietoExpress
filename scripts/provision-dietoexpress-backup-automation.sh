#!/usr/bin/env bash
set -euo pipefail

# Este script se ejecuta UNA VEZ por un administrador con root. No debe invocarse
# automáticamente desde GitHub Actions: el árbol /opt/dietoexpress es modificable
# por el usuario de despliegue y nunca debe convertirse en código root por accidente.

[[ "$(id -u)" -eq 0 ]] || {
  echo "ERROR: este provisioning debe ejecutarse como root." >&2
  exit 1
}

APP_DIR="${DIETOEXPRESS_APP_DIR:-/opt/dietoexpress}"
SYSTEMD_DIR=/etc/systemd/system
ROOT_SBIN=/usr/local/sbin
ROOT_LIBEXEC=/usr/local/libexec
SUDOERS_FILE=/etc/sudoers.d/dietoexpress-restore

for required in \
  "$APP_DIR/scripts/dietoexpress-backup.sh" \
  "$APP_DIR/scripts/dietoexpress-restore-user.sh" \
  "$APP_DIR/scripts/systemd/dietoexpress-backup.service" \
  "$APP_DIR/scripts/systemd/dietoexpress-backup.timer"; do
  [[ -f "$required" ]] || {
    echo "ERROR: falta el archivo requerido: $required" >&2
    exit 1
  }
done

install -d -m 0755 "$ROOT_SBIN" "$ROOT_LIBEXEC"

# El wrapper y la unidad de restore son root-owned y quedan fuera de /opt.
install -o root -g root -m 0750 "$APP_DIR/scripts/dietoexpress-restore-web.sh" "$ROOT_SBIN/dietoexpress-restore-web"

# La implementación ejecutada durante el restore corre como joso. Así un despliegue
# normal puede actualizarla sin convertir código del deploy en código ejecutado como root.
install -o root -g root -m 0755 "$APP_DIR/scripts/systemd/dietoexpress-restore@.service" "$SYSTEMD_DIR/dietoexpress-restore@.service"

install -o root -g root -m 0644 "$APP_DIR/scripts/systemd/dietoexpress-backup.service" "$SYSTEMD_DIR/dietoexpress-backup.service"
install -o root -g root -m 0644 "$APP_DIR/scripts/systemd/dietoexpress-backup.timer" "$SYSTEMD_DIR/dietoexpress-backup.timer"

cat > "$SUDOERS_FILE" <<'EOF'
# El único comando root expuesto a joso para restauraciones es el wrapper root-owned.
joso ALL=(root) NOPASSWD: /usr/local/sbin/dietoexpress-restore-web ^dietoexpress-postgresql-[A-Za-z0-9_.-]+\.dump --confirm$
EOF
chown root:root "$SUDOERS_FILE"
chmod 0440 "$SUDOERS_FILE"
visudo -cf "$SUDOERS_FILE"

systemctl daemon-reload
systemctl enable --now dietoexpress-backup.timer

echo "Provisioning de backups completado."
systemctl is-enabled dietoexpress-backup.timer
systemctl is-active dietoexpress-backup.timer
