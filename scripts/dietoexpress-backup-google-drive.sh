#!/usr/bin/env bash
set -euo pipefail

# Sube snapshots completos de PostgreSQL + documentos a un remote rclone
# cifrado. No sube dietoexpress.env ni snapshots históricos que carezcan de
# documentos, y nunca borra contenido del destino remoto.
BACKUP_DIR="${DIETOEXPRESS_BACKUP_DIR:-/var/lib/dietoexpress-backups}"
DRIVE_REMOTE="${DIETOEXPRESS_DRIVE_REMOTE:-gdrive-crypt:}"
RCLONE_BIN="${DIETOEXPRESS_RCLONE_BIN:-/usr/bin/rclone}"

[[ "$BACKUP_DIR" == /* && -d "$BACKUP_DIR" && ! -L "$BACKUP_DIR" ]] || {
  echo "ERROR: almacén local de backups no válido: $BACKUP_DIR" >&2
  exit 1
}
[[ "$DRIVE_REMOTE" =~ ^[A-Za-z0-9_.-]+:.*$ && "${DRIVE_REMOTE%%:*}" == "gdrive-crypt" ]] || {
  echo "ERROR: DIETOEXPRESS_DRIVE_REMOTE debe apuntar al remote cifrado gdrive-crypt:, nunca al remote Drive sin cifrar." >&2
  exit 1
}
[[ -x "$RCLONE_BIN" ]] || { echo "ERROR: no existe rclone ejecutable: $RCLONE_BIN" >&2; exit 1; }
"$RCLONE_BIN" listremotes | grep -Fxq "${DRIVE_REMOTE%%:*}:" || {
  echo "ERROR: el remote ${DRIVE_REMOTE%%:*}: no está configurado en rclone." >&2
  exit 1
}

tmp_dir="$(mktemp -d "${TMPDIR:-/tmp}/dietoexpress-drive.XXXXXX")"
chmod 0700 "$tmp_dir"
cleanup() { rm -rf -- "$tmp_dir"; }
trap cleanup EXIT

mapfile -t remote_files < <("$RCLONE_BIN" lsf --files-only "$DRIVE_REMOTE")
has_remote_file() {
  local wanted="$1" item
  for item in "${remote_files[@]}"; do
    [[ "$item" != "$wanted" ]] || return 0
  done
  return 1
}

eligible=0
uploaded=0
skipped_legacy=0
failed=0

shopt -s nullglob
dumps=("$BACKUP_DIR"/dietoexpress-postgresql-*.dump)
if (("${#dumps[@]}" == 0)); then
  echo "ERROR: no hay dumps PostgreSQL locales en $BACKUP_DIR." >&2
  exit 1
fi

for dump_path in "${dumps[@]}"; do
  dump_name="$(basename "$dump_path")"
  stem="${dump_name%.dump}"
  suffix="${stem#dietoexpress-postgresql-}"
  dump_manifest="${stem}.sha256"
  metadata="${stem}.txt"
  documents_name="dietoexpress-documents-$suffix"
  documents_manifest="${documents_name}.sha256"
  documents_path="$BACKUP_DIR/$documents_name"

  # Los backups anteriores a las snapshots documentales no son restaurables
  # como conjunto y se omiten, sin impedir subir snapshots válidos más nuevos.
  if [[ ! -d "$documents_path" || -L "$documents_path" || ! -f "$BACKUP_DIR/$documents_manifest" ]]; then
    echo "AVISO: se omite backup histórico sin snapshot documental: $dump_name" >&2
    ((skipped_legacy+=1))
    continue
  fi
  if [[ ! -f "$BACKUP_DIR/$dump_manifest" || ! -f "$BACKUP_DIR/$metadata" ]]; then
    echo "ERROR: faltan checksum o metadatos para $dump_name." >&2
    ((failed+=1))
    continue
  fi

  eligible=$((eligible + 1))
  if find "$documents_path" -type l -print -quit | grep -q . ||
     find "$documents_path" ! -type d ! -type f -print -quit | grep -q .; then
    echo "ERROR: snapshot documental con enlaces simbólicos o archivos especiales: $documents_name" >&2
    ((failed+=1))
    continue
  fi
  if ! (cd "$BACKUP_DIR" && sha256sum -c "$dump_manifest" >/dev/null) ||
     ! (cd "$documents_path" && sha256sum -c "$BACKUP_DIR/$documents_manifest" >/dev/null); then
    echo "ERROR: checksum inválido en $dump_name; no se sube." >&2
    ((failed+=1))
    continue
  fi
  metadata_dump_hash="$(sed -n 's/^sha256=//p' "$BACKUP_DIR/$metadata" | head -n1)"
  manifest_dump_hash="$(awk 'NR == 1 {print $1}' "$BACKUP_DIR/$dump_manifest")"
  metadata_documents_hash="$(sed -n 's/^documents_manifest_sha256=//p' "$BACKUP_DIR/$metadata" | head -n1)"
  actual_documents_hash="$(sha256sum "$BACKUP_DIR/$documents_manifest" | awk '{print $1}')"
  if [[ -z "$metadata_dump_hash" || "$metadata_dump_hash" != "$manifest_dump_hash" ||
        -z "$metadata_documents_hash" || "$metadata_documents_hash" != "$actual_documents_hash" ]]; then
    echo "ERROR: los metadatos no coinciden con los manifiestos de $dump_name; no se sube." >&2
    ((failed+=1))
    continue
  fi

  bundle_name="${stem}-completo.tar.gz"
  bundle_path="$tmp_dir/$bundle_name"
  bundle_checksum="$tmp_dir/$bundle_name.sha256"

  if has_remote_file "$bundle_name" && has_remote_file "$bundle_name.sha256"; then
    echo "Ya existe en Drive el snapshot completo: $bundle_name"
    continue
  fi

  tar_entries=("$dump_name" "$dump_manifest" "$metadata" "$documents_name" "$documents_manifest")
  if ! tar --create --gzip --file "$bundle_path" --directory "$BACKUP_DIR" "${tar_entries[@]}"; then
    echo "ERROR: no se pudo empaquetar $dump_name." >&2
    ((failed+=1))
    continue
  fi
  (cd "$tmp_dir" && sha256sum "$bundle_name" > "$bundle_name.sha256")

  # Evitar una barra inicial cuando el remote apunta a su raíz (gdrive-crypt:).
  if [[ "$DRIVE_REMOTE" == *: ]]; then
    remote_bundle="${DRIVE_REMOTE}${bundle_name}"
  else
    remote_bundle="${DRIVE_REMOTE}/${bundle_name}"
  fi
  # El destino debe ser el remote crypt, no el remote de Drive sin cifrar.
  if ! "$RCLONE_BIN" copyto "$bundle_path" "$remote_bundle" \
       --retries 3 --low-level-retries 10 --transfers 1 --checkers 4; then
    echo "ERROR: no se pudo subir $bundle_name a $DRIVE_REMOTE." >&2
    ((failed+=1))
    continue
  fi
  if [[ "$DRIVE_REMOTE" == *: ]]; then
    remote_checksum="${DRIVE_REMOTE}${bundle_name}.sha256"
  else
    remote_checksum="${DRIVE_REMOTE}/${bundle_name}.sha256"
  fi
  if ! "$RCLONE_BIN" copyto "$bundle_checksum" "$remote_checksum" \
       --retries 3 --low-level-retries 10 --transfers 1 --checkers 4; then
    echo "ERROR: se subió $bundle_name, pero falló su checksum remoto." >&2
    ((failed+=1))
    continue
  fi

  remote_files+=("$bundle_name" "$bundle_name.sha256")
  uploaded=$((uploaded + 1))
  echo "Snapshot completo subido y checksum publicado: $bundle_name"
done

if (( eligible == 0 )); then
  echo "ERROR: no se encontró ningún snapshot completo local para subir." >&2
  exit 1
fi
if (( failed > 0 )); then
  echo "ERROR: fallaron $failed operaciones de backup remoto; subidos=$uploaded, históricos omitidos=$skipped_legacy." >&2
  exit 1
fi

echo "Backup remoto terminado: nuevos=$uploaded, completos_comprobados=$eligible, históricos_omitidos=$skipped_legacy."
