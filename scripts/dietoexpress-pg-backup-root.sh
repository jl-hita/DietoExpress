#!/usr/bin/env bash
set -euo pipefail
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH
umask 077

[[ "$(id -u)" -eq 0 ]] || { echo "ERROR: este helper debe ejecutarse como root." >&2; exit 1; }
[[ "$#" -eq 1 && ( "$1" == "--backup" || "$1" == "--backup-pre-restore" ) ]] || {
  echo "ERROR: uso permitido: dietoexpress-pg-backup --backup o --backup-pre-restore" >&2
  exit 2
}
BACKUP_MODE="$1"

ENV_FILE=/etc/dietoexpress/dietoexpress.env
[[ -r "$ENV_FILE" ]] || { echo "ERROR: no se puede leer $ENV_FILE." >&2; exit 1; }
# shellcheck disable=SC1090
source "$ENV_FILE"

BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
DATABASE_NAME="${DIETOEXPRESS_DATABASE:-}"
DOCUMENTS_DIR="${DIETOEXPRESS_DOCUMENTS_PATH:-/var/lib/dietoexpress/documents}"
DB_PORT="${DIETOEXPRESS_DB_PORT:-5432}"
BACKUP_GROUP="${DIETOEXPRESS_BACKUP_GROUP:-joso}"
RETENTION="${DIETOEXPRESS_BACKUP_RETENTION:-8}"
PRE_RETENTION="${DIETOEXPRESS_BACKUP_PRE_RETENTION:-4}"
INCLUDE_ENV="${DIETOEXPRESS_BACKUP_INCLUDE_ENV:-false}"

[[ -n "$BACKUP_DIR" && "$BACKUP_DIR" == /* && "$BACKUP_DIR" != "/" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR debe ser una ruta absoluta válida." >&2; exit 1; }
[[ "$(realpath -m -- "$BACKUP_DIR")" == "$BACKUP_DIR" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR debe estar normalizado y no usar enlaces simbólicos." >&2; exit 1; }
[[ -n "$DATABASE_NAME" ]] || { echo "ERROR: DIETOEXPRESS_DATABASE debe estar configurado en $ENV_FILE." >&2; exit 1; }
[[ "$DOCUMENTS_DIR" == /* && "$DOCUMENTS_DIR" != "/" && "$(realpath -m -- "$DOCUMENTS_DIR")" == "$DOCUMENTS_DIR" ]] || { echo "ERROR: DIETOEXPRESS_DOCUMENTS_PATH debe ser una ruta absoluta normalizada." >&2; exit 1; }
[[ -d "$DOCUMENTS_DIR" && ! -L "$DOCUMENTS_DIR" ]] || { echo "ERROR: no existe el directorio de documentos o es un enlace simbólico: $DOCUMENTS_DIR" >&2; exit 1; }
if find "$DOCUMENTS_DIR" -type l -print -quit | grep -q .; then
  echo "ERROR: el árbol de documentos contiene enlaces simbólicos; se rechaza para evitar escapes al restaurar." >&2
  exit 1
fi
[[ "$DB_PORT" =~ ^[0-9]+$ ]] && (( DB_PORT >= 1 && DB_PORT <= 65535 )) || { echo "ERROR: DIETOEXPRESS_DB_PORT no es válido." >&2; exit 1; }
getent group "$BACKUP_GROUP" >/dev/null 2>&1 || { echo "ERROR: no existe el grupo de backups $BACKUP_GROUP." >&2; exit 1; }

# Ningún ancestro del directorio de copias puede ser modificable por joso.
# En concreto, /var/lib/dietoexpress es escribible por el servicio y no debe
# contener el almacén privilegiado: usa /var/lib/dietoexpress-backups.
parent="$(dirname "$BACKUP_DIR")"
[[ -d "$parent" ]] || { echo "ERROR: el directorio padre de backups debe existir: $parent" >&2; exit 1; }
candidate="$BACKUP_DIR"
while [[ "$candidate" != "/" ]]; do
  [[ ! -L "$candidate" ]] || { echo "ERROR: no se permiten enlaces simbólicos en la ruta de backups: $candidate" >&2; exit 1; }
  candidate="$(dirname "$candidate")"
done
candidate="$parent"
while [[ "$candidate" != "/" ]]; do
  if runuser -u joso -- /usr/bin/test -w "$candidate"; then
    echo "ERROR: $candidate es modificable por joso. Configura DIETOEXPRESS_BACKUP_DIR en una ruta root-owned, por ejemplo /var/lib/dietoexpress-backups." >&2
    exit 1
  fi
  candidate="$(dirname "$candidate")"
done

install -d -o root -g "$BACKUP_GROUP" -m 0750 "$BACKUP_DIR"
[[ ! -L "$BACKUP_DIR" ]] || { echo "ERROR: el directorio de backups no puede ser un enlace simbólico." >&2; exit 1; }
if runuser -u joso -- /usr/bin/test -w "$BACKUP_DIR"; then
  echo "ERROR: joso no debe poder modificar el directorio privilegiado de backups." >&2
  exit 1
fi

# Backups y restauraciones comparten lock para evitar snapshots concurrentes
# mientras pg_restore está cambiando la base.
install -d -o root -g root -m 0755 /run/lock
exec 9>/run/lock/dietoexpress-postgresql.lock
flock 9

tmp_dir="$(mktemp -d /tmp/dietoexpress-pg-backup.XXXXXX)"
chown postgres:postgres "$tmp_dir"
chmod 0700 "$tmp_dir"

timestamp="$(date -u '+%Y%m%dT%H%M%SZ')"
host="$(hostname -f 2>/dev/null || hostname)"
suffix=""
[[ "$BACKUP_MODE" == "--backup-pre-restore" ]] && suffix="-pre-restore"
stem="dietoexpress-postgresql-$timestamp$suffix"
file="$stem.dump"
manifest_name="$stem.sha256"
metadata_name="$stem.txt"
documents_name="dietoexpress-documents-$timestamp$suffix.tar.gz"
documents_manifest_name="dietoexpress-documents-$timestamp$suffix.tar.gz.sha256"
dump_path="$BACKUP_DIR/$file"
manifest_path="$BACKUP_DIR/$manifest_name"
metadata_path="$BACKUP_DIR/$metadata_name"
documents_path="$BACKUP_DIR/$documents_name"
documents_manifest_path="$BACKUP_DIR/$documents_manifest_name"
env_suffix="$timestamp$suffix"
env_path="$BACKUP_DIR/dietoexpress-env-$env_suffix"
tmp_dump="$tmp_dir/$file"
tmp_documents="$tmp_dir/$documents_name"
tmp_documents_manifest="$tmp_dir/$documents_manifest_name"
committed=false
published_dump=false
published_manifest=false
published_metadata=false
published_documents=false
published_documents_manifest=false
published_env=false

cleanup() {
  local rc=$?
  # Solo eliminamos artefactos que este proceso publicó; nunca borramos una
  # copia preexistente en caso de colisión de timestamp.
  if [[ "$committed" != true ]]; then
    [[ "$published_dump" != true ]] || rm -f -- "$dump_path"
    [[ "$published_manifest" != true ]] || rm -f -- "$manifest_path"
    [[ "$published_metadata" != true ]] || rm -f -- "$metadata_path"
    [[ "$published_documents" != true ]] || rm -f -- "$documents_path"
    [[ "$published_documents_manifest" != true ]] || rm -f -- "$documents_manifest_path"
    [[ "$published_env" != true ]] || rm -f -- "$env_path"
  fi
  rm -rf -- "$tmp_dir"
  return "$rc"
}
trap cleanup EXIT

for final_path in "$dump_path" "$manifest_path" "$metadata_path" "$documents_path" "$documents_manifest_path"; do
  [[ ! -e "$final_path" && ! -L "$final_path" ]] || { echo "ERROR: ya existe un artefacto de backup con timestamp $timestamp." >&2; exit 1; }
done
if [[ "$INCLUDE_ENV" == "true" ]]; then
  [[ ! -e "$env_path" && ! -L "$env_path" ]] || { echo "ERROR: ya existe la copia de configuración $env_path." >&2; exit 1; }
fi

# No usar --no-owner: pg_dump debe guardar la información de propietarios para
# que la restauración privilegiada devuelva las tablas a dietoexpress y las
# extensiones a su propietario original.
runuser -u postgres -- /usr/bin/pg_dump \
  --format=custom \
  --username=postgres \
  --port="$DB_PORT" \
  --dbname="$DATABASE_NAME" \
  --file="$tmp_dump"

# La copia de documentos comparte timestamp con PostgreSQL para poder restaurar
# ambos conjuntos como una sola unidad lógica.
/usr/bin/tar --create --gzip --file="$tmp_documents" --directory="$DOCUMENTS_DIR" .
/usr/bin/tar --list --gzip --file="$tmp_documents" >/dev/null
/usr/bin/pg_restore --list "$tmp_dump" >/dev/null
sha256="$(sha256sum "$tmp_dump" | awk '{print $1}')"
size_bytes="$(stat -c '%s' "$tmp_dump")"
documents_sha256="$(sha256sum "$tmp_documents" | awk '{print $1}')"
documents_size_bytes="$(stat -c '%s' "$tmp_documents")"
printf '%s  %s\n' "$sha256" "$file" > "$tmp_dir/$manifest_name"
printf '%s  %s\n' "$documents_sha256" "$documents_name" > "$tmp_documents_manifest"
{
  printf 'created_at_utc=%s\n' "$timestamp"
  printf 'host=%s\n' "$host"
  printf 'backup=%s\n' "$dump_path"
  printf 'size_bytes=%s\n' "$size_bytes"
  printf 'sha256=%s\n' "$sha256"
  printf 'documents_archive=%s\n' "$documents_name"
  printf 'documents_size_bytes=%s\n' "$documents_size_bytes"
  printf 'documents_sha256=%s\n' "$documents_sha256"
} > "$tmp_dir/$metadata_name"

# El almacén es root-owned y joso solo tiene lectura/travesía por grupo.
install -o root -g "$BACKUP_GROUP" -m 0640 "$tmp_dump" "$dump_path"
published_dump=true
install -o root -g "$BACKUP_GROUP" -m 0640 "$tmp_dir/$manifest_name" "$manifest_path"
published_manifest=true
install -o root -g "$BACKUP_GROUP" -m 0640 "$tmp_dir/$metadata_name" "$metadata_path"
published_metadata=true
install -o root -g "$BACKUP_GROUP" -m 0640 "$tmp_documents" "$documents_path"
published_documents=true
install -o root -g "$BACKUP_GROUP" -m 0640 "$tmp_documents_manifest" "$documents_manifest_path"
published_documents_manifest=true
if [[ "$INCLUDE_ENV" == "true" ]]; then
  install -o root -g root -m 0600 "$ENV_FILE" "$env_path"
  published_env=true
fi
(cd "$BACKUP_DIR" && sha256sum -c "$manifest_name" "$documents_manifest_name")
committed=true

echo "Backup creado: $dump_path"
echo "Checksum: $manifest_path"
echo "Documentos: $documents_path"
echo "Checksum documentos: $documents_manifest_path"

# La retención la hace root, porque joso no puede borrar ni reemplazar backups.
# Las copias automáticas pre-restore tienen una retención independiente; las
# copias manuales cuyo sufijo contiene -pre- nunca se mezclan con las normales.
if [[ "$RETENTION" =~ ^[0-9]+$ ]] && (( RETENTION > 0 )); then
  mapfile -t expired < <(
    find "$BACKUP_DIR" -maxdepth 1 -type f \
      -name 'dietoexpress-postgresql-*.dump' ! -name '*-pre-*' \
      -printf '%T@ %p\n' | sort -nr | tail -n +$((RETENTION + 1)) | cut -d' ' -f2-
  )
  for old_backup in "${expired[@]}"; do
    old_stem="$(basename "${old_backup%.dump}")"
    suffix_part="${old_stem#dietoexpress-postgresql-}"
    rm -f -- "$old_backup" "${old_backup%.dump}.sha256" "${old_backup%.dump}.txt" \
      "$BACKUP_DIR/dietoexpress-documents-$suffix_part.tar.gz" \
      "$BACKUP_DIR/dietoexpress-documents-$suffix_part.tar.gz.sha256" \
      "$BACKUP_DIR/dietoexpress-env-$suffix_part"
  done
fi
if [[ "$PRE_RETENTION" =~ ^[0-9]+$ ]] && (( PRE_RETENTION > 0 )); then
  mapfile -t expired_pre < <(
    find "$BACKUP_DIR" -maxdepth 1 -type f \
      -name 'dietoexpress-postgresql-*-pre-restore.dump' \
      -printf '%T@ %p\n' | sort -nr | tail -n +$((PRE_RETENTION + 1)) | cut -d' ' -f2-
  )
  for old_backup in "${expired_pre[@]}"; do
    old_stem="$(basename "${old_backup%.dump}")"
    suffix_part="${old_stem#dietoexpress-postgresql-}"
    rm -f -- "$old_backup" "${old_backup%.dump}.sha256" "${old_backup%.dump}.txt" \
      "$BACKUP_DIR/dietoexpress-documents-$suffix_part.tar.gz" \
      "$BACKUP_DIR/dietoexpress-documents-$suffix_part.tar.gz.sha256" \
      "$BACKUP_DIR/dietoexpress-env-$suffix_part"
  done
fi
