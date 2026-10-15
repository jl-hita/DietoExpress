#!/usr/bin/env bash
set -euo pipefail
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH
umask 077

[[ "$(id -u)" -eq 0 ]] || { echo "ERROR: este helper debe ejecutarse como root." >&2; exit 1; }
[[ "$#" -eq 3 && "$1" == "--restore" && "$3" == "--confirm" ]] || {
  echo "ERROR: uso permitido: dietoexpress-pg-restore --restore FILE.dump --confirm" >&2
  exit 2
}
file="$2"
[[ "$file" != */* && "$file" == dietoexpress-postgresql-*.dump ]] || { echo "ERROR: nombre de backup no válido." >&2; exit 2; }

ENV_FILE=/etc/dietoexpress/dietoexpress.env
[[ -r "$ENV_FILE" ]] || { echo "ERROR: no se puede leer $ENV_FILE." >&2; exit 1; }
# shellcheck disable=SC1090
source "$ENV_FILE"
BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
DATABASE_NAME="${DIETOEXPRESS_DATABASE:-}"
DOCUMENTS_DIR="${DIETOEXPRESS_DOCUMENTS_PATH:-/var/lib/dietoexpress/documents}"
BACKUP_GROUP="${DIETOEXPRESS_BACKUP_GROUP:-joso}"
getent group "$BACKUP_GROUP" >/dev/null 2>&1 || { echo "ERROR: no existe el grupo de backups $BACKUP_GROUP." >&2; exit 1; }
BACKUP_GID="$(getent group "$BACKUP_GROUP" | cut -d: -f3)"

[[ -n "$BACKUP_DIR" && "$BACKUP_DIR" == /* && "$BACKUP_DIR" != "/" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR debe ser una ruta absoluta válida." >&2; exit 1; }
[[ "$(realpath -m -- "$BACKUP_DIR")" == "$BACKUP_DIR" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR debe estar normalizado y no usar enlaces simbólicos." >&2; exit 1; }
[[ -n "$DATABASE_NAME" ]] || { echo "ERROR: DIETOEXPRESS_DATABASE debe estar configurado para la restauración privilegiada." >&2; exit 1; }
[[ "$DOCUMENTS_DIR" == /* && "$DOCUMENTS_DIR" != "/" && "$(realpath -m -- "$DOCUMENTS_DIR")" == "$DOCUMENTS_DIR" ]] || { echo "ERROR: DIETOEXPRESS_DOCUMENTS_PATH debe ser una ruta absoluta normalizada." >&2; exit 1; }
[[ -d "$DOCUMENTS_DIR" && ! -L "$DOCUMENTS_DIR" ]] || { echo "ERROR: el directorio de documentos no existe o es un enlace simbólico: $DOCUMENTS_DIR" >&2; exit 1; }
[[ -d "$BACKUP_DIR" && ! -L "$BACKUP_DIR" ]] || { echo "ERROR: el almacén root-owned de backups no existe." >&2; exit 1; }

candidate="$BACKUP_DIR"
while [[ "$candidate" != "/" ]]; do
  [[ ! -L "$candidate" ]] || { echo "ERROR: no se permiten enlaces simbólicos en la ruta de backups: $candidate" >&2; exit 1; }
  candidate="$(dirname "$candidate")"
done
candidate="$(dirname "$BACKUP_DIR")"
while [[ "$candidate" != "/" ]]; do
  if runuser -u joso -- /usr/bin/test -w "$candidate"; then
    echo "ERROR: $candidate es modificable por joso; el almacén privilegiado no es seguro." >&2
    exit 1
  fi
  candidate="$(dirname "$candidate")"
done
if runuser -u joso -- /usr/bin/test -w "$BACKUP_DIR"; then
  echo "ERROR: joso no debe poder modificar el almacén de backups." >&2
  exit 1
fi

source_file="$BACKUP_DIR/$file"
manifest="$BACKUP_DIR/${file%.dump}.sha256"
[[ -f "$source_file" && ! -L "$source_file" ]] || { echo "ERROR: backup no encontrado o no válido: $source_file" >&2; exit 1; }
[[ "$(stat -c '%u:%g:%a' "$source_file")" == "0:$BACKUP_GID:640" ]] || { echo "ERROR: el backup debe ser root-owned, grupo $BACKUP_GROUP y modo 0640." >&2; exit 1; }
[[ -f "$manifest" && ! -L "$manifest" ]] || { echo "ERROR: falta un manifiesto SHA-256 válido: $manifest" >&2; exit 1; }
[[ "$(stat -c '%u:%g:%a' "$manifest")" == "0:$BACKUP_GID:640" ]] || { echo "ERROR: el manifiesto debe ser root-owned, grupo $BACKUP_GROUP y modo 0640." >&2; exit 1; }

# Admite manifiestos antiguos (ruta absoluta) y nuevos (nombre relativo),
# pero nunca permite que el manifiesto solicite validar un archivo diferente.
read -r expected stored_path < "$manifest" || { echo "ERROR: manifiesto SHA-256 vacío o inválido." >&2; exit 1; }
[[ "$expected" =~ ^[[:xdigit:]]{64}$ ]] || { echo "ERROR: checksum con formato inválido." >&2; exit 1; }
[[ "$stored_path" == "$file" || "$stored_path" == "$BACKUP_DIR/$file" ]] || { echo "ERROR: el manifiesto no apunta a la copia solicitada." >&2; exit 1; }
actual="$(sha256sum "$source_file" | awk '{print $1}')"
[[ "$actual" == "$expected" ]] || { echo "ERROR: el checksum de $source_file no coincide." >&2; exit 1; }
/usr/bin/pg_restore --list "$source_file" >/dev/null

# Cada dump debe tener su archivo de documentos con el mismo timestamp. No se
# permite restaurar solo PostgreSQL si falta la parte documental de la copia.
stem="${file%.dump}"
timestamp_part="${stem#dietoexpress-postgresql-}"
documents_file="dietoexpress-documents-$timestamp_part.tar.gz"
documents_source="$BACKUP_DIR/$documents_file"
documents_manifest="$documents_source.sha256"
[[ -f "$documents_source" && ! -L "$documents_source" ]] || { echo "ERROR: falta el archivo de documentos asociado ($documents_file); no se inicia una restauración parcial." >&2; exit 1; }
[[ -f "$documents_manifest" && ! -L "$documents_manifest" ]] || { echo "ERROR: falta el checksum de documentos $documents_manifest." >&2; exit 1; }
[[ "$(stat -c '%u:%g:%a' "$documents_source")" == "0:$BACKUP_GID:640" ]] || { echo "ERROR: el archivo de documentos debe ser root-owned, grupo $BACKUP_GROUP y modo 0640." >&2; exit 1; }
[[ "$(stat -c '%u:%g:%a' "$documents_manifest")" == "0:$BACKUP_GID:640" ]] || { echo "ERROR: el checksum de documentos debe ser root-owned, grupo $BACKUP_GROUP y modo 0640." >&2; exit 1; }
read -r documents_expected documents_stored < "$documents_manifest" || { echo "ERROR: checksum de documentos vacío o inválido." >&2; exit 1; }
[[ "$documents_expected" =~ ^[[:xdigit:]]{64}$ && "$documents_stored" == "$documents_file" ]] || { echo "ERROR: manifiesto de documentos inválido." >&2; exit 1; }
documents_actual="$(sha256sum "$documents_source" | awk '{print $1}')"
[[ "$documents_actual" == "$documents_expected" ]] || { echo "ERROR: el checksum de documentos no coincide." >&2; exit 1; }
/usr/bin/tar --list --gzip --file="$documents_source" >/dev/null
while IFS= read -r archive_entry; do
  archive_entry="${archive_entry#./}"
  [[ "$archive_entry" != /* ]] || { echo "ERROR: el archivo de documentos contiene una ruta absoluta." >&2; exit 1; }
  case "/$archive_entry/" in */../*|*/..|../*) echo "ERROR: el archivo de documentos contiene una ruta no permitida." >&2; exit 1 ;; esac
done < <(/usr/bin/tar --list --gzip --file="$documents_source")

install -d -o root -g root -m 0755 /run/lock
exec 9>/run/lock/dietoexpress-postgresql.lock
flock 9

# El directorio permanente es privado para joso. PostgreSQL solo recibe una
# copia temporal dentro de un directorio root:postgres 0750 y un archivo 0600.
tmp_dir="$(mktemp -d /tmp/dietoexpress-restore.XXXXXX)"
chown root:postgres "$tmp_dir"
chmod 0750 "$tmp_dir"
tmp_file="$tmp_dir/$file"
documents_parent="$(dirname "$DOCUMENTS_DIR")"
documents_staging="$(mktemp -d "$documents_parent/.dietoexpress-documents-restore.XXXXXX")"
documents_previous="$documents_parent/.dietoexpress-documents-previous-$$"
documents_swapped=false
cleanup() {
  local rc=$?
  rm -f -- "$tmp_file"
  rm -rf -- "$tmp_dir"
  [[ ! -d "$documents_staging" ]] || rm -rf -- "$documents_staging"
  # La copia anterior solo se conserva durante la operación. Si pg_restore falla,
  # el wrapper restaura el snapshot pre-restore (incluidos los documentos).
  [[ ! -d "$documents_previous" ]] || rm -rf -- "$documents_previous"
  return "$rc"
}
trap cleanup EXIT
install -o postgres -g postgres -m 0600 "$source_file" "$tmp_file"
/usr/bin/tar --extract --gzip --file="$documents_source" --directory="$documents_staging" --no-same-owner --no-same-permissions
chown -R joso:joso "$documents_staging"
chmod 0750 "$documents_staging"
[[ ! -e "$documents_previous" && ! -L "$documents_previous" ]] || { echo "ERROR: ya existe una ruta temporal de restauración documental." >&2; exit 1; }
mv -- "$DOCUMENTS_DIR" "$documents_previous"
if ! mv -- "$documents_staging" "$DOCUMENTS_DIR"; then
  mv -- "$documents_previous" "$DOCUMENTS_DIR"
  echo "ERROR: no se pudo activar el árbol de documentos restaurado." >&2
  exit 1
fi
documents_swapped=true
echo "Archivos de documentos restaurados desde $documents_file."

echo "Ejecutando pg_restore como usuario PostgreSQL postgres..."
# Sin --no-owner: el archive conserva los owners originales de tablas/extensiones.
runuser -u postgres -- /usr/bin/pg_restore \
  --username=postgres \
  --clean \
  --if-exists \
  --exit-on-error \
  --dbname="$DATABASE_NAME" \
  "$tmp_file"

echo "pg_restore privilegiado completado correctamente."
