# Manual del SuperAdmin — Copias de seguridad y recuperación

## 1. Objetivo

El SuperAdmin puede consultar, crear y descargar copias de seguridad PostgreSQL desde **Administración → Copias de seguridad de PostgreSQL**.

Las copias contienen datos sensibles. Deben tratarse como información protegida y conservarse según la política de seguridad y retención de DietoExpress.

La restauración de producción **no se ejecuta desde la interfaz web**. Es una operación de infraestructura deliberadamente separada del proceso ASP.NET para evitar que la aplicación pueda destruirse o quedar inutilizada mientras se está restaurando su propia base de datos.

## 2. Crear un backup desde SuperAdmin

1. Entrar con una cuenta con rol `superadmin`.
2. Abrir el panel de Administración.
3. Localizar **Copias de seguridad de PostgreSQL**.
4. Comprobar que el estado aparece como configurado.
5. Pulsar **Crear backup ahora**.
6. Esperar a que finalice la operación.
7. Comprobar que la nueva copia aparece en el listado con nombre, tamaño y fecha.
8. Descargarla si se necesita conservar una copia fuera del servidor.

Si la sección indica que el backup no está configurado, no se debe intentar solucionar el problema modificando datos desde la aplicación. La configuración del destino y de PostgreSQL corresponde a la operación del servidor.

## 3. Qué contiene una copia

El sistema utiliza el script operativo `scripts/dietoexpress-backup.sh`.

El backup PostgreSQL se genera en formato custom de PostgreSQL e incluye un checksum SHA-256 y metadatos operativos. El destino debe estar fuera del directorio de despliegue y protegido con permisos restrictivos.

La configuración de producción puede incluirse en el backup mediante la opción operativa correspondiente. Esa copia contiene secretos y debe protegerse especialmente.

**Nunca** enviar un backup, el archivo de configuración de producción ni sus secretos por correo, tickets o canales no autorizados.

## 4. Verificación antes de restaurar

Antes de modificar una base de datos se debe comprobar el backup:

```bash
./scripts/dietoexpress-restore.sh --verify dietoexpress-postgresql-XXXXXXXX.dump
```

El preflight comprueba:

- que el archivo existe;
- que su nombre corresponde a un backup DietoExpress;
- que existe su checksum;
- que el SHA-256 coincide;
- que PostgreSQL puede inspeccionar el formato del dump.

El modo `--verify` **no modifica la base de datos ni detiene DietoExpress**.

## 5. Restauración controlada de producción

La restauración debe realizarla un administrador de infraestructura con acceso al servidor. No se debe ejecutar desde el navegador ni mediante una petición HTTP.

Antes de restaurar:

1. Confirmar que existe una copia válida del estado actual.
2. Elegir el backup correcto y verificarlo.
3. Confirmar que la restauración es necesaria.
4. Informar de la ventana de mantenimiento si afecta a usuarios.
5. Comprobar que existe espacio suficiente.
6. Confirmar que PostgreSQL está operativo.

La operación se inicia en el servidor con:

```bash
./scripts/dietoexpress-restore.sh --restore dietoexpress-postgresql-XXXXXXXX.dump --confirm
```

El helper realiza, en este orden:

1. verifica el backup;
2. detiene `dietoexpress.service`;
3. crea automáticamente un **backup pre-restauración** del estado actual;
4. ejecuta `pg_restore` con limpieza de objetos existentes;
5. si la restauración falla, intenta restaurar automáticamente el backup pre-restauración;
6. si la restauración tiene éxito, arranca de nuevo DietoExpress;
7. comprueba que `dietoexpress.service` queda activo.

Si tanto la restauración como el rollback fallan, se considera un incidente crítico y no se deben realizar cambios manuales adicionales sin conservar primero toda la evidencia y consultar el procedimiento de recuperación.

## 6. Después de restaurar

Comprobar siempre:

```bash
systemctl is-active dietoexpress.service
systemctl is-active postgresql
systemctl is-active nginx
```

Después realizar el smoke test de producción:

- acceso HTTPS;
- inicio de sesión SuperAdmin;
- carga del dashboard;
- acceso a datos principales;
- creación/consulta de una operación no destructiva;
- revisión de logs;
- comprobación de que no aparecen errores de bootstrap o migración.

Si la restauración corresponde a una versión anterior, comprobar también la compatibilidad entre el código desplegado y el esquema restaurado.

## 7. Restauración de prueba

La existencia de backups y del script de restauración **no demuestra que el proceso de recuperación haya sido probado**.

Para cerrar este requisito de 1.0 se debe realizar una restauración real en una base independiente, ejecutar el smoke test y registrar el resultado.

La prueba debe hacerse sin tocar la base de producción.

## 8. Reglas de seguridad

- Solo `superadmin` puede gestionar backups desde la aplicación.
- No ejecutar restauraciones mediante endpoints web.
- No restaurar sin backup pre-restauración.
- No eliminar manualmente backups para liberar espacio sin aplicar la política de retención.
- No guardar backups en Git.
- No exponer rutas internas del servidor al usuario.
- No compartir dumps ni secretos en tickets o capturas.
- Registrar las restauraciones e incidencias operativas.
- Ante una restauración dudosa, detenerse y verificar primero el backup mediante `--verify`.

## 9. Resumen operativo

**Backup normal:** SuperAdmin → Administración → Copias de seguridad → Crear backup.

**Descarga:** seleccionar una copia válida y descargarla para almacenamiento seguro externo.

**Comprobación:** `--verify`.

**Restauración:** operación de infraestructura con `--restore ... --confirm`, nunca desde la aplicación web.

**Incidente durante restore:** el helper intenta rollback automático al backup creado inmediatamente antes de la restauración.