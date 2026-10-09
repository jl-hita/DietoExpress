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

[[ -n "$BACKUP_DIR" && "$BACKUP_DIR" == /* && "$BACKUP_DIR" != "/" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR debe ser una ruta absoluta válida." >&2; exit 1; }
[[ "$(realpath -m -- "$BACKUP_DIR")" == "$BACKUP_DIR" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR debe estar normalizado y no usar enlaces simbólicos." >&2; exit 1; }
[[ -n "$DATABASE_NAME" ]] || { echo "ERROR: DIETOEXPRESS_DATABASE debe estar configurado para la restauración privilegiada." >&2; exit 1; }
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
[[ "$(stat -c '%u' "$source_file")" == "0" ]] || { echo "ERROR: el backup no es root-owned; migra y verifica el almacén antes de restaurar." >&2; exit 1; }
[[ -f "$manifest" && ! -L "$manifest" ]] || { echo "ERROR: falta un manifiesto SHA-256 válido: $manifest" >&2; exit 1; }
[[ "$(stat -c '%u' "$manifest")" == "0" ]] || { echo "ERROR: el manifiesto no es root-owned; migra y verifica el almacén antes de restaurar." >&2; exit 1; }

# Admite manifiestos antiguos (ruta absoluta) y nuevos (nombre relativo),
# pero nunca permite que el manifiesto solicite validar un archivo diferente.
read -r expected stored_path < "$manifest" || { echo "ERROR: manifiesto SHA-256 vacío o inválido." >&2; exit 1; }
[[ "$expected" =~ ^[[:xdigit:]]{64}$ ]] || { echo "ERROR: checksum con formato inválido." >&2; exit 1; }
[[ "$stored_path" == "$file" || "$stored_path" == "$BACKUP_DIR/$file" ]] || { echo "ERROR: el manifiesto no apunta a la copia solicitada." >&2; exit 1; }
actual="$(sha256sum "$source_file" | awk '{print $1}')"
[[ "$actual" == "$expected" ]] || { echo "ERROR: el checksum de $source_file no coincide." >&2; exit 1; }
/usr/bin/pg_restore --list "$source_file" >/dev/null

install -d -o root -g root -m 0755 /run/lock
exec 9>/run/lock/dietoexpress-postgresql.lock
flock 9

# El directorio permanente es privado para joso. PostgreSQL solo recibe una
# copia temporal dentro de un directorio root:postgres 0750 y un archivo 0600.
tmp_dir="$(mktemp -d /tmp/dietoexpress-restore.XXXXXX)"
chown root:postgres "$tmp_dir"
chmod 0750 "$tmp_dir"
tmp_file="$tmp_dir/$file"
cleanup() { rm -f -- "$tmp_file"; rm -rf -- "$tmp_dir"; }
trap cleanup EXIT
install -o postgres -g postgres -m 0600 "$source_file" "$tmp_file"

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
