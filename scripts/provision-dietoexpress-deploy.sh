#!/usr/bin/env bash
set -Eeuo pipefail

# Provisioning inicial/actualización del helper privilegiado de despliegue.
# Ejecutar explícitamente como administrador, indicando el helper revisado.
[[ "$(id -u)" -eq 0 ]] || {
  echo "ERROR: ejecutar este provisioning como root." >&2
  exit 1
}
[[ "$#" -eq 1 ]] || {
  echo "Uso: sudo bash provision-dietoexpress-deploy.sh /ruta/revisada/dietoexpress-deploy-root.sh" >&2
  exit 2
}

SOURCE_HELPER="$1"
ROOT_HELPER="/usr/local/sbin/dietoexpress-deploy"
SUDOERS_FILE="/etc/sudoers.d/dietoexpress-deploy"

[[ "$SOURCE_HELPER" == /* && -f "$SOURCE_HELPER" && ! -L "$SOURCE_HELPER" ]] || {
  echo "ERROR: la ruta del helper debe ser absoluta y apuntar a un archivo regular, no a un enlace." >&2
  exit 1
}
bash -n "$SOURCE_HELPER"
install -d -o root -g root -m 0755 /usr/local/sbin
install -o root -g root -m 0750 "$SOURCE_HELPER" "$ROOT_HELPER"

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT
cat > "$tmp" <<'EOF'
# El runner solo puede ejecutar el helper de despliegue con un identificador
# de versión validado; no obtiene permisos sudo para comandos genéricos.
joso ALL=(root) NOPASSWD: /usr/local/sbin/dietoexpress-deploy ^[A-Za-z0-9._-]{7,80}$
EOF
chown root:root "$tmp"
chmod 0440 "$tmp"
visudo -cf "$tmp"
install -o root -g root -m 0440 "$tmp" "$SUDOERS_FILE"
visudo -c

echo "Helper instalado: $ROOT_HELPER"
stat -c 'Propietario/permisos: %U:%G %a' "$ROOT_HELPER"
echo "Regla sudoers instalada: $SUDOERS_FILE"
