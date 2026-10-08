#!/usr/bin/env bash
set -euo pipefail

# Esta parte de la restauración se ejecuta siempre como joso. El control privilegiado
# (parar/arrancar el servicio y crear la unidad systemd) queda fuera del árbol de
# despliegue, en archivos propiedad de root instalados por el provisioning inicial.
BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
DATABASE_URL="${DIETOEXPRESS_DATABASE_URL:-}"
DB_NAME="${DIETOEXPRESS_DATABASE:-}"
DB_USER="${DIETOEXPRESS_DB_USER:-}"
DB_HOST="${DIETOEXPRESS_DB_HOST:-127.0.0.1}"
DB_PORT="${DIETOEXPRESS_DB_PORT:-5432}"
BACKUP_SCRIPT="${DIETOEXPRESS_BACKUP_SCRIPT:-/opt/dietoexpress/scripts/dietoexpress-backup.sh}"

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

# El servicio ya ha sido detenido por la unidad systemd root antes de llegar aquí.
# El backup previo se genera como joso, igual que los backups normales.
pre_restore_output="$("$BACKUP_SCRIPT")"
pre_restore_file="$(printf '%s\n' "$pre_restore_output" | sed -n 's/^Backup creado: //p' | tail -n1)"
[[ -n "$pre_restore_file" && -f "$pre_restore_file" ]] || {
  echo "ERROR: no se pudo crear el backup pre-restauración." >&2
  exit 1
}

restore_ok=false
set +e
if [[ -n "$DATABASE_URL" ]]; then
  pg_restore --clean --if-exists --no-owner --exit-on-error --dbname="$DATABASE_URL" "$path"
  rc=$?
else
  pg_restore --clean --if-exists --no-owner --exit-on-error \
    --host="$DB_HOST" --port="$DB_PORT" --username="$DB_USER" --dbname="$DB_NAME" "$path"
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

echo "Restauración PostgreSQL completada correctamente."
