#!/usr/bin/env bash
set -euo pipefail

# Punto de entrada privilegiado para SuperAdmin. La restauración se desacopla del
# proceso web mediante una unidad systemd independiente porque el restore detiene
# dietoexpress.service antes de modificar PostgreSQL.
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

unit="dietoexpress-restore-$(date -u +%Y%m%dT%H%M%SZ)-$$"
systemd-run --quiet --collect --unit="$unit"   --property=EnvironmentFile=-/etc/dietoexpress/dietoexpress.env   /opt/dietoexpress/scripts/dietoexpress-restore.sh --restore "$file" --confirm

echo "Restauración iniciada en la unidad systemd: $unit"
