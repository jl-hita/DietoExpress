#!/usr/bin/env bash
set -euo pipefail

# Backup operativo: falla de forma explícita si no se ha configurado un destino.
# Nunca escribe backups dentro del directorio de despliegue.

BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
DATABASE_URL="${DIETOEXPRESS_DATABASE_URL:-}"
DB_NAME="${DIETOEXPRESS_DATABASE:-}"
DB_USER="${DIETOEXPRESS_DB_USER:-}"
DB_HOST="${DIETOEXPRESS_DB_HOST:-127.0.0.1}"
DB_PORT="${DIETOEXPRESS_DB_PORT:-5432}"
INCLUDE_ENV="${DIETOEXPRESS_BACKUP_INCLUDE_ENV:-false}"
RETENTION="${DIETOEXPRESS_BACKUP_RETENTION:-8}"

if [[ -z "$BACKUP_DIR" ]]; then
  echo "ERROR: DIETOEXPRESS_BACKUP_DIR no está configurado." >&2
  exit 1
fi

if [[ -z "$DATABASE_URL" && -z "$DB_NAME" ]]; then
  echo "ERROR: configure DIETOEXPRESS_DATABASE_URL o DIETOEXPRESS_DATABASE." >&2
  exit 1
fi

umask 077
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

timestamp="$(date -u '+%Y%m%dT%H%M%SZ')"
host="$(hostname -f 2>/dev/null || hostname)"
output="$BACKUP_DIR/dietoexpress-postgresql-$timestamp.dump"
manifest="$BACKUP_DIR/dietoexpress-postgresql-$timestamp.sha256"
metadata="$BACKUP_DIR/dietoexpress-postgresql-$timestamp.txt"

if [[ -n "$DATABASE_URL" ]]; then
  pg_dump --format=custom --no-owner --file="$output" "$DATABASE_URL"
else
  pg_dump     --format=custom     --no-owner     --host="$DB_HOST"     --port="$DB_PORT"     --username="$DB_USER"     --file="$output"     "$DB_NAME"
fi

chmod 600 "$output"
sha256sum "$output" > "$manifest"
chmod 600 "$manifest"

{
  printf 'created_at_utc=%s\n' "$timestamp"
  printf 'host=%s\n' "$host"
  printf 'backup=%s\n' "$output"
  printf 'size_bytes=%s\n' "$(stat -c '%s' "$output")"
  printf 'sha256=%s\n' "$(cut -d ' ' -f1 "$manifest")"
} > "$metadata"
chmod 600 "$metadata"

if [[ "$INCLUDE_ENV" == "true" ]]; then
  if [[ ! -r /etc/dietoexpress/dietoexpress.env ]]; then
    echo "ERROR: no se puede leer /etc/dietoexpress/dietoexpress.env." >&2
    exit 1
  fi
  cp /etc/dietoexpress/dietoexpress.env "$BACKUP_DIR/dietoexpress-env-$timestamp"
  chmod 600 "$BACKUP_DIR/dietoexpress-env-$timestamp"
fi

echo "Backup creado: $output"
echo "Checksum: $manifest"

# Mantiene solo las últimas N copias completas. Los sidecars se eliminan junto
# con su dump para evitar dejar metadatos huérfanos.
if [[ "$RETENTION" =~ ^[0-9]+$ ]] && (( RETENTION > 0 )); then
  mapfile -t backups < <(find "$BACKUP_DIR" -maxdepth 1 -type f -name 'dietoexpress-postgresql-*.dump' -printf '%T@ %p\n' | sort -nr | tail -n +$((RETENTION + 1)) | cut -d' ' -f2-)
  for old_backup in "${backups[@]}"; do
    old_base="${old_backup%.dump}"
    rm -f -- "$old_backup" "${old_base}.sha256" "${old_base}.txt"
  done
fi
