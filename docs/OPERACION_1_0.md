# Operación DietoExpress 1.0

## Objetivo

Este documento define el mínimo procedimiento operativo para considerar DietoExpress preparado para producción 1.0. No sustituye la configuración real del servidor ni la revisión jurídica.

## Antes de cada despliegue

1. Confirmar CI verde sobre el commit que se va a desplegar.
2. Ejecutar un backup PostgreSQL con `scripts/dietoexpress-backup.sh`.
3. Comprobar espacio libre en disco.
4. Confirmar que PostgreSQL y `dietoexpress.service` están activos.
5. Conservar la referencia del commit/artefacto anterior para rollback.
6. Desplegar mediante GitHub Actions.
7. Esperar al smoke test post-deploy y comprobar el resultado.
8. Revisar el journal de los últimos minutos si el despliegue ha cambiado bootstrap, configuración o esquema.

El workflow de despliegue ya falla antes de reemplazar la aplicación si los directorios persistentes requeridos no existen o no son escribibles.

## Backup

El backup debe estar fuera del directorio de despliegue y, preferiblemente, fuera del propio servidor.

Configurar antes de usar el script:

```text
DIETOEXPRESS_BACKUP_DIR=/ruta/segura
DIETOEXPRESS_DATABASE=nombre_bd
DIETOEXPRESS_DB_USER=usuario_bd
DIETOEXPRESS_DB_HOST=127.0.0.1
DIETOEXPRESS_DB_PORT=5432
```

Si se utiliza `DATABASE_URL` en lugar de los parámetros anteriores, el script puede recibirla mediante `DIETOEXPRESS_DATABASE_URL`.

Los archivos de backup contienen datos sensibles y deben quedar protegidos con permisos 0700/0600 y una política de cifrado y retención adecuada.

El script genera:
- dump PostgreSQL en formato custom;
- manifiesto con fecha, host, tamaño y checksum SHA-256;
- copia opcional de `/etc/dietoexpress/dietoexpress.env` cuando `DIETOEXPRESS_BACKUP_INCLUDE_ENV=true`.

No se deben guardar secretos en Git ni en este documento.

## Restauración

Una restauración de producción no debe ejecutarse sobre la base activa sin una ventana controlada y un backup previo.

Procedimiento recomendado:

1. Provisionar una base de restauración independiente.
2. Restaurar el dump con `pg_restore`.
3. Comprobar que la base abre correctamente.
4. Arrancar una instancia aislada con la configuración de restauración.
5. Ejecutar el smoke test y las pruebas críticas.
6. Registrar el resultado.
7. Solo después planificar una restauración sobre producción si fuese necesaria.

Para una restauración controlada sobre producción existe `scripts/dietoexpress-restore.sh`. El helper exige checksum válido y formato PostgreSQL correcto, crea automáticamente un backup pre-restauración, detiene `dietoexpress.service`, ejecuta `pg_restore --clean --if-exists --no-owner` y, si la restauración falla, intenta recuperar automáticamente el backup previo. El arranque final del servicio se comprueba con systemd.

Ejemplos:

```bash
# Solo preflight: no modifica producción.
./scripts/dietoexpress-restore.sh --verify dietoexpress-postgresql-XXXXXXXX.dump

# Restauración controlada. Requiere sudo no interactivo para systemctl.
./scripts/dietoexpress-restore.sh --restore dietoexpress-postgresql-XXXXXXXX.dump --confirm
```

El helper está pensado para una operación de infraestructura, no para ejecutarse desde el proceso web ni como respuesta HTTP. La existencia del script **no significa que la restauración haya sido probada**. Esa prueba debe realizarse de forma controlada y quedar registrada.

## Rollback de aplicación

Si falla el código pero el esquema sigue siendo compatible:

1. detener DietoExpress;
2. restaurar el artefacto/backend/frontend anterior;
3. arrancar DietoExpress;
4. comprobar `systemctl is-active`;
5. comprobar HTTPS y el endpoint público;
6. revisar logs;
7. ejecutar las pruebas críticas disponibles.

Si la versión desplegada ha aplicado un cambio de esquema incompatible, restaurar solo los archivos no es suficiente. En ese caso se necesita restaurar también una copia PostgreSQL compatible y revisar cualquier dato creado desde el despliegue fallido.

Toda migración destructiva o no reversible debe tener un procedimiento específico antes de desplegarse.

## Recuperación ante pérdida completa del servidor

La guía de instalación existente documenta la reconstrucción desde un servidor Linux nuevo. Los elementos que deben existir fuera del servidor son, como mínimo:

- backup PostgreSQL;
- `/etc/dietoexpress/dietoexpress.env` protegido;
- configuración Nginx;
- configuración systemd;
- material necesario para recuperar HTTPS;
- inventario de proveedores y cuentas;
- DNS y dominio;
- procedimiento de acceso administrativo.

La recuperación solo se considera verificada después de una restauración controlada en infraestructura independiente.

## Monitorización mínima

Comprobar periódicamente:

```bash
systemctl is-active dietoexpress.service
systemctl is-active nginx
systemctl is-active postgresql
df -h
du -sh /var/lib/dietoexpress/Logs
du -sh /var/lib/dietoexpress/AlertSpool
```

Incidentes prioritarios:
- DietoExpress caído;
- PostgreSQL caído;
- disco próximo a llenarse;
- backup fallido;
- certificado próximo a caducar;
- errores críticos de bootstrap;
- crecimiento anormal de AlertSpool.

La aplicación ya registra alertas persistentes en `system_alerts` y conserva temporalmente alertas en `AlertSpool` cuando PostgreSQL no está disponible. Esto complementa, pero no sustituye, un sistema externo de monitorización.

## Soporte de primera línea

Ante una incidencia:

1. identificar hora de inicio y alcance;
2. comprobar estado de DietoExpress, PostgreSQL y Nginx;
3. comprobar espacio de disco;
4. revisar journal y logs de aplicación;
5. consultar `system_alerts` si PostgreSQL está disponible;
6. comprobar si existe AlertSpool pendiente;
7. comprobar el último despliegue y commit;
8. si coincide con un despliegue reciente, valorar rollback;
9. no modificar datos manualmente sin backup y procedimiento;
10. documentar causa, impacto y resolución.

Nunca incluir contraseñas, JWT, client secrets, access tokens o refresh tokens en tickets, capturas o logs compartidos.

## Checklist de release 1.0

- [x] CI ejecuta build, auditorías y regresiones.
- [x] Deploy comprueba directorios persistentes antes de reemplazar la aplicación.
- [x] Deploy ejecuta smoke test real después del reinicio.
- [x] Existe procedimiento documentado de backup.
- [x] Existe procedimiento documentado de restauración.
- [x] Existe procedimiento documentado de rollback.
- [x] Existe procedimiento de recuperación completa.
- [x] Existe procedimiento de soporte de primera línea.
- [ ] Restauración de un backup probada en infraestructura independiente.
- [ ] Alertas externas operativas y verificadas.
- [ ] Política real de retención/cifrado de backups configurada.
- [ ] Revisión contractual/legal externa completada.

Las casillas no marcadas requieren una prueba o configuración real; no deben marcarse por la mera existencia de documentación.
