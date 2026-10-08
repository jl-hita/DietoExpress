#!/usr/bin/env bash
set -euo pipefail

# Esta parte de la restauración se ejecuta siempre como joso. El control privilegiado
# (parar/arrancar el servicio y ejecutar pg_restore como postgres) queda fuera del árbol
# de despliegue, en archivos propiedad de root instalados por el provisioning inicial.
BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
DATABASE_URL="${DIETOEXPRESS_DATABASE_URL:-}"
DB_NAME="${DIETOEXPRESS_DATABASE:-}"
DB_USER="${DIETOEXPRESS_DB_USER:-}"
DB_HOST="${DIETOEXPRESS_DB_HOST:-127.0.0.1}"
DB_PORT="${DIETOEXPRESS_DB_PORT:-5432}"
BACKUP_SCRIPT="${DIETOEXPRESS_BACKUP_SCRIPT:-/opt/dietoexpress/scripts/dietoexpress-backup.sh}"
RESTORE_HELPER="${DIETOEXPRESS_RESTORE_HELPER:-/usr/local/sbin/dietoexpress-pg-restore}"
MAINTENANCE_FILE="${DIETOEXPRESS_MAINTENANCE_FILE:-/var/lib/dietoexpress/maintenance.json}"

clear_maintenance_notice() {
  rm -f -- "$MAINTENANCE_FILE"
}
trap clear_maintenance_notice EXIT

mode=""
file=""
confirm=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --restore) mode=restore; file="${2:-}"; shift 2 ;;
    --confirm) confirm=true; shift ;;
    *) echo "Argumento desconocido: $1" >&2; exit 2 ;;
  esac
done

[[ "$mode" == restore && -n "$file" && "$confirm" == true ]] || {
  echo "ERROR: restauración no autorizada." >&2
  exit 2
}

[[ "$file" != */* ]] || { echo "ERROR: nombre de backup no válido." >&2; exit 2; }
[[ "$file" == dietoexpress-postgresql-*.dump ]] || {
  echo "ERROR: nombre de backup no válido." >&2
  exit 2
}

[[ -n "$BACKUP_DIR" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR no está configurado." >&2; exit 1; }

path="$BACKUP_DIR/$file"
[[ -f "$path" ]] || { echo "ERROR: backup no encontrado: $path" >&2; exit 1; }

manifest="${path%.dump}.sha256"
[[ -f "$manifest" ]] || { echo "ERROR: falta el checksum $manifest" >&2; exit 1; }
(cd "$BACKUP_DIR" && sha256sum -c "$(basename "$manifest")")
pg_restore --list "$path" >/dev/null

[[ -x "$BACKUP_SCRIPT" ]] || {
  echo "ERROR: no existe el script de backup: $BACKUP_SCRIPT" >&2
  exit 1
}
[[ -x "$RESTORE_HELPER" ]] || {
  echo "ERROR: no existe el helper privilegiado de restauración: $RESTORE_HELPER. Ejecuta de nuevo el provisioning como root." >&2
  exit 1
}

pre_restore_output="$("$BACKUP_SCRIPT")"
pre_restore_file="$(printf '%s\n' "$pre_restore_output" | sed -n 's/^Backup creado: //p' | tail -n1)"
[[ -n "$pre_restore_file" && -f "$pre_restore_file" ]] || {
  echo "ERROR: no se pudo crear el backup pre-restauración." >&2
  exit 1
}

restore_ok=false
set +e
sudo -n "$RESTORE_HELPER" --restore "$file" --confirm
rc=$?
set -e

if [[ $rc -eq 0 ]]; then
  restore_ok=true
else
  echo "ERROR: pg_restore terminó con código $rc. Intentando rollback..." >&2
fi

if [[ "$restore_ok" != true ]]; then
  set +e
  pre_restore_name="$(basename "$pre_restore_file")"
  sudo -n "$RESTORE_HELPER" --restore "$pre_restore_name" --confirm
  rollback_rc=$?
  set -e

  if [[ $rollback_rc -ne 0 ]]; then
    echo "CRITICAL: restauración y rollback han fallado (rollback=$rollback_rc)." >&2
    exit 1
  fi

  echo "Rollback completado correctamente."
  exit 1
fi

echo "Restauración PostgreSQL completada correctamente."
