#!/usr/bin/env bash
set -euo pipefail

# Orquestador no privilegiado de backups. El helper root-owned ejecuta pg_dump
# como PostgreSQL superuser y publica los ficheros con propietario joso.
BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
DB_NAME="${DIETOEXPRESS_DATABASE:-}"
BACKUP_HELPER="${DIETOEXPRESS_BACKUP_HELPER:-/usr/local/sbin/dietoexpress-pg-backup}"
INCLUDE_ENV="${DIETOEXPRESS_BACKUP_INCLUDE_ENV:-false}"
BACKUP_MODE="${DIETOEXPRESS_BACKUP_MODE:-normal}"
RETENTION="${DIETOEXPRESS_BACKUP_RETENTION:-8}"

[[ -n "$BACKUP_DIR" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR no está configurado." >&2; exit 1; }
[[ -n "$DB_NAME" ]] || { echo "ERROR: DIETOEXPRESS_DATABASE es obligatorio para el backup privilegiado local." >&2; exit 1; }
# El helper es root:root y 0750: joso comprueba que exista, pero sudo comprueba
# el permiso de ejecución y limita los argumentos permitidos.
[[ -f "$BACKUP_HELPER" ]] || { echo "ERROR: falta el helper privilegiado $BACKUP_HELPER. Ejecuta el provisioning como root." >&2; exit 1; }
command -v sudo >/dev/null 2>&1 || { echo "ERROR: sudo no está disponible." >&2; exit 1; }

umask 077
mkdir -p -- "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

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
[[ -n "$output" && -f "$output" ]] || { echo "ERROR: el helper no devolvió una copia existente." >&2; exit 1; }
manifest="${output%.dump}.sha256"

if [[ "$INCLUDE_ENV" == "true" ]]; then
  if [[ ! -r /etc/dietoexpress/dietoexpress.env ]]; then
    echo "ERROR: no se puede leer /etc/dietoexpress/dietoexpress.env." >&2
    exit 1
  fi
  timestamp="${output##*dietoexpress-postgresql-}"
  timestamp="${timestamp%.dump}"
  cp /etc/dietoexpress/dietoexpress.env "$BACKUP_DIR/dietoexpress-env-$timestamp"
  chmod 600 "$BACKUP_DIR/dietoexpress-env-$timestamp"
fi

# Los snapshots de emergencia/manuales -pre-* se excluyen de la retención.
if [[ "$RETENTION" =~ ^[0-9]+$ ]] && (( RETENTION > 0 )); then
  mapfile -t backups < <(
    find "$BACKUP_DIR" -maxdepth 1 -type f \
      -name 'dietoexpress-postgresql-*.dump' ! -name '*-pre-*' \
      -printf '%T@ %p\n' |
      sort -nr |
      tail -n +$((RETENTION + 1)) |
      cut -d' ' -f2-
  )
  for old_backup in "${backups[@]}"; do
    old_base="${old_backup%.dump}"
    rm -f -- "$old_backup" "${old_base}.sha256" "${old_base}.txt"
  done
fi

echo "Checksum: $manifest"
