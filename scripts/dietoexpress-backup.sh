#!/usr/bin/env bash
set -euo pipefail

# Orquestador sin privilegios. El helper root-owned ejecuta pg_dump como
# postgres y conserva el almacén de copias fuera de cualquier ruta modificable
# por el usuario de despliegue.
BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
DB_NAME="${DIETOEXPRESS_DATABASE:-}"
BACKUP_HELPER="${DIETOEXPRESS_BACKUP_HELPER:-/usr/local/sbin/dietoexpress-pg-backup}"
BACKUP_MODE="${DIETOEXPRESS_BACKUP_MODE:-normal}"

[[ -n "$BACKUP_DIR" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR no está configurado." >&2; exit 1; }
[[ -n "$DB_NAME" ]] || { echo "ERROR: DIETOEXPRESS_DATABASE es obligatorio para el backup privilegiado local." >&2; exit 1; }
[[ -f "$BACKUP_HELPER" ]] || { echo "ERROR: falta el helper privilegiado $BACKUP_HELPER. Ejecuta el provisioning como root." >&2; exit 1; }
[[ -d "$BACKUP_DIR" && ! -L "$BACKUP_DIR" && -r "$BACKUP_DIR" && -x "$BACKUP_DIR" ]] || {
  echo "ERROR: el almacén root-owned no existe o no es accesible. Ejecuta el provisioning y revisa DIETOEXPRESS_BACKUP_DIR." >&2
  exit 1
}
command -v sudo >/dev/null 2>&1 || { echo "ERROR: sudo no está disponible." >&2; exit 1; }

case "$BACKUP_MODE" in
  normal) helper_mode="--backup" ;;
  pre-restore) helper_mode="--backup-pre-restore" ;;
  *) echo "ERROR: DIETOEXPRESS_BACKUP_MODE debe ser normal o pre-restore." >&2; exit 1 ;;
esac

if ! backup_output="$(sudo -n "$BACKUP_HELPER" "$helper_mode")"; then
  echo "ERROR: el helper privilegiado de backup ha fallado. Revisa sudoers y journalctl." >&2
  exit 1
fi
printf '%s\n' "$backup_output"

output="$(printf '%s\n' "$backup_output" | sed -n 's/^Backup creado: //p' | tail -n1)"
[[ -n "$output" && -f "$output" && -r "$output" ]] || {
  echo "ERROR: el helper no devolvió una copia existente y legible." >&2
  exit 1
}
