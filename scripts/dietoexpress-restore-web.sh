#!/usr/bin/env bash
set -euo pipefail

# Este archivo solo sirve como fuente para el provisioning inicial. La copia que
# realmente ejecuta sudo está en /usr/local/sbin y es propiedad de root.
[[ "$#" -eq 2 && "$2" == "--confirm" ]] || {
  echo "ERROR: restauración no autorizada." >&2
  exit 2
}

file="$1"
[[ "$file" != */* ]] || { echo "ERROR: nombre de backup no válido." >&2; exit 2; }
[[ "$file" == dietoexpress-postgresql-*.dump ]] || {
  echo "ERROR: nombre de backup no válido." >&2
  exit 2
}

[[ -r /etc/dietoexpress/dietoexpress.env ]] || {
  echo "ERROR: no se puede leer la configuración de DietoExpress." >&2
  exit 1
}
set -a
# shellcheck disable=SC1091
source /etc/dietoexpress/dietoexpress.env
set +a

backup_dir="${DIETOEXPRESS_BACKUP_DIR:-}"
[[ -n "$backup_dir" && -f "$backup_dir/$file" ]] || {
  echo "ERROR: backup no encontrado." >&2
  exit 1
}

# La unidad root-owned es quien detiene/reinicia el servicio. El pg_restore se
# ejecuta dentro de ella como joso mediante runuser.
systemctl start --no-block "dietoexpress-restore@$file.service"
echo "Restauración iniciada para $file"
