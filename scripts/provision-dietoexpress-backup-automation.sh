#!/usr/bin/env bash
set -euo pipefail

# Ejecutado como root, de forma idempotente. Los despliegues de GitHub Actions
# no ejecutan como root código que reside en /opt/dietoexpress.
[[ "$(id -u)" -eq 0 ]] || { echo "ERROR: este provisioning debe ejecutarse como root." >&2; exit 1; }
command -v rsync >/dev/null 2>&1 || { echo "ERROR: rsync es obligatorio para snapshots documentales incrementales; instálalo antes del provisioning." >&2; exit 1; }
RCLONE_BIN="$(command -v rclone)" || { echo "ERROR: rclone es obligatorio para la copia cifrada a Google Drive; instálalo antes del provisioning." >&2; exit 1; }
RCLONE_CONFIG=/home/joso/.config/rclone/rclone.conf
[[ -f "$RCLONE_CONFIG" && ! -L "$RCLONE_CONFIG" ]] || { echo "ERROR: falta la configuración rclone de joso en $RCLONE_CONFIG." >&2; exit 1; }
[[ "$(stat -c '%U:%G:%a' "$RCLONE_CONFIG")" == "joso:joso:600" ]] || { echo "ERROR: $RCLONE_CONFIG debe pertenecer a joso:joso y tener modo 0600." >&2; exit 1; }
if ! runuser -u joso -- env RCLONE_CONFIG="$RCLONE_CONFIG" "$RCLONE_BIN" listremotes | grep -Fxq 'gdrive-crypt:'; then
  echo "ERROR: no se encuentra el remote cifrado gdrive-crypt: en la configuración de rclone de joso." >&2
  exit 1
fi

APP_DIR="${DIETOEXPRESS_APP_DIR:-/opt/dietoexpress}"
SYSTEMD_DIR=/etc/systemd/system
ROOT_SBIN=/usr/local/sbin
SUDOERS_FILE=/etc/sudoers.d/dietoexpress-restore
ENV_FILE=/etc/dietoexpress/dietoexpress.env

[[ -r "$ENV_FILE" ]] || { echo "ERROR: falta o no se puede leer $ENV_FILE." >&2; exit 1; }
# shellcheck disable=SC1090
source "$ENV_FILE"
BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
BACKUP_GROUP="${DIETOEXPRESS_BACKUP_GROUP:-joso}"
DRIVE_REMOTE="${DIETOEXPRESS_DRIVE_REMOTE:-gdrive-crypt:}"
[[ "$DRIVE_REMOTE" == gdrive-crypt:* ]] || { echo "ERROR: DIETOEXPRESS_DRIVE_REMOTE debe usar exclusivamente el remote cifrado gdrive-crypt:." >&2; exit 1; }

[[ -n "$BACKUP_DIR" && "$BACKUP_DIR" == /* && "$BACKUP_DIR" != "/" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR debe ser una ruta absoluta válida." >&2; exit 1; }
[[ "$(realpath -m -- "$BACKUP_DIR")" == "$BACKUP_DIR" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR debe estar normalizado y no usar enlaces simbólicos." >&2; exit 1; }
[[ -d "$(dirname "$BACKUP_DIR")" ]] || { echo "ERROR: el directorio padre del almacén de backups debe existir." >&2; exit 1; }
getent group "$BACKUP_GROUP" >/dev/null 2>&1 || { echo "ERROR: no existe el grupo $BACKUP_GROUP." >&2; exit 1; }

# /var/lib/dietoexpress lo puede escribir el servicio por el aviso de
# mantenimiento. No puede albergar archivos que posteriormente lea pg_restore
# como postgres con privilegios. Rechazamos rutas con ancestros escribibles por
# joso antes de tocar ningún helper instalado.
candidate="$BACKUP_DIR"
while [[ "$candidate" != "/" ]]; do
  [[ ! -L "$candidate" ]] || { echo "ERROR: no se permiten enlaces simbólicos en la ruta de backups: $candidate" >&2; exit 1; }
  candidate="$(dirname "$candidate")"
done
candidate="$(dirname "$BACKUP_DIR")"
while [[ "$candidate" != "/" ]]; do
  if runuser -u joso -- /usr/bin/test -w "$candidate"; then
    echo "ERROR: $candidate es modificable por joso. Migra las copias a /var/lib/dietoexpress-backups y actualiza DIETOEXPRESS_BACKUP_DIR en $ENV_FILE." >&2
    exit 1
  fi
  candidate="$(dirname "$candidate")"
done

for required in \
  "$APP_DIR/scripts/dietoexpress-backup.sh" \
  "$APP_DIR/scripts/dietoexpress-pg-backup-root.sh" \
  "$APP_DIR/scripts/dietoexpress-restore-user.sh" \
  "$APP_DIR/scripts/dietoexpress-pg-restore-root.sh" \
  "$APP_DIR/scripts/dietoexpress-restore-web.sh" \
  "$APP_DIR/scripts/dietoexpress-restore-post.sh" \
  "$APP_DIR/scripts/systemd/dietoexpress-backup.service" \
  "$APP_DIR/scripts/systemd/dietoexpress-backup.timer" \
  "$APP_DIR/scripts/dietoexpress-backup-google-drive.sh" \
  "$APP_DIR/scripts/systemd/dietoexpress-backup-drive.service" \
  "$APP_DIR/scripts/systemd/dietoexpress-backup-drive.timer" \
  "$APP_DIR/scripts/systemd/dietoexpress-restore@.service"; do
  [[ -f "$required" ]] || { echo "ERROR: falta el archivo requerido: $required" >&2; exit 1; }
done

install -d -o root -g "$BACKUP_GROUP" -m 0750 "$BACKUP_DIR"
if runuser -u joso -- /usr/bin/test -w "$BACKUP_DIR"; then
  echo "ERROR: joso no debe poder escribir en $BACKUP_DIR." >&2
  exit 1
fi
install -d -m 0755 "$ROOT_SBIN"
install -o root -g root -m 0750 "$APP_DIR/scripts/dietoexpress-restore-web.sh" "$ROOT_SBIN/dietoexpress-restore-web"
install -o root -g root -m 0750 "$APP_DIR/scripts/dietoexpress-pg-backup-root.sh" "$ROOT_SBIN/dietoexpress-pg-backup"
install -o root -g root -m 0750 "$APP_DIR/scripts/dietoexpress-pg-restore-root.sh" "$ROOT_SBIN/dietoexpress-pg-restore"
install -o root -g root -m 0750 "$APP_DIR/scripts/dietoexpress-restore-post.sh" "$ROOT_SBIN/dietoexpress-restore-post"

install -o root -g root -m 0755 "$APP_DIR/scripts/systemd/dietoexpress-restore@.service" "$SYSTEMD_DIR/dietoexpress-restore@.service"
install -o root -g root -m 0644 "$APP_DIR/scripts/systemd/dietoexpress-backup.service" "$SYSTEMD_DIR/dietoexpress-backup.service"
install -o root -g root -m 0644 "$APP_DIR/scripts/systemd/dietoexpress-backup.timer" "$SYSTEMD_DIR/dietoexpress-backup.timer"
install -o root -g "$BACKUP_GROUP" -m 0750 "$APP_DIR/scripts/dietoexpress-backup-google-drive.sh" "$ROOT_SBIN/dietoexpress-backup-google-drive"
install -o root -g root -m 0644 "$APP_DIR/scripts/systemd/dietoexpress-backup-drive.service" "$SYSTEMD_DIR/dietoexpress-backup-drive.service"
install -o root -g root -m 0644 "$APP_DIR/scripts/systemd/dietoexpress-backup-drive.timer" "$SYSTEMD_DIR/dietoexpress-backup-drive.timer"

cat > "$SUDOERS_FILE" <<'EOF'
# joso solo puede invocar el helper de backup con modos fijos y los helpers de
# restauración con nombres de backup validados. No acepta rutas arbitrarias.
joso ALL=(root) NOPASSWD: /usr/local/sbin/dietoexpress-pg-backup ^--backup$
joso ALL=(root) NOPASSWD: /usr/local/sbin/dietoexpress-pg-backup ^--backup-pre-restore$
joso ALL=(root) NOPASSWD: /usr/local/sbin/dietoexpress-restore-web ^dietoexpress-postgresql-[A-Za-z0-9_.-]+\.dump --confirm$
joso ALL=(root) NOPASSWD: /usr/local/sbin/dietoexpress-pg-restore ^--restore dietoexpress-postgresql-[A-Za-z0-9_.-]+\.dump --confirm$
EOF
chown root:root "$SUDOERS_FILE"
chmod 0440 "$SUDOERS_FILE"
visudo -cf "$SUDOERS_FILE"

systemctl daemon-reload
systemctl enable --now dietoexpress-backup.timer
systemctl enable --now dietoexpress-backup-drive.timer

echo "Provisioning de backups locales, copia cifrada a Drive y restauración completado."
systemctl is-enabled dietoexpress-backup.timer
systemctl is-active dietoexpress-backup.timer
systemctl is-enabled dietoexpress-backup-drive.timer
systemctl is-active dietoexpress-backup-drive.timer
