#!/usr/bin/env bash
set -Eeuo pipefail
umask 022
PATH=/usr/sbin:/usr/bin:/sbin:/bin
export PATH

# Helper privilegiado instalado como root en /usr/local/sbin.
# Solo acepta una versión; todas las rutas y opciones son fijas.
[[ "$(id -u)" -eq 0 && "${SUDO_USER:-}" == "joso" ]] || {
  echo "ERROR: este helper solo puede invocarlo joso mediante sudo." >&2
  exit 1
}
[[ "$#" -eq 1 ]] || { echo "ERROR: uso: dietoexpress-deploy VERSION" >&2; exit 2; }

APP_ROOT="/opt/dietoexpress"
WEB_ROOT="/var/www/dietoexpress"
WEB_RELEASES="/var/www/dietoexpress-releases"
BACKEND_STAGE="/tmp/dietoexpress-backend"
FRONTEND_STAGE="/tmp/dietoexpress-frontend"
VERSION="$1"
DOMAIN="jlhitap.duckdns.org"
KEEP_RELEASES=5
LOCK_FILE="/run/lock/dietoexpress-deploy.lock"

fail() { echo "ERROR: $*" >&2; exit 1; }
[[ "$VERSION" =~ ^[A-Za-z0-9._-]{7,80}$ ]] || fail "identificador de versión no válido."
[[ "$KEEP_RELEASES" =~ ^[1-9][0-9]*$ ]] || fail "retención no válida."
[[ -d "$BACKEND_STAGE" && ! -L "$BACKEND_STAGE" && -f "$BACKEND_STAGE/Anguloso.Server.dll" ]] || fail "falta el backend publicado o el staging no es válido."
[[ "$(stat -c '%U' "$BACKEND_STAGE")" == "joso" ]] || fail "el staging del backend debe pertenecer a joso."
[[ -z "$(find "$BACKEND_STAGE" -type l -print -quit)" ]] || fail "el staging del backend no puede contener enlaces simbólicos."
[[ -d "$FRONTEND_STAGE" && ! -L "$FRONTEND_STAGE" && -f "$FRONTEND_STAGE/index.html" ]] || fail "falta el frontend compilado o el staging no es válido."
[[ "$(stat -c '%U' "$FRONTEND_STAGE")" == "joso" ]] || fail "el staging del frontend debe pertenecer a joso."
[[ -z "$(find "$FRONTEND_STAGE" -type l -print -quit)" ]] || fail "el staging del frontend no puede contener enlaces simbólicos."
command -v flock >/dev/null
command -v curl >/dev/null
command -v rsync >/dev/null
command -v runuser >/dev/null

# Los datos persistentes se provisionan fuera del despliegue. Fallar antes de
# crear releases o tocar enlaces si falta alguno o joso no puede escribir.
for persistent_dir in \
  /var/lib/dietoexpress/Logs \
  /var/lib/dietoexpress/AlertSpool \
  /var/lib/dietoexpress/documents; do
  [[ -d "$persistent_dir" ]] && runuser -u joso -- test -w "$persistent_dir" ||
    fail "falta el directorio persistente o no es escribible por joso: $persistent_dir"
done

install -d -m 0755 /run/lock
exec 9>"$LOCK_FILE"
flock -n 9 || fail "ya hay otro despliegue activo."

BACKEND_RELEASES="$APP_ROOT/releases"
BACKEND_CURRENT="$APP_ROOT/current"
BACKEND_NEXT="$APP_ROOT/.current-$VERSION"
WEB_NEXT="/var/www/.dietoexpress-$VERSION"
BACKEND_TARGET="$BACKEND_RELEASES/$VERSION"
WEB_TARGET="$WEB_RELEASES/$VERSION"
ACTIVATION_STARTED=0
SCRIPTS_ARCHIVE=""
cleanup_failed_deploy() {
  local status=$?
  if (( status != 0 && ACTIVATION_STARTED == 0 )); then
    rm -rf -- "$BACKEND_TARGET" "$WEB_TARGET" 2>/dev/null || true
    rm -f -- "$BACKEND_NEXT" "$WEB_NEXT" 2>/dev/null || true
    if [[ -n "$SCRIPTS_ARCHIVE" && -d "$SCRIPTS_ARCHIVE" && ! -e "$APP_ROOT/scripts" && ! -L "$APP_ROOT/scripts" ]]; then
      mv "$SCRIPTS_ARCHIVE" "$APP_ROOT/scripts" 2>/dev/null || true
    fi
  fi
  return "$status"
}
trap cleanup_failed_deploy EXIT
PREVIOUS_BACKEND=""
PREVIOUS_WEB=""
if [[ -L "$BACKEND_CURRENT" ]]; then
  PREVIOUS_BACKEND="$(readlink -f "$BACKEND_CURRENT")"
  [[ "$PREVIOUS_BACKEND" == "$BACKEND_RELEASES/"* && -d "$PREVIOUS_BACKEND" ]] || fail "el enlace current apunta fuera de releases."
fi
if [[ -L "$WEB_ROOT" ]]; then
  PREVIOUS_WEB="$(readlink -f "$WEB_ROOT")"
  [[ "$PREVIOUS_WEB" == "$WEB_RELEASES/"* && -d "$PREVIOUS_WEB" ]] || fail "el enlace web apunta fuera de releases."
fi

[[ ! -e "$BACKEND_TARGET" && ! -L "$BACKEND_TARGET" && ! -e "$WEB_TARGET" && ! -L "$WEB_TARGET" ]] || fail "la versión $VERSION ya existe; no se sobrescriben releases."
# Proteger las raíces y los directorios de releases frente al usuario del runner.
[[ -d "$APP_ROOT" && ! -L "$APP_ROOT" ]] || fail "la raíz backend no existe o es un enlace simbólico."
[[ -d "$WEB_ROOT" || -L "$WEB_ROOT" ]] || fail "la raíz web no existe."
[[ ! -L "$BACKEND_RELEASES" ]] || fail "releases backend no puede ser un enlace simbólico."
[[ ! -L "$WEB_RELEASES" ]] || fail "releases web no puede ser un enlace simbólico."
chown root:root "$APP_ROOT"
chmod 0755 "$APP_ROOT"
install -d -o root -g root -m 0755 "$BACKEND_RELEASES" "$WEB_RELEASES"
chown root:root "$BACKEND_RELEASES" "$WEB_RELEASES"
chmod 0755 "$BACKEND_RELEASES" "$WEB_RELEASES"
install -d -o root -g root -m 0755 "$BACKEND_TARGET" "$WEB_TARGET"

# Se construyen las dos versiones completas antes de cambiar ningún enlace.
rsync -a --delete "$BACKEND_STAGE/" "$BACKEND_TARGET/"
rsync -a --delete "$FRONTEND_STAGE/" "$BACKEND_TARGET/wwwroot/"
rsync -a --delete "$FRONTEND_STAGE/" "$WEB_TARGET/"

# Los releases son inmutables para joso y para Nginx: el servicio solo necesita
# leer el backend y Nginx solo necesita leer los estáticos.
chown -R root:root "$BACKEND_TARGET"
find "$BACKEND_TARGET" -type d -exec chmod 0755 {} +
if [[ -d "$BACKEND_TARGET/scripts" ]]; then
  find "$BACKEND_TARGET/scripts" -type f -name '*.sh' -exec chmod 0755 {} +
fi
chown -R root:root "$WEB_TARGET"
find "$WEB_TARGET" -type d -exec chmod 0755 {} +
find "$WEB_TARGET" -type f -exec chmod 0644 {} +

# Fallar antes de activar la versión si Nginx no puede atravesar las rutas
# ni leer el index. No se concede escritura al usuario nginx.
getent passwd nginx >/dev/null || fail "no existe el usuario de servicio nginx."
runuser -u nginx -- test -x /var/www || fail "nginx no puede atravesar /var/www."
runuser -u nginx -- test -x "$WEB_RELEASES" || fail "nginx no puede atravesar el directorio de releases web."
runuser -u nginx -- test -x "$WEB_TARGET" || fail "nginx no puede atravesar el release web."
runuser -u nginx -- test -r "$WEB_TARGET/index.html" || fail "nginx no puede leer el index.html publicado."

restorecon -RF "$WEB_TARGET" "$BACKEND_TARGET"

# Primera migración del backend: guardar una copia ejecutable de la versión
# plana actual antes de activar el nuevo layout. Así también hay rollback en el
# primer despliegue con este mecanismo.
if [[ ! -L "$BACKEND_CURRENT" && -f "$APP_ROOT/Anguloso.Server.dll" ]]; then
  legacy_backend="$BACKEND_RELEASES/legacy-$(date -u +%Y%m%d%H%M%S)"
  install -d -m 0755 "$legacy_backend"
  rsync -a --exclude="/releases/" --exclude="/current" --exclude="/scripts" --exclude="/scripts-legacy-*" "$APP_ROOT/" "$legacy_backend/"
  if [[ -d "$APP_ROOT/scripts" && ! -L "$APP_ROOT/scripts" ]]; then
    rsync -a "$APP_ROOT/scripts/" "$legacy_backend/scripts/"
  fi
  chown -R root:root "$legacy_backend"
  find "$legacy_backend" -type d -exec chmod 0755 {} +
  if [[ -d "$legacy_backend/scripts" ]]; then
    find "$legacy_backend/scripts" -type f -name '*.sh' -exec chmod 0755 {} +
  fi
  ln -s "$legacy_backend" "$BACKEND_CURRENT"
  PREVIOUS_BACKEND="$legacy_backend"
fi
# Primera migración: conservar la web actual como release legacy y convertir
# su ruta pública en un enlace. Nginx mantiene exactamente el mismo root.
if [[ -d "$WEB_ROOT" && ! -L "$WEB_ROOT" ]]; then
  legacy="$WEB_RELEASES/legacy-$(date -u +%Y%m%d%H%M%S)"
  mv "$WEB_ROOT" "$legacy"
  chown -R root:root "$legacy"
  find "$legacy" -type d -exec chmod 0755 {} +
  find "$legacy" -type f -exec chmod 0644 {} +
  ln -s "$legacy" "$WEB_ROOT"
  PREVIOUS_WEB="$legacy"
elif [[ ! -e "$WEB_ROOT" && ! -L "$WEB_ROOT" ]]; then
  ln -s "$WEB_TARGET" "$WEB_ROOT"
fi

# Los scripts operativos tienen una ruta estable para timers y provisioning.
if [[ -d "$APP_ROOT/scripts" && ! -L "$APP_ROOT/scripts" ]]; then
  SCRIPTS_ARCHIVE="$APP_ROOT/scripts-legacy-$(date -u +%Y%m%d%H%M%S)"
  mv "$APP_ROOT/scripts" "$SCRIPTS_ARCHIVE"
fi
if [[ ! -e "$APP_ROOT/scripts" && ! -L "$APP_ROOT/scripts" ]]; then
  ln -s "$BACKEND_CURRENT/scripts" "$APP_ROOT/scripts"
fi

# El servicio usa siempre current; el drop-in también migra instalaciones ya
# existentes sin depender de editar manualmente el unit file del servidor.
install -d -m 0755 /etc/systemd/system/dietoexpress.service.d
cat > /etc/systemd/system/dietoexpress.service.d/10-versioned-release.conf <<EOF
[Service]
WorkingDirectory=$BACKEND_CURRENT
ExecStart=
ExecStart=/usr/bin/dotnet $BACKEND_CURRENT/Anguloso.Server.dll
EOF
systemctl daemon-reload
service_user="$(systemctl show -p User --value dietoexpress.service)"
[[ "$service_user" == "joso" ]] || fail "dietoexpress.service debe ejecutarse como joso (actual: ${service_user:-root})."

# Las funciones de rollback/smoke test se definen antes de tocar los enlaces.
rollback() {
  local reason="$1"
  echo "ERROR: $reason. Iniciando rollback de código." >&2
  if [[ -n "$PREVIOUS_BACKEND" && -d "$PREVIOUS_BACKEND" && -n "$PREVIOUS_WEB" && -d "$PREVIOUS_WEB" ]]; then
    rm -f -- "$BACKEND_NEXT" "$WEB_NEXT"
    if ! ln -s "$PREVIOUS_BACKEND" "$BACKEND_NEXT" ||
       ! ln -s "$PREVIOUS_WEB" "$WEB_NEXT" ||
       ! mv -Tf "$BACKEND_NEXT" "$BACKEND_CURRENT" ||
       ! mv -Tf "$WEB_NEXT" "$WEB_ROOT"; then
      echo "CRITICAL: no se pudieron restaurar ambos enlaces; se detiene el servicio." >&2
      systemctl stop dietoexpress.service || true
      exit 1
    fi
    systemctl restart dietoexpress.service || true
    if smoke_test; then
      echo "Rollback completado; vuelve a estar activa la versión anterior."
    else
      echo "CRITICAL: rollback aplicado pero la versión anterior tampoco supera el smoke test." >&2
    fi
  else
    echo "CRITICAL: no hay una versión anterior completa a la que volver. Se detiene el servicio." >&2
    systemctl stop dietoexpress.service || true
  fi
  exit 1
}

smoke_test() {
  for attempt in $(seq 1 12); do
    if systemctl is-active --quiet dietoexpress.service &&
      curl --fail --silent --show-error --max-time 5 \
        --resolve "$DOMAIN:443:127.0.0.1" "https://$DOMAIN/" \
        -o /tmp/dietoexpress-smoke-home.html 2>/dev/null &&
      grep -Fq "DietoExpress" /tmp/dietoexpress-smoke-home.html; then
      break
    fi
    [[ "$attempt" -lt 12 ]] || return 1
    sleep 5
  done
  for attempt in $(seq 1 12); do
    if curl --fail --silent --show-error --max-time 5 \
      --resolve "$DOMAIN:443:127.0.0.1" \
      -H 'Accept: application/json' \
      "https://$DOMAIN/api/directory/professionals" \
      -o /tmp/dietoexpress-smoke-directory.json 2>/dev/null &&
      python3 -c 'import json; data=json.load(open("/tmp/dietoexpress-smoke-directory.json")); assert isinstance(data,list)' 2>/dev/null; then
      return 0
    fi
    [[ "$attempt" -lt 12 ]] || return 1
    sleep 5
  done
  return 1
}

# Preparar enlaces temporales en el mismo filesystem y sustituirlos mediante
# rename(2). Si cualquiera de los dos cambios falla, restaurar ambos destinos.
rm -f -- "$BACKEND_NEXT" "$WEB_NEXT"
ln -s "$BACKEND_TARGET" "$BACKEND_NEXT"
ln -s "$WEB_TARGET" "$WEB_NEXT"
ACTIVATION_STARTED=1
if ! mv -Tf "$BACKEND_NEXT" "$BACKEND_CURRENT"; then
  rollback "no se pudo activar el enlace del backend"
fi
if ! mv -Tf "$WEB_NEXT" "$WEB_ROOT"; then
  rollback "no se pudo activar el enlace del frontend"
fi

if ! systemctl enable dietoexpress.service || ! systemctl restart dietoexpress.service; then
  rollback "no se pudo reiniciar systemd"
fi
if ! smoke_test; then
  systemctl status dietoexpress.service --no-pager || true
  journalctl -u dietoexpress.service -n 100 --no-pager || true
  rollback "falló el smoke test de frontend o API"
fi

# La versión anterior se conserva siempre, incluso con KEEP_RELEASES=1.
prune_releases() {
  local directory="$1" current="$2"
  mapfile -t releases < <(find "$directory" -mindepth 1 -maxdepth 1 -type d -printf '%T@ %f\n' | sort -nr | cut -d' ' -f2-)
  local kept=0 name path
  for name in "${releases[@]}"; do
    path="$directory/$name"
    [[ "$path" == "$current" ]] && continue
    if (( kept < KEEP_RELEASES - 1 )); then
      ((kept+=1))
    else
      rm -rf -- "$path"
    fi
  done
}
prune_releases "$BACKEND_RELEASES" "$BACKEND_TARGET"
prune_releases "$WEB_RELEASES" "$WEB_TARGET"

echo "Despliegue correcto: $VERSION"
echo "Backend activo: $(readlink -f "$BACKEND_CURRENT")"
echo "Frontend activo: $(readlink -f "$WEB_ROOT")"
