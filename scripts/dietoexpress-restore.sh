#!/usr/bin/env bash
set -euo pipefail

# CLI de verificación y compatibilidad. La restauración de producción se delega
# siempre al wrapper root-owned y a la unidad systemd.
BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-}"
RESTORE_WRAPPER="${DIETOEXPRESS_RESTORE_WRAPPER:-/usr/local/sbin/dietoexpress-restore-web}"

usage() {
  cat <<USAGE
Usage:
  $0 --verify FILE.dump
  $0 --restore FILE.dump --confirm

La restauración de producción se delega al wrapper root-owned de systemd.
No se ejecuta pg_restore directamente como usuario de aplicación.
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
[[ "$file" != */* && "$file" == dietoexpress-postgresql-*.dump ]] || { echo "ERROR: nombre de backup no permitido." >&2; exit 2; }
[[ -n "$BACKUP_DIR" ]] || { echo "ERROR: DIETOEXPRESS_BACKUP_DIR no está configurado." >&2; exit 1; }

path="$BACKUP_DIR/$file"
[[ -f "$path" && ! -L "$path" ]] || { echo "ERROR: backup no encontrado o no válido: $path" >&2; exit 1; }
manifest="${path%.dump}.sha256"
[[ -f "$manifest" && ! -L "$manifest" ]] || { echo "ERROR: falta el checksum $manifest" >&2; exit 1; }
(cd "$BACKUP_DIR" && sha256sum -c "$(basename "$manifest")")
pg_restore --list "$path" >/dev/null
echo "Backup verificado: $path"

if [[ "$mode" == verify ]]; then
  echo "Preflight correcto. No se ha modificado la base de datos."
  exit 0
fi
[[ "$confirm" == true ]] || { echo "ERROR: una restauración requiere --confirm." >&2; exit 2; }
[[ -f "$RESTORE_WRAPPER" ]] || { echo "ERROR: no existe el wrapper privilegiado $RESTORE_WRAPPER. Ejecuta el provisioning como root." >&2; exit 1; }

if [[ "$(id -u)" -eq 0 ]]; then
  "$RESTORE_WRAPPER" "$file" --confirm
else
  sudo -n "$RESTORE_WRAPPER" "$file" --confirm
fi
