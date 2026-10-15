#!/usr/bin/env bash
set -euo pipefail
[[ "$(id -u)" -eq 0 && $# -eq 1 ]] || { echo 'Uso: helper NOMBRE-completo.tar.gz' >&2; exit 2; }
archive="$1"
[[ "$archive" =~ ^dietoexpress-postgresql-[A-Za-z0-9_.-]+-completo\.tar\.gz$ ]] || exit 2
BACKUP_DIR=/var/lib/dietoexpress-backups
RCLONE_CONFIG=/home/joso/.config/rclone/rclone.conf
[[ -d "$BACKUP_DIR" && -f "$RCLONE_CONFIG" && ! -L "$RCLONE_CONFIG" && "$(stat -c '%U:%G:%a' "$RCLONE_CONFIG")" == 'joso:joso:600' ]] || { echo 'Configuración local no válida' >&2; exit 1; }
tmp="$(mktemp -d /var/tmp/dietoexpress-drive.XXXXXX)"
trap 'rm -rf -- "$tmp"' EXIT
runuser -u joso -- env RCLONE_CONFIG="$RCLONE_CONFIG" /usr/bin/rclone copyto "gdrive-crypt:$archive" "$tmp/$archive"
runuser -u joso -- env RCLONE_CONFIG="$RCLONE_CONFIG" /usr/bin/rclone copyto "gdrive-crypt:$archive.sha256" "$tmp/$archive.sha256"
(cd "$tmp" && sha256sum -c "$archive.sha256")
python3 - "$tmp/$archive" "$archive" <<'PY'
import sys,tarfile
with tarfile.open(sys.argv[1],"r:gz") as t:
 names=set()
 for m in t.getmembers():
  n=m.name
  if n.startswith('/') or '\\' in n or any(p in ('','.','..') for p in n.rstrip('/').split('/')) or not (m.isfile() or m.isdir()): raise SystemExit('Paquete inseguro')
  names.add(n)
 dumps=[n for n in names if n.endswith('.dump')]
 if len(dumps)!=1: raise SystemExit('Dump no único')
 expected_dump=sys.argv[2][:-len('-completo.tar.gz')]+'.dump'
 if dumps[0]!=expected_dump: raise SystemExit('El dump no coincide con el nombre del paquete')
 stem=dumps[0][:-5]
 docs='dietoexpress-documents-'+stem.removeprefix('dietoexpress-postgresql-')
 required={dumps[0],stem+'.sha256',stem+'.txt',docs+'.sha256'}
 if not required.issubset(names) or not (docs in names or docs+'/' in names) or any(n not in required and n!=docs and n!=docs+'/' and not n.startswith(docs+'/') for n in names): raise SystemExit('Componentes incorrectos')
PY
stage="$tmp/extracted"
install -d -o root -g root -m 0700 "$stage"
tar --extract --gzip --file "$tmp/$archive" --directory "$stage" --no-same-owner --no-same-permissions
dump="$(find "$stage" -maxdepth 1 -type f -name 'dietoexpress-postgresql-*.dump' -printf '%f\n')"
[[ -n "$dump" && "$(printf '%s\n' "$dump" | wc -l)" -eq 1 ]] || exit 1
stem="${dump%.dump}"; docs="dietoexpress-documents-${stem#dietoexpress-postgresql-}"
(cd "$stage" && sha256sum -c "$stem.sha256")
(cd "$stage/$docs" && sha256sum -c "$stage/$docs.sha256")
a="$(awk 'NR==1 {print $1}' "$stage/$stem.sha256")"; b="$(sed -n 's/^sha256=//p' "$stage/$stem.txt" | head -n1)"
c="$(sha256sum "$stage/$docs.sha256" | awk '{print $1}')"; d="$(sed -n 's/^documents_manifest_sha256=//p' "$stage/$stem.txt" | head -n1)"
[[ -n "$a" && "$a" == "$b" && -n "$d" && "$c" == "$d" ]] || { echo 'Manifiestos incoherentes' >&2; exit 1; }
pg_restore --list "$stage/$dump" >/dev/null
for x in "$dump" "$stem.sha256" "$stem.txt" "$docs" "$docs.sha256"; do [[ ! -e "$BACKUP_DIR/$x" && ! -L "$BACKUP_DIR/$x" ]] || { echo "Ya existe $x; no se sobrescribe" >&2; exit 1; }; done
group="${DIETOEXPRESS_BACKUP_GROUP:-joso}"
install -o root -g "$group" -m 0640 "$stage/$dump" "$BACKUP_DIR/$dump"
install -o root -g "$group" -m 0640 "$stage/$stem.sha256" "$BACKUP_DIR/$stem.sha256"
install -o root -g "$group" -m 0640 "$stage/$stem.txt" "$BACKUP_DIR/$stem.txt"
install -o root -g "$group" -m 0640 "$stage/$docs.sha256" "$BACKUP_DIR/$docs.sha256"
cp -a --no-preserve=ownership "$stage/$docs" "$BACKUP_DIR/$docs"
chown -R "root:$group" "$BACKUP_DIR/$docs"
find "$BACKUP_DIR/$docs" -type d -exec chmod 0750 {} +
find "$BACKUP_DIR/$docs" -type f -exec chmod 0640 {} +
echo "Backup importado: $dump"
