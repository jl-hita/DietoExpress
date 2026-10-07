#!/usr/bin/env bash
set -euo pipefail

# Comprobación operativa sin modificar datos.
# Se puede usar desde un monitor externo o una sesión de diagnóstico.

fail=0

check_service() {
  local service="$1"
  if systemctl is-active --quiet "$service"; then
    echo "OK service=$service"
  else
    echo "FAIL service=$service" >&2
    fail=1
  fi
}

check_service dietoexpress.service
check_service nginx
check_service postgresql

if ! df -P / | awk 'NR == 2 { exit ($5 + 0 >= 90) ? 1 : 0 }'; then
  echo "FAIL disk_usage_root>=90%" >&2
  fail=1
else
  echo "OK disk_usage_root<90%"
fi

if [[ -d /var/lib/dietoexpress/AlertSpool ]]; then
  spool_count="$(find /var/lib/dietoexpress/AlertSpool -maxdepth 1 -type f | wc -l)"
  echo "INFO alert_spool_files=$spool_count"
else
  echo "FAIL alert_spool_missing" >&2
  fail=1
fi

exit "$fail"
