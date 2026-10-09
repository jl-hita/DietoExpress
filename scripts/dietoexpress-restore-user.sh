#!/usr/bin/env bash
set -euo pipefail

# Esta parte se ejecuta como joso. Los helpers root-owned, instalados fuera de
# /opt, realizan pg_dump/pg_restore con la identidad PostgreSQL adecuada.
BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
BACKUP_SCRIPT="${DIETOEXPRESS_BACKUP_SCRIPT:-/opt/dietoexpress/scripts/dietoexpress-backup.sh}"
RESTORE_HELPER="${DIETOEXPRESS_RESTORE_HELPER:-/usr/local/sbin/dietoexpress-pg-restore}"

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

[[ "$mode" == restore && -n "$file" && "$confirm" == true ]] || { echo "ERROR: restauración no autorizada." >&2; exit 2; }
[[ "$file" != */* && "$file" == dietoexpress-postgresql-*.dump ]] || { echo "ERROR: nombre de backup no válido." >&2; exit 2; }
[[ -n "$BACKUP_DIR" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR no está configurado." >&2; exit 1; }

path="$BACKUP_DIR/$file"
[[ -f "$path" && ! -L "$path" ]] || { echo "ERROR: backup no encontrado o no válido: $path" >&2; exit 1; }
manifest="${path%.dump}.sha256"
[[ -f "$manifest" && ! -L "$manifest" ]] || { echo "ERROR: falta el checksum $manifest" >&2; exit 1; }
(cd "$BACKUP_DIR" && sha256sum -c "$(basename "$manifest")")
pg_restore --list "$path" >/dev/null

[[ -x "$BACKUP_SCRIPT" ]] || { echo "ERROR: no existe el script de backup ejecutable: $BACKUP_SCRIPT" >&2; exit 1; }
# El helper es root:root; puede no ser ejecutable directamente por joso.
[[ -f "$RESTORE_HELPER" ]] || { echo "ERROR: falta el helper privilegiado $RESTORE_HELPER. Ejecuta el provisioning como root." >&2; exit 1; }
command -v sudo >/dev/null 2>&1 || { echo "ERROR: sudo no está disponible." >&2; exit 1; }

# La copia previa se crea por el mismo helper privilegiado que usan el timer y
# el backup manual: pg_dump como postgres, más checksum y metadatos.
pre_restore_output="$("$BACKUP_SCRIPT")"
pre_restore_file="$(printf '%s\n' "$pre_restore_output" | sed -n 's/^Backup creado: //p' | tail -n1)"
[[ -n "$pre_restore_file" && -f "$pre_restore_file" ]] || { echo "ERROR: no se pudo crear/verificar el backup pre-restauración." >&2; exit 1; }
pre_restore_name="$(basename "$pre_restore_file")"
echo "Backup pre-restauración: $pre_restore_name"

set +e
sudo -n "$RESTORE_HELPER" --restore "$file" --confirm
restore_rc=$?
set -e
if [[ "$restore_rc" -eq 0 ]]; then
  echo "Restauración PostgreSQL completada correctamente."
  exit 0
fi

echo "ERROR: la restauración solicitada falló (código $restore_rc). Intentando rollback a $pre_restore_name..." >&2
set +e
sudo -n "$RESTORE_HELPER" --restore "$pre_restore_name" --confirm
rollback_rc=$?
set -e

if [[ "$rollback_rc" -ne 0 ]]; then
  echo "CRITICAL: restauración y rollback han fallado (rollback=$rollback_rc). La unidad debe dejar el servicio detenido para evitar iniciar contra una base potencialmente parcial." >&2
  exit 20
fi

echo "Rollback completado correctamente. La restauración solicitada se considera fallida; se recuperó el estado previo."
# Systemd marca el intento como fallido, pero ExecStopPost sabe que debe iniciar
# el backend porque el rollback ha terminado correctamente.
exit 10
