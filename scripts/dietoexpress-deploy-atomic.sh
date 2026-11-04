#!/usr/bin/env bash
set -Eeuo pipefail

# Publicación versionada de backend y frontend. Ejecutar como joso desde CI.
APP_ROOT="${DIETOEXPRESS_APP_DIR:-/opt/dietoexpress}"
WEB_ROOT="${DIETOEXPRESS_WEB_DIR:-/var/www/dietoexpress}"
WEB_RELEASES="${DIETOEXPRESS_WEB_RELEASES:-/var/www/dietoexpress-releases}"
BACKEND_STAGE="${DIETOEXPRESS_BACKEND_STAGE:-/tmp/dietoexpress-backend}"
FRONTEND_STAGE="${DIETOEXPRESS_FRONTEND_STAGE:-/tmp/dietoexpress-frontend}"
VERSION="${DIETOEXPRESS_VERSION:-}"
DOMAIN="${DIETOEXPRESS_DOMAIN:-jlhitap.duckdns.org}"
KEEP_RELEASES="${DIETOEXPRESS_KEEP_RELEASES:-5}"
LOCK_FILE="${DIETOEXPRESS_DEPLOY_LOCK:-/tmp/dietoexpress-deploy.lock}"

fail() { echo "ERROR: $*" >&2; exit 1; }
[[ "$(id -un)" == "joso" ]] || fail "el despliegue debe ejecutarse como joso."
[[ "$VERSION" =~ ^[A-Za-z0-9._-]{7,80}$ ]] || fail "identificador de versión no válido."
[[ "$KEEP_RELEASES" =~ ^[1-9][0-9]*$ ]] || fail "retención no válida."
[[ -d "$BACKEND_STAGE" && -f "$BACKEND_STAGE/Anguloso.Server.dll" ]] || fail "falta el backend publicado."
[[ -d "$FRONTEND_STAGE" && -f "$FRONTEND_STAGE/index.html" ]] || fail "falta el frontend compilado."
command -v flock >/dev/null
command -v curl >/dev/null
command -v rsync >/dev/null

exec 9>"$LOCK_FILE"
flock -n 9 || fail "ya hay otro despliegue activo."

BACKEND_RELEASES="$APP_ROOT/releases"
BACKEND_CURRENT="$APP_ROOT/current"
BACKEND_NEXT="$APP_ROOT/.current-$VERSION"
WEB_NEXT="/var/www/.dietoexpress-$VERSION"
BACKEND_TARGET="$BACKEND_RELEASES/$VERSION"
WEB_TARGET="$WEB_RELEASES/$VERSION"
PREVIOUS_BACKEND=""
PREVIOUS_WEB=""
if [[ -L "$BACKEND_CURRENT" ]]; then PREVIOUS_BACKEND="$(readlink -f "$BACKEND_CURRENT")"; fi
if [[ -L "$WEB_ROOT" ]]; then PREVIOUS_WEB="$(readlink -f "$WEB_ROOT")"; fi

[[ ! -e "$BACKEND_TARGET" && ! -e "$WEB_TARGET" ]] || fail "la versión $VERSION ya existe; no se sobrescriben releases."
install -d -m 0755 "$BACKEND_RELEASES"
sudo install -d -o joso -g joso -m 0755 "$WEB_RELEASES"
install -d -m 0755 "$BACKEND_TARGET"
sudo install -d -o joso -g joso -m 0755 "$WEB_TARGET"

# Se construyen las dos versiones completas antes de cambiar ningún enlace.
rsync -a --delete "$BACKEND_STAGE/" "$BACKEND_TARGET/"
rsync -a --delete "$FRONTEND_STAGE/" "$BACKEND_TARGET/wwwroot/"
sudo rsync -a --delete "$FRONTEND_STAGE/" "$WEB_TARGET/"
chmod 750 "$BACKEND_TARGET"/scripts/*.sh 2>/dev/null || true

# Primera migración del backend: guardar una copia ejecutable de la versión
# plana actual antes de activar el nuevo layout. Así también hay rollback en el
# primer despliegue con este mecanismo.
if [[ ! -L "$BACKEND_CURRENT" && -f "$APP_ROOT/Anguloso.Server.dll" ]]; then
  legacy_backend="$BACKEND_RELEASES/legacy-$(date -u +%Y%m%d%H%M%S)"
  install -d -m 0755 "$legacy_backend"
  rsync -a --exclude="/releases/" --exclude="/current" --exclude="/scripts" --exclude="/scripts-legacy-*" "$APP_ROOT/" "$legacy_backend/"
  if [[ -d "$APP_ROOT/scripts" && ! -L "$APP_ROOT/scripts" ]]; then
    mv "$APP_ROOT/scripts" "$legacy_backend/scripts"
  fi
  ln -s "$legacy_backend" "$BACKEND_CURRENT"
  PREVIOUS_BACKEND="$legacy_backend"
fi
# Primera migración: conservar la web actual como release legacy y convertir
# su ruta pública en un enlace. Nginx mantiene exactamente el mismo root.
if [[ -d "$WEB_ROOT" && ! -L "$WEB_ROOT" ]]; then
  legacy="$WEB_RELEASES/legacy-$(date -u +%Y%m%d%H%M%S)"
  sudo mv "$WEB_ROOT" "$legacy"
  sudo ln -s "$legacy" "$WEB_ROOT"
  PREVIOUS_WEB="$legacy"
elif [[ ! -e "$WEB_ROOT" && ! -L "$WEB_ROOT" ]]; then
  sudo ln -s "$WEB_TARGET" "$WEB_ROOT"
fi

# Los scripts operativos tienen una ruta estable para timers y provisioning.
if [[ -d "$APP_ROOT/scripts" && ! -L "$APP_ROOT/scripts" ]]; then
  mv "$APP_ROOT/scripts" "$APP_ROOT/scripts-legacy-$(date -u +%Y%m%d%H%M%S)"
fi
if [[ ! -e "$APP_ROOT/scripts" && ! -L "$APP_ROOT/scripts" ]]; then
  ln -s "$BACKEND_CURRENT/scripts" "$APP_ROOT/scripts"
fi

# El servicio usa siempre current; el drop-in también migra instalaciones ya
# existentes sin depender de editar manualmente el unit file del servidor.
sudo install -d -m 0755 /etc/systemd/system/dietoexpress.service.d
sudo tee /etc/systemd/system/dietoexpress.service.d/10-versioned-release.conf >/dev/null <<EOF
[Service]
WorkingDirectory=$BACKEND_CURRENT
ExecStart=
ExecStart=/usr/bin/dotnet $BACKEND_CURRENT/Anguloso.Server.dll
EOF
sudo systemctl daemon-reload

# Preparar enlaces temporales en el mismo filesystem y sustituirlos mediante
# rename(2), evitando estados intermedios o mezclas de archivos.
ln -s "$BACKEND_TARGET" "$BACKEND_NEXT"
sudo ln -s "$WEB_TARGET" "$WEB_NEXT"
mv -Tf "$BACKEND_NEXT" "$BACKEND_CURRENT"
sudo mv -Tf "$WEB_NEXT" "$WEB_ROOT"

rollback() {
  local reason="$1"
  echo "ERROR: $reason. Iniciando rollback de código." >&2
  if [[ -n "$PREVIOUS_BACKEND" && -d "$PREVIOUS_BACKEND" && -n "$PREVIOUS_WEB" && -d "$PREVIOUS_WEB" ]]; then
    ln -s "$PREVIOUS_BACKEND" "$BACKEND_NEXT"
    sudo ln -s "$PREVIOUS_WEB" "$WEB_NEXT"
    mv -Tf "$BACKEND_NEXT" "$BACKEND_CURRENT"
    sudo mv -Tf "$WEB_NEXT" "$WEB_ROOT"
    sudo systemctl restart dietoexpress.service || true
    if smoke_test; then
      echo "Rollback completado; vuelve a estar activa la versión anterior."
    else
      echo "CRITICAL: rollback aplicado pero la versión anterior tampoco supera el smoke test." >&2
    fi
  else
    echo "CRITICAL: no hay una versión anterior completa a la que volver. Se detiene el servicio." >&2
    sudo systemctl stop dietoexpress.service || true
  fi
  exit 1
}

smoke_test() {
  for attempt in $(seq 1 12); do
    if sudo systemctl is-active --quiet dietoexpress.service &&
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

if ! sudo systemctl enable dietoexpress.service || ! sudo systemctl restart dietoexpress.service; then
  rollback "no se pudo reiniciar systemd"
fi
if ! smoke_test; then
  sudo systemctl status dietoexpress.service --no-pager || true
  sudo journalctl -u dietoexpress.service -n 100 --no-pager || true
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
sudo bash -c "$(declare -f prune_releases); prune_releases '$WEB_RELEASES' '$WEB_TARGET'"

echo "Despliegue correcto: $VERSION"
echo "Backend activo: $(readlink -f "$BACKEND_CURRENT")"
echo "Frontend activo: $(readlink -f "$WEB_ROOT")"
