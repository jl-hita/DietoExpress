#!/usr/bin/env bash
set -euo pipefail

# Este helper se instala como root en /usr/local/sbin y es el único punto privilegiado
# que puede ejecutar pg_restore. El código del despliegue nunca se ejecuta como root.
[[ "$(id -u)" -eq 0 ]] || {
  echo "ERROR: este helper debe ejecutarse como root." >&2
  exit 1
}

BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
DATABASE_NAME="${DIETOEXPRESS_DATABASE:-}"
DATABASE_URL="${DIETOEXPRESS_DATABASE_URL:-}"
ENV_FILE="${DIETOEXPRESS_ENV_FILE:-/etc/dietoexpress/dietoexpress.env}"

if [[ -r "$ENV_FILE" ]]; then
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-$BACKUP_DIR}"
  DATABASE_NAME="${DIETOEXPRESS_DATABASE:-$DATABASE_NAME}"
  DATABASE_URL="${DIETOEXPRESS_DATABASE_URL:-$DATABASE_URL}"
fi

[[ -n "$BACKUP_DIR" ]] || {
  echo "ERROR: DIETOEXPRESS_BACKUP_DIR no está configurado." >&2
  exit 1
}

[[ -n "$DATABASE_NAME" ]] || {
  echo "ERROR: DIETOEXPRESS_DATABASE debe estar configurado para la restauración privilegiada." >&2
  exit 1
}

mode=""
file=""
confirm=false
while [[ $# -gt 0 ]]; do
  case "$1" in
    --restore) mode=restore; file="${2:-}"; shift 2 ;;
    --confirm) confirm=true; shift ;;
    *) echo "ERROR: argumento desconocido: $1" >&2; exit 2 ;;
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

source_file="$BACKUP_DIR/$file"
[[ -f "$source_file" ]] || {
  echo "ERROR: backup no encontrado: $source_file" >&2
  exit 1
}

tmp_file="$(mktemp "$BACKUP_DIR/.restore-postgres-XXXXXX.dump")"
cleanup() {
  rm -f -- "$tmp_file"
}
trap cleanup EXIT

# Los dumps operativos son 0600 de joso. Copiamos temporalmente el archivo con
# propietario postgres para que pg_restore pueda ejecutarse como el superusuario
# del motor sin abrir permisos del backup permanente.
install -o postgres -g postgres -m 0600 "$source_file" "$tmp_file"

echo "Ejecutando pg_restore como usuario PostgreSQL postgres..."
runuser -u postgres -- /usr/bin/pg_restore   --clean   --if-exists   --no-owner   --exit-on-error   --dbname="$DATABASE_NAME"   "$tmp_file"

echo "pg_restore privilegiado completado correctamente."
