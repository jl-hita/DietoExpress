#!/usr/bin/env bash
set -euo pipefail
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH
umask 077

[[ "$(id -u)" -eq 0 ]] || { echo "ERROR: este helper debe ejecutarse como root." >&2; exit 1; }
[[ "$#" -eq 1 && "$1" == "--backup" ]] || { echo "ERROR: uso permitido: dietoexpress-pg-backup --backup" >&2; exit 2; }

ENV_FILE=/etc/dietoexpress/dietoexpress.env
[[ -r "$ENV_FILE" ]] || { echo "ERROR: no se puede leer $ENV_FILE." >&2; exit 1; }
# shellcheck disable=SC1090
source "$ENV_FILE"

BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
DATABASE_NAME="${DIETOEXPRESS_DATABASE:-}"
DB_PORT="${DIETOEXPRESS_DB_PORT:-5432}"
BACKUP_OWNER="${DIETOEXPRESS_BACKUP_OWNER:-joso}"
BACKUP_GROUP="${DIETOEXPRESS_BACKUP_GROUP:-$BACKUP_OWNER}"

[[ -n "$BACKUP_DIR" && "$BACKUP_DIR" == /* && "$BACKUP_DIR" != "/" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR debe ser una ruta absoluta válida." >&2; exit 1; }
[[ -n "$DATABASE_NAME" ]] || { echo "ERROR: DIETOEXPRESS_DATABASE debe estar configurado en $ENV_FILE." >&2; exit 1; }
[[ "$DB_PORT" =~ ^[0-9]+$ ]] && (( DB_PORT >= 1 && DB_PORT <= 65535 )) || { echo "ERROR: DIETOEXPRESS_DB_PORT no es válido." >&2; exit 1; }
id "$BACKUP_OWNER" >/dev/null 2>&1 || { echo "ERROR: no existe el usuario de backups $BACKUP_OWNER." >&2; exit 1; }
getent group "$BACKUP_GROUP" >/dev/null 2>&1 || { echo "ERROR: no existe el grupo de backups $BACKUP_GROUP." >&2; exit 1; }
[[ ! -L "$BACKUP_DIR" ]] || { echo "ERROR: el directorio de backups no puede ser un enlace simbólico." >&2; exit 1; }

install -d -o "$BACKUP_OWNER" -g "$BACKUP_GROUP" -m 0700 "$BACKUP_DIR"
[[ ! -L "$BACKUP_DIR" ]] || { echo "ERROR: el directorio de backups no puede ser un enlace simbólico." >&2; exit 1; }

# Backups y restauraciones comparten lock para impedir que pg_dump lea la base
# mientras pg_restore la está modificando.
install -d -o root -g root -m 0755 /run/lock
exec 9>/run/lock/dietoexpress-postgresql.lock
flock 9

tmp_dir="$(mktemp -d /tmp/dietoexpress-pg-backup.XXXXXX)"
chown postgres:postgres "$tmp_dir"
chmod 0700 "$tmp_dir"

timestamp="$(date -u '+%Y%m%dT%H%M%SZ')"
host="$(hostname -f 2>/dev/null || hostname)"
file="dietoexpress-postgresql-$timestamp.dump"
manifest_name="dietoexpress-postgresql-$timestamp.sha256"
metadata_name="dietoexpress-postgresql-$timestamp.txt"
dump_path="$BACKUP_DIR/$file"
manifest_path="$BACKUP_DIR/$manifest_name"
metadata_path="$BACKUP_DIR/$metadata_name"
tmp_dump="$tmp_dir/$file"
committed=false

cleanup() {
  local rc=$?
  if [[ "$committed" != true ]]; then
    rm -f -- "$dump_path" "$manifest_path" "$metadata_path"
  fi
  rm -rf -- "$tmp_dir"
  return "$rc"
}
trap cleanup EXIT

for final_path in "$dump_path" "$manifest_path" "$metadata_path"; do
  [[ ! -e "$final_path" && ! -L "$final_path" ]] || { echo "ERROR: ya existe un artefacto de backup con timestamp $timestamp." >&2; exit 1; }
done

# No se usa --no-owner. pg_dump corre como PostgreSQL superuser y conserva
# la información de propietarios para la restauración.
runuser -u postgres -- /usr/bin/pg_dump \
  --format=custom \
  --username=postgres \
  --port="$DB_PORT" \
  --dbname="$DATABASE_NAME" \
  --file="$tmp_dump"

/usr/bin/pg_restore --list "$tmp_dump" >/dev/null
sha256="$(sha256sum "$tmp_dump" | awk '{print $1}')"
size_bytes="$(stat -c '%s' "$tmp_dump")"
printf '%s  %s\n' "$sha256" "$file" > "$tmp_dir/$manifest_name"
{
  printf 'created_at_utc=%s\n' "$timestamp"
  printf 'host=%s\n' "$host"
  printf 'backup=%s\n' "$dump_path"
  printf 'size_bytes=%s\n' "$size_bytes"
  printf 'sha256=%s\n' "$sha256"
} > "$tmp_dir/$metadata_name"

install -o "$BACKUP_OWNER" -g "$BACKUP_GROUP" -m 0600 "$tmp_dump" "$dump_path"
install -o "$BACKUP_OWNER" -g "$BACKUP_GROUP" -m 0600 "$tmp_dir/$manifest_name" "$manifest_path"
install -o "$BACKUP_OWNER" -g "$BACKUP_GROUP" -m 0600 "$tmp_dir/$metadata_name" "$metadata_path"
(cd "$BACKUP_DIR" && sha256sum -c "$manifest_name")
committed=true

echo "Backup creado: $dump_path"
echo "Checksum: $manifest_path"
