#!/usr/bin/env bash
set -Eeuo pipefail

# Preparación inicial de un Fedora limpio para recuperar DietoExpress.
# No despliega la aplicación, no configura secretos, no abre puertos y no restaura
# ni sobrescribe datos. Ejecutar como root y revisar el runbook antes de continuar.

if [[ ${EUID} -ne 0 ]]; then
  echo "ERROR: ejecutar con sudo/root." >&2
  exit 1
fi
if [[ ! -r /etc/fedora-release ]]; then
  echo "ERROR: este script está diseñado exclusivamente para Fedora." >&2
  exit 1
fi

APP_USER="${DIETOEXPRESS_APP_USER:-joso}"
APP_HOME="${DIETOEXPRESS_APP_HOME:-/home/joso}"
APP_DIR="${DIETOEXPRESS_APP_DIR:-/opt/dietoexpress}"
WEB_DIR="${DIETOEXPRESS_WEB_DIR:-/var/www/dietoexpress}"
STATE_DIR="${DIETOEXPRESS_STATE_DIR:-/var/lib/dietoexpress}"
BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-/var/lib/dietoexpress-backups}"

case "$APP_DIR:$WEB_DIR:$STATE_DIR:$BACKUP_DIR" in
  *"/../"*|*"/.."|*"//"*)
    echo "ERROR: se detectó una ruta no normalizada." >&2
    exit 1
    ;;
esac
for path in "$APP_HOME" "$APP_DIR" "$WEB_DIR" "$STATE_DIR" "$BACKUP_DIR"; do
  [[ "$path" == /* && "$path" != "/" ]] || {
    echo "ERROR: ruta absoluta no válida: $path" >&2
    exit 1
  }
done

echo "==> Instalando dependencias de sistema en Fedora"
dnf install -y \
  aspnetcore-runtime-10.0 \
  postgresql-server \
  nginx \
  certbot \
  python3-certbot-nginx \
  git \
  curl \
  jq \
  rsync \
  rclone \
  tar \
  gzip \
  policycoreutils-python-utils

if ! getent group "$APP_USER" >/dev/null; then
  groupadd --system "$APP_USER"
fi
if ! id "$APP_USER" >/dev/null 2>&1; then
  useradd --system --create-home --home-dir "$APP_HOME" \
    --gid "$APP_USER" --shell /usr/sbin/nologin "$APP_USER"
fi
install -d -o "$APP_USER" -g "$APP_USER" -m 0750 "$APP_HOME"
install -d -o "$APP_USER" -g "$APP_USER" -m 0755 "$APP_DIR"
install -d -o "$APP_USER" -g "$APP_USER" -m 0755 "$WEB_DIR"
install -d -o "$APP_USER" -g "$APP_USER" -m 0750 "$STATE_DIR/Logs"
install -d -o "$APP_USER" -g "$APP_USER" -m 0750 "$STATE_DIR/AlertSpool"
install -d -o "$APP_USER" -g "$APP_USER" -m 0750 "$STATE_DIR/documents"

# El almacén de backups debe ser propiedad de root y no escribible por joso.
# El provisioning operativo posterior valida sus ancestros y puede abortar si
# la topología de permisos del sistema no es segura.
install -d -o root -g "$APP_USER" -m 0750 "$BACKUP_DIR"

# Inicializar PostgreSQL solo cuando aún no existe un clúster. No se toca uno
# existente, y no se crea ninguna base de datos de aplicación automáticamente.
if [[ ! -s /var/lib/pgsql/data/PG_VERSION ]]; then
  echo "==> No se encontró un clúster PostgreSQL; inicializando uno vacío"
  postgresql-setup --initdb
else
  echo "==> Clúster PostgreSQL existente detectado; no se modifica"
fi

systemctl enable --now postgresql
systemctl enable --now nginx

echo
echo "Preparación del sistema completada."
echo "No se ha desplegado DietoExpress ni se han restaurado datos."
echo "Siguiente paso: seguir docs/RECUPERACION_DESDE_CERO.md."
echo "Antes de continuar, recuperar los secretos desde la custodia externa segura."
echo
echo "Versiones instaladas:"
dotnet --list-runtimes
psql --version
nginx -v 2>&1
rclone version | head -n 1
