#!/usr/bin/env bash
set -Eeuo pipefail

# Punto de entrada no privilegiado del runner self-hosted.
# Toda operación privilegiada queda encapsulada en un helper root-owned.
[[ "$(id -un)" == "joso" ]] || {
  echo "ERROR: el despliegue debe ejecutarse como joso." >&2
  exit 1
}

VERSION="${DIETOEXPRESS_VERSION:-}"
[[ "$VERSION" =~ ^[A-Za-z0-9._-]{7,80}$ ]] || {
  echo "ERROR: identificador de versión no válido." >&2
  exit 1
}

[[ -d /tmp/dietoexpress-backend && -f /tmp/dietoexpress-backend/Anguloso.Server.dll ]] || {
  echo "ERROR: falta el backend publicado en /tmp/dietoexpress-backend." >&2
  exit 1
}
[[ -d /tmp/dietoexpress-frontend && -f /tmp/dietoexpress-frontend/index.html ]] || {
  echo "ERROR: falta el frontend compilado en /tmp/dietoexpress-frontend." >&2
  exit 1
}

command -v sudo >/dev/null 2>&1 || {
  echo "ERROR: sudo no está instalado." >&2
  exit 1
}

# -n evita que el workflow se quede esperando una contraseña en un runner
# sin terminal. La autorización se limita a este helper y a un SHA validado.
sudo -n /usr/local/sbin/dietoexpress-deploy "$VERSION"
