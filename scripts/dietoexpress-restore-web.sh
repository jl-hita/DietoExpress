#!/usr/bin/env bash
set -euo pipefail

# Único punto de entrada privilegiado para restauraciones iniciadas desde SuperAdmin.
# El script se instala como root y se autoriza explícitamente en sudoers.
if [[ "$#" -ne 2 || "$2" != "--confirm" ]]; then
  echo "ERROR: restauración no autorizada." >&2
  exit 2
fi

file="$1"
[[ "$file" != */* ]] || { echo "ERROR: nombre de backup no válido." >&2; exit 2; }
[[ "$file" == dietoexpress-postgresql-*.dump ]] || { echo "ERROR: nombre de backup no válido." >&2; exit 2; }

if [[ -r /etc/dietoexpress/dietoexpress.env ]]; then
  set -a
  # shellcheck disable=SC1091
  source /etc/dietoexpress/dietoexpress.env
  set +a
fi

exec /opt/dietoexpress/scripts/dietoexpress-restore.sh --restore "$file" --confirm
