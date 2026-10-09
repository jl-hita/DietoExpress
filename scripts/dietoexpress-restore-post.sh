#!/usr/bin/env bash
set -euo pipefail
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH

# Exit status 20 está reservado para el caso en que también falla el rollback.
# Nunca arrancar contra una base potencialmente restaurada a medias.
if [[ "${EXIT_CODE:-}" == "exited" && "${EXIT_STATUS:-}" == "20" ]]; then
  echo "CRITICAL: rollback fallido; DietoExpress permanece detenido y el aviso de mantenimiento se conserva." >&2
  logger -t dietoexpress-restore "CRITICAL: rollback fallido; servicio dejado detenido para recuperación manual."
  exit 0
fi

if ! systemctl start dietoexpress.service; then
  echo "ERROR: no se pudo iniciar dietoexpress.service; se conserva el aviso de mantenimiento." >&2
  logger -t dietoexpress-restore "ERROR: no se pudo iniciar dietoexpress.service después de restore."
  exit 1
fi

for _ in {1..30}; do
  if systemctl is-active --quiet dietoexpress.service; then
    rm -f -- /var/lib/dietoexpress/maintenance.json
    exit 0
  fi
  sleep 1
done

echo "ERROR: DietoExpress no quedó activo tras 30 segundos; se conserva el aviso." >&2
logger -t dietoexpress-restore "ERROR: DietoExpress no quedó activo después de restore; aviso conservado."
exit 1
