#!/usr/bin/env bash
set -euo pipefail

# Restauración de producción controlada: nunca debe ejecutarse desde el proceso web.
BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
DATABASE_URL="${DIETOEXPRESS_DATABASE_URL:-}"
DB_NAME="${DIETOEXPRESS_DATABASE:-}"
DB_USER="${DIETOEXPRESS_DB_USER:-}"
DB_HOST="${DIETOEXPRESS_DB_HOST:-127.0.0.1}"
DB_PORT="${DIETOEXPRESS_DB_PORT:-5432}"
SERVICE="${DIETOEXPRESS_SERVICE_NAME:-dietoexpress.service}"
BACKUP_SCRIPT="${DIETOEXPRESS_BACKUP_SCRIPT:-/opt/dietoexpress/scripts/dietoexpress-backup.sh}"

usage() {
  cat <<USAGE
Usage:
  $0 --verify FILE
  $0 --restore FILE --confirm

La restauración verifica checksum y formato, crea un backup pre-restauración,
detiene DietoExpress, restaura el dump y vuelve a arrancar el servicio.
Si pg_restore falla, intenta recuperar automáticamente el backup anterior.
USAGE
}

mode=""
file=""
confirm=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --verify) mode=verify; file="${2:-}"; shift 2 ;;
    --restore) mode=restore; file="${2:-}"; shift 2 ;;
    --confirm) confirm=true; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Argumento desconocido: $1" >&2; usage >&2; exit 2 ;;
  esac
done

[[ -n "$mode" && -n "$file" ]] || { usage >&2; exit 2; }
[[ "$file" == /* ]] || file="$BACKUP_DIR/$file"
[[ -f "$file" ]] || { echo "ERROR: backup no encontrado: $file" >&2; exit 1; }
[[ "$(basename "$file")" == dietoexpress-postgresql-*.dump ]] || { echo "ERROR: nombre de backup no permitido." >&2; exit 1; }

manifest="${file%.dump}.sha256"
[[ -f "$manifest" ]] || { echo "ERROR: falta el checksum $manifest" >&2; exit 1; }
(cd "$(dirname "$file")" && sha256sum -c "$(basename "$manifest")")
pg_restore --list "$file" >/dev/null

echo "Backup verificado: $file"

if [[ "$mode" == verify ]]; then
  echo "Preflight correcto. No se ha modificado la base de datos."
  exit 0
fi

[[ "$confirm" == true ]] || { echo "ERROR: una restauración requiere --confirm." >&2; exit 2; }
[[ -x "$BACKUP_SCRIPT" ]] || { echo "ERROR: no existe el script de backup: $BACKUP_SCRIPT" >&2; exit 1; }

# El servicio se detiene antes de tocar la base para impedir nuevas transacciones.
sudo -n systemctl stop "$SERVICE"
started=false
cleanup() {
  if [[ "$started" == true ]]; then return; fi
  sudo -n systemctl start "$SERVICE" >/dev/null 2>&1 || true
}
trap cleanup EXIT

# Siempre conservamos el estado actual antes de sustituirlo.
pre_restore_output="$($BACKUP_SCRIPT)"
pre_restore_file="$(printf '%s\n' "$pre_restore_output" | sed -n 's/^Backup creado: //p' | tail -n1)"
[[ -n "$pre_restore_file" && -f "$pre_restore_file" ]] || { echo "ERROR: no se pudo crear el backup pre-restauración." >&2; exit 1; }

restore_ok=false
set +e
if [[ -n "$DATABASE_URL" ]]; then
  pg_restore --clean --if-exists --no-owner --exit-on-error --dbname="$DATABASE_URL" "$file"
  rc=$?
else
  pg_restore --clean --if-exists --no-owner --exit-on-error \
    --host="$DB_HOST" --port="$DB_PORT" --username="$DB_USER" --dbname="$DB_NAME" "$file"
  rc=$?
fi
set -e

if [[ $rc -eq 0 ]]; then
  restore_ok=true
else
  echo "ERROR: pg_restore terminó con código $rc. Intentando rollback..." >&2
fi

if [[ "$restore_ok" != true ]]; then
  set +e
  if [[ -n "$DATABASE_URL" ]]; then
    pg_restore --clean --if-exists --no-owner --exit-on-error --dbname="$DATABASE_URL" "$pre_restore_file"
    rollback_rc=$?
  else
    pg_restore --clean --if-exists --no-owner --exit-on-error \
      --host="$DB_HOST" --port="$DB_PORT" --username="$DB_USER" --dbname="$DB_NAME" "$pre_restore_file"
    rollback_rc=$?
  fi
  set -e
  if [[ $rollback_rc -ne 0 ]]; then
    echo "CRITICAL: restauración y rollback han fallado (rollback=$rollback_rc)." >&2
    exit 1
  fi
  echo "Rollback completado correctamente."
  exit 1
fi

sudo -n systemctl start "$SERVICE"
started=true
sudo -n systemctl is-active --quiet "$SERVICE"
echo "Restauración completada y servicio activo."
