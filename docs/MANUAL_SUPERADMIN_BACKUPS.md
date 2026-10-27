# Manual del SuperAdmin — Copias de seguridad y recuperación

## 1. Objetivo

El SuperAdmin puede consultar, crear y descargar snapshots completos desde **Administración → Copias de seguridad de PostgreSQL**. Cada snapshot incluye PostgreSQL y el árbol de archivos de documentos asociado, empaquetados juntos al descargarlo.

Las copias contienen datos sensibles y deben tratarse como información protegida. La restauración de producción no se ejecuta en el proceso ASP.NET: la aplicación solicita una unidad systemd independiente que realiza la operación.

La copia externa cifrada se automatiza mediante `rclone` y el remote `gdrive-crypt:`; cada paquete remoto incluye PostgreSQL y los documentos en un único snapshot. Consulta [BACKUPS_GOOGLE_DRIVE.md](BACKUPS_GOOGLE_DRIVE.md) para instalar, verificar y recuperar desde Google Drive. La subida remota no borra copias de Drive.

## 2. Almacén root-owned y migración inicial

El almacén recomendado es /var/lib/dietoexpress-backups, propiedad root, grupo joso y permisos 0750. Los dumps, manifiestos y metadatos son root:joso con permisos 0640. De esta forma joso puede listarlos/descargarlos, pero no cambiar el dump ni su checksum antes de que el helper los restaure como PostgreSQL superuser.

No uses /var/lib/dietoexpress/backups: su directorio padre es escribible por joso para el aviso de mantenimiento, por lo que no constituye una frontera segura para archivos que abrirá pg_restore como postgres.

Si el servidor aún guarda las copias en la ruta antigua, sigue primero la subsección "Migración del almacén anterior" de la guía de despliegue. Comprueba que cada checksum se valida en origen, copia dumps y metadatos a la nueva ruta como root, vuelve a generar los manifiestos para el nuevo directorio y actualiza DIETOEXPRESS_BACKUP_DIR en /etc/dietoexpress/dietoexpress.env. Las copias históricas que no tengan snapshot documental asociado no se consideran restaurables como conjunto. Conserva el directorio antiguo hasta que las copias migradas hayan pasado la verificación y una prueba de restauración aislada.

## 3. Backups automáticos diarios e híbridos

Producción utiliza un systemd timer diario a las 02:00, con retraso aleatorio de hasta 15 minutos. PostgreSQL se vuelca completo en cada ejecución. Los documentos se guardan como snapshots completos independientes, compartiendo mediante hard links los archivos idénticos entre días; el backup normal del domingo crea una nueva base documental sin reutilizar archivos del snapshot anterior. Esto limita el crecimiento y vuelve a leer el árbol documental completo semanalmente.

La infraestructura privilegiada se provisiona desde el servidor, nunca desde GitHub Actions. Tras desplegar los scripts, el administrador debe ejecutar:

~~~bash
sudo /opt/dietoexpress/scripts/provision-dietoexpress-backup-automation.sh
~~~

El provisioning instala helpers root-owned en /usr/local/sbin, unidades systemd y reglas sudoers restringidas. Es idempotente, pero debe volver a ejecutarse después de actualizar cualquiera de los helpers privilegiados o unidades systemd: el deploy a /opt/dietoexpress no actualiza las copias root-owned.

El timer se ejecuta como joso. El script de backup invoca mediante sudo -n el helper dietoexpress-pg-backup con --backup para copias normales y --backup-pre-restore para el snapshot automático previo a una restauración; el helper ejecuta pg_dump como usuario PostgreSQL postgres. El helper publica el dump PostgreSQL, un snapshot documental y su manifiesto SHA-256 como root:joso. Los ficheros del snapshot tienen permisos 0640 y los directorios 0750: la aplicación puede leerlos, pero no modificarlos. Cada ejecución genera un snapshot con vista completa del árbol documental y usa `rsync --checksum --link-dest` contra el snapshot completo más reciente que haya superado la verificación, salvo el backup normal del domingo, que inicia una nueva base completa. Los archivos idénticos comparten inodos entre snapshots; los modificados se guardan de nuevo y las eliminaciones solo desaparecen de snapshots posteriores. Como los ficheros compartidos son root-owned y no escribibles por joso, la aplicación no puede alterar copias anteriores. PostgreSQL se vuelca completo en cada ejecución. Todos los componentes comparten timestamp y la retención elimina el dump, el manifiesto y el snapshot asociado como una unidad.

Los backups históricos que solo contienen PostgreSQL no se muestran como restaurables en SuperAdmin, porque no permiten recuperar los documentos en el mismo estado. Se conservan en disco hasta que se decida una política de retirada, pero no deben usarse para una restauración coordinada.

Comprobaciones:

~~~bash
systemctl status dietoexpress-backup.timer
systemctl list-timers dietoexpress-backup.timer
journalctl -u dietoexpress-backup.service --since "7 days ago" --no-pager
sudo visudo -cf /etc/sudoers.d/dietoexpress-restore
~~~

La retención por defecto conserva catorce backups diarios normales (aproximadamente dos semanas). Los snapshots automáticos previos a una restauración incluyen el sufijo -pre-restore y tienen retención independiente de cuatro copias. Los snapshots manuales con sufijo -pre-* quedan excluidos de la rotación automática.


## 10. Resultado de prueba de restauración — 2026-10-27

El propietario del proyecto confirma que la restauración real de PostgreSQL se probó con éxito. El pendiente de ejecutar la primera restauración PostgreSQL queda cerrado.

Para conservar la validez operativa:
- Mantener el backup y su manifiesto/checksum disponibles conforme a la retención.
- Registrar fuera de Git la fecha, el snapshot usado, el entorno destino, el resultado y cualquier incidencia de la prueba.
- Para pruebas futuras, usar una base o instancia aislada; nunca experimentar con una restauración directa sobre la base de producción.
- La prueba de PostgreSQL no debe interpretarse automáticamente como prueba de restauración conjunta de documentos, configuración/secrets, DNS/TLS y recuperación completa del servidor. Esos componentes solo se marcan como probados si se incluyeron en el simulacro.


## 4. Crear un backup desde SuperAdmin

1. Entrar con una cuenta con rol superadmin.
2. Abrir Administración.
3. Localizar **Copias de seguridad de PostgreSQL**.
4. Comprobar que el estado aparece como configurado.
5. Pulsar **Crear backup ahora**.
6. Esperar a que finalice.
7. Comprobar que la copia aparece con nombre, tamaño y fecha.
8. Descargarla si debe conservarse fuera del servidor.

La configuración privilegiada toma como fuente de verdad /etc/dietoexpress/dietoexpress.env. Debe contener al menos:

~~~text
DIETOEXPRESS_BACKUP_DIR=/var/lib/dietoexpress-backups
DIETOEXPRESS_DATABASE=<NOMBRE_DE_LA_BASE>
~~~

El helper conecta al PostgreSQL local como postgres; por eso DIETOEXPRESS_DATABASE es obligatorio. No se debe usar el usuario de aplicación para pg_dump, ya que puede carecer de permisos de lectura sobre algunas tablas. El provisioning requiere `rsync` instalado en el servidor.

## 5. Contenido y propietarios de las copias

El sistema utiliza scripts/dietoexpress-backup.sh, que delega pg_dump en scripts/dietoexpress-pg-backup-root.sh, instalado como /usr/local/sbin/dietoexpress-pg-backup.

El dump usa formato custom y conserva la información de propietarios; no se genera con --no-owner. El checksum SHA-256 y los metadatos se publican junto al dump. Los dumps, manifiestos y metadatos quedan como root:joso con permisos 0640. El almacén root-owned es de solo lectura para joso. El snapshot documental es una carpeta completa `dietoexpress-documents-<timestamp>` con un manifiesto `.sha256` que verifica el contenido de cada archivo. No borres ni edites manualmente archivos de esos directorios: los hard links comparten datos entre snapshots.

La configuración de producción puede incluirse mediante la opción operativa correspondiente. Esa copia contiene secretos y debe protegerse especialmente. **Nunca** envíes backups, configuración de producción ni secretos por correo, tickets o canales no autorizados.

## 6. Verificación antes de restaurar

Desde SuperAdmin puede usarse **Verificar** en la fila de la copia. En el servidor:

~~~bash
/opt/dietoexpress/scripts/dietoexpress-restore.sh --verify dietoexpress-postgresql-XXXXXXXX.dump
~~~

El preflight verifica el nombre, el checksum SHA-256 y el formato del dump, además del manifiesto de inventario del snapshot documental. Rechaza snapshots incompletos, enlaces simbólicos y archivos especiales. No modifica la base de datos ni detiene DietoExpress.

## 7. Restauración controlada de producción

La restauración debe realizarla un administrador de infraestructura. Antes de empezar, confirmar la copia elegida, verificarla, comprobar espacio disponible y coordinar una ventana de mantenimiento.

Puede iniciarse desde SuperAdmin con **Restaurar** o manualmente:

~~~bash
sudo /usr/local/sbin/dietoexpress-restore-web dietoexpress-postgresql-XXXXXXXX.dump --confirm
~~~

La petición solicita una unidad dietoexpress-restore@...service. La unidad detiene dietoexpress.service; el script en /opt se ejecuta como joso para verificar la copia y crear el snapshot previo, pero las operaciones privilegiadas de PostgreSQL las realizan helpers root-owned:

1. dietoexpress-backup.sh llama al helper root-owned de backup mediante sudo -n.
2. El helper ejecuta pg_dump como postgres, conservando propietarios, y publica una copia previa con checksum verificado.
3. El helper de restauración valida de nuevo nombre, manifiestos, checksums, formato y propiedad root-owned tanto del dump como del archivo documental.
4. Verifica todos los hashes del snapshot, copia sus documentos a un directorio de preparación escribible por joso y sustituye el árbol documental existente; después copia el dump temporalmente a un directorio privado de /tmp accesible a postgres, sin cambiar permisos del almacén permanente.
5. Ejecuta pg_restore como postgres sin --no-owner, de modo que las tablas vuelvan al propietario dietoexpress y las extensiones mantengan su propietario.
6. Si la restauración objetivo falla, intenta recuperar automáticamente el snapshot previo de PostgreSQL y documentos usando el mismo helper.

Resultado del rollback:

- **Restauración correcta:** systemd inicia DietoExpress y comprueba que queda activo.
- **Restauración solicitada fallida, rollback correcto:** la operación queda marcada como fallida, pero se inicia la aplicación y se retira el aviso de mantenimiento solo cuando el backend queda activo.
- **Restauración y rollback fallidos:** la unidad deja DietoExpress detenido y conserva el aviso de mantenimiento para evitar arrancar sobre una base potencialmente parcial. Es un incidente crítico; no se debe lanzar otra restauración a ciegas.

El código de salida 10 identifica una restauración fallida cuyo rollback sí terminó; el 20 identifica que también falló el rollback. No se debe ejecutar el helper root directamente salvo en una intervención de infraestructura controlada.

El script scripts/dietoexpress-restore.sh --restore ... --confirm es una interfaz de compatibilidad que delega en el wrapper privilegiado; nunca ejecuta directamente pg_restore como el usuario de la aplicación.

## 8. Después de restaurar

~~~bash
systemctl is-active dietoexpress.service
systemctl is-active postgresql
systemctl is-active nginx
journalctl -u dietoexpress.service --since "10 minutes ago" --no-pager
~~~

Realizar un smoke test: HTTPS, login SuperAdmin, dashboard, datos principales y ausencia de errores de bootstrap/migración. Comprobar la compatibilidad entre el código desplegado y el esquema restaurado.

Si la unidad está fallida y el backend detenido, revisar primero el journal de dietoexpress-restore@...service. Si consta que falló el rollback, conservar backups y logs; no arrancar el servicio hasta evaluar si la base quedó parcial.

## 9. Restauración de prueba

La presencia de backups no demuestra que la recuperación esté probada. Realizar las pruebas en una base independiente sin tocar producción. La validación mínima incluye checksum, restauración completa, verificación de propietarios y arranque de una instancia compatible.

## 10. Reglas de seguridad

- Solo superadmin puede gestionar backups desde la aplicación.
- Las restauraciones se ejecutan en una unidad systemd fuera del proceso web.
- Nunca se restaura sin snapshot previo verificado.
- No se relajan los permisos del almacén para permitir acceso a postgres ni se deja que joso modifique dumps/manifiestos que el helper privilegiado va a procesar.
- No se usa --no-owner en los backups operativos ni en la restauración privilegiada.
- No se ejecuta como root código de /opt/dietoexpress desde CI/CD.
- No se guardan backups en Git ni se comparten dumps o secretos en tickets.
- No se elimina manualmente un backup para liberar espacio sin revisar la política de retención.

## 11. Resumen operativo

**Backup manual:** SuperAdmin → Administración → Copias de seguridad → Crear backup. El archivo descargado `*-completo.tar.gz` contiene el dump, su checksum, metadatos, el directorio del snapshot documental y su manifiesto. Es un paquete autocontenido para custodia externa. Cada descarga se empaqueta bajo demanda; no se conserva una segunda copia comprimida de todos los documentos en el disco del servidor.

**Verificación:** seleccionar la copia y pulsar **Verificar**, o utilizar --verify.

**Restauración:** SuperAdmin → **Restaurar**, o ejecutar el wrapper dietoexpress-restore-web. La unidad crea una copia previa privilegiada, restaura con propietarios y hace rollback automático si falla.

**Actualizar infraestructura:** volver a ejecutar sudo /opt/dietoexpress/scripts/provision-dietoexpress-backup-automation.sh después de desplegar cambios en helpers o unidades systemd.