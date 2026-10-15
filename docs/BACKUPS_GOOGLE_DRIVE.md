# Backups cifrados de DietoExpress en Google Drive

## Objetivo y arquitectura

DietoExpress mantiene dos niveles independientes:

1. **Copia local diaria:** PostgreSQL completo más un snapshot completo de documentos y PDF. Los snapshots documentales reutilizan archivos idénticos mediante hard links creados por `rsync`; cada domingo se crea una nueva base documental completa.
2. **Copia externa cifrada:** `rclone` sube cada snapshot local completo a Google Drive a través del remote cifrado `gdrive-crypt:`. La subida se ejecuta en un servicio systemd separado para que un fallo de red/Drive no invalide el backup local.

`rsync` solo optimiza snapshots en el disco local; no sincroniza directamente con Google Drive. Para Drive se usa `rclone` y el remote **crypt**. No subir los archivos mediante el remote sin cifrar.

Cada paquete `dietoexpress-postgresql-<timestamp>-completo.tar.gz` incluye:
- dump PostgreSQL `.dump`;
- checksum SHA-256 y metadatos del dump;
- carpeta `dietoexpress-documents-<timestamp>` con los PDF/documentos y otros archivos persistentes de DietoExpress;
- manifiesto SHA-256 del inventario documental.

También se publica un archivo `.tar.gz.sha256` para verificar el paquete descargado. El paquete no contiene `dietoexpress.env`, contraseñas ni otros secretos de configuración.

Los backups históricos que solo contienen PostgreSQL se omiten: no permiten recuperar base de datos y documentos en un mismo punto temporal.

## Requisitos en Fedora

Instalar las herramientas necesarias:

~~~bash
sudo dnf install -y rsync rclone
rsync --version
rclone version
~~~

La configuración rclone utilizada por el servicio es:

- archivo: `/home/joso/.config/rclone/rclone.conf`;
- propietario y permisos: `joso:joso`, modo `0600`;
- remote requerido: `gdrive-crypt:`.

Comprobar sin mostrar ni copiar credenciales:

~~~bash
sudo -u joso env RCLONE_CONFIG=/home/joso/.config/rclone/rclone.conf rclone listremotes
sudo -u joso env RCLONE_CONFIG=/home/joso/.config/rclone/rclone.conf rclone lsd gdrive-crypt:
sudo stat -c '%U:%G:%a %n' /home/joso/.config/rclone/rclone.conf
~~~

La salida de `listremotes` debe incluir `gdrive-crypt:`. El remote debe ser de tipo `crypt` y apuntar al directorio de destino deseado en el remote Drive subyacente. No pegues aquí el contenido de `rclone.conf`: contiene material sensible.

## Configuración de DietoExpress

En `/etc/dietoexpress/dietoexpress.env`, conservar todas las variables existentes y comprobar que estén definidas estas rutas/valores:

~~~text
DIETOEXPRESS_BACKUP_DIR=/var/lib/dietoexpress-backups
DIETOEXPRESS_DATABASE=<NOMBRE_DE_LA_BASE>
DIETOEXPRESS_DB_PORT=5432
DIETOEXPRESS_DOCUMENTS_PATH=/var/lib/dietoexpress/documents
DIETOEXPRESS_DRIVE_REMOTE=gdrive-crypt:
~~~

Si `DIETOEXPRESS_DRIVE_REMOTE` se omite, el uploader utiliza `gdrive-crypt:`. No reemplazar el archivo completo ni modificar sus secretos para añadir estas líneas. El directorio documental debe ser el mismo que utiliza el servicio web.

La configuración de despliegue de la aplicación no instala ni actualiza automáticamente los helpers root-owned o las unidades systemd. Después de desplegar los cambios de backup, ejecutar una vez:

~~~bash
sudo /opt/dietoexpress/scripts/provision-dietoexpress-backup-automation.sh
sudo systemctl daemon-reload
sudo systemctl status dietoexpress-backup.timer --no-pager
sudo systemctl status dietoexpress-backup-drive.timer --no-pager
~~~

El provisioning comprueba que `rsync`, `rclone`, el archivo de configuración protegido y el remote cifrado existen. Si falta alguno, se detiene con error en lugar de habilitar una subida sin cifrar o una automatización incompleta.

## Ejecución y verificación

El backup local está programado diariamente alrededor de las 02:00. La subida externa se programa diariamente alrededor de las 03:00, con retraso aleatorio de hasta diez minutos. La subida escanea los snapshots locales, valida ambos manifiestos y sube los paquetes completos que aún no existen en Drive. Los paquetes ya publicados se omiten; los backups incompletos se rechazan.

Para probar manualmente, primero genera una copia local y después inicia la subida:

~~~bash
sudo systemctl start dietoexpress-backup.service
sudo systemctl status dietoexpress-backup.service --no-pager
sudo systemctl start dietoexpress-backup-drive.service
sudo systemctl status dietoexpress-backup-drive.service --no-pager
~~~

Consultar logs y temporizadores:

~~~bash
sudo journalctl -u dietoexpress-backup.service --since "24 hours ago" --no-pager
sudo journalctl -u dietoexpress-backup-drive.service --since "24 hours ago" --no-pager
systemctl list-timers 'dietoexpress-backup*'
sudo -u joso env RCLONE_CONFIG=/home/joso/.config/rclone/rclone.conf rclone lsf gdrive-crypt:
~~~

La presencia de un archivo en Drive no demuestra por sí sola que se pueda restaurar. Debe probarse periódicamente la descarga, la verificación SHA-256 y una restauración en una base/servidor aislado.

El servicio de subida **no borra archivos remotos**. Esto protege contra la eliminación accidental de copias fuera del servidor, pero implica que el almacenamiento de Drive crecerá con el tiempo; cualquier política de limpieza remota debe definirse y probarse por separado. Los antiguos `.dump`, `.sha256` y `.txt` subidos antes de esta actualización pueden permanecer en Drive, pero no sustituyen a los nuevos paquetes completos.

## Recuperar un paquete desde Drive

1. Listar los paquetes disponibles sin mostrar el contenido de la configuración:

   ~~~bash
   sudo -u joso env RCLONE_CONFIG=/home/joso/.config/rclone/rclone.conf rclone lsf gdrive-crypt:
   ~~~

2. Descargar el paquete y su checksum a un directorio temporal privado. Sustituir el nombre por uno listado en el remote:

   ~~~bash
   sudo install -d -o joso -g joso -m 0700 /home/joso/dietoexpress-drive-restore
   cd /home/joso/dietoexpress-drive-restore
   sudo -u joso env RCLONE_CONFIG=/home/joso/.config/rclone/rclone.conf rclone copyto "gdrive-crypt:<PAQUETE>.tar.gz" "<PAQUETE>.tar.gz"
   sudo -u joso env RCLONE_CONFIG=/home/joso/.config/rclone/rclone.conf rclone copyto "gdrive-crypt:<PAQUETE>.tar.gz.sha256" "<PAQUETE>.tar.gz.sha256"
   sha256sum -c "<PAQUETE>.tar.gz.sha256"
   tar -tzf "<PAQUETE>.tar.gz"
   ~~~

3. Antes de extraer, comprobar que el listado contiene únicamente los nombres esperados del snapshot, sin rutas absolutas ni componentes `..`. No extraer un archivo que no supere la verificación del checksum o que tenga rutas inesperadas.
4. Con una copia verificada, extraer como administrador en un directorio de preparación, revisar propietarios/permisos y transferir el dump, su manifiesto, metadatos, carpeta documental y manifiesto documental al almacén `/var/lib/dietoexpress-backups`. No sobrescribir silenciosamente una copia existente.
5. Ejecutar el preflight de la aplicación sobre el dump recuperado:

   ~~~bash
   sudo /opt/dietoexpress/scripts/dietoexpress-restore.sh --verify <NOMBRE_DEL_DUMP>.dump
   ~~~

6. Realizar la restauración únicamente mediante el procedimiento controlado documentado en [GUIA_DESPLIEGUE_LINUX_PRODUCCION.md](GUIA_DESPLIEGUE_LINUX_PRODUCCION.md) y [MANUAL_SUPERADMIN_BACKUPS.md](MANUAL_SUPERADMIN_BACKUPS.md). No ejecutar `pg_restore` directamente contra producción.

## Seguridad y operación

- No subir `/etc/dietoexpress/dietoexpress.env` al paquete ordinario de Drive.
- No utilizar `gdrive:` directamente para los paquetes; la subida debe pasar por `gdrive-crypt:`.
- No poner tokens OAuth, claves de cifrado ni contenido de `rclone.conf` en Git, logs, incidencias o mensajes.
- Un fallo de Google Drive no elimina ni invalida el backup local. Revisar el servicio de Drive y reintentar tras corregir la conexión.
- La retención local es de 14 snapshots diarios y cuatro snapshots automáticos pre-restauración. La retención remota no está automatizada y debe supervisarse por separado.
- Hacer una prueba real de recuperación antes de considerar completada la configuración externa.
