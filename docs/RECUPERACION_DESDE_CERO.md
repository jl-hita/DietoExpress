# Recuperación de DietoExpress desde un servidor vacío

> Runbook para reconstruir DietoExpress en un Fedora nuevo tras la pérdida total de `weirdfish`. Está pensado para ejecutar los pasos en orden y registrar los resultados. No sustituye la guía detallada de despliegue ni autoriza a restaurar sobre un servidor que todavía contenga datos que deban conservarse.

## Alcance y estado conocido

- La restauración de PostgreSQL se ha probado con éxito (confirmación del propietario, 2026-10-27).
- Las alertas externas de disponibilidad están operativas (confirmación del propietario, 2026-10-27).
- Este runbook automatiza la preparación inicial del sistema Fedora; no convierte la reconstrucción completa en una operación de un solo comando.
- La recuperación integral solo se considera validada después de descargar y verificar un paquete cifrado de Drive, recuperar documentos y PostgreSQL, desplegar la aplicación y superar el smoke test en un host aislado.

Guías relacionadas:

- [Despliegue Linux y configuración completa](GUIA_DESPLIEGUE_LINUX_PRODUCCION.md)
- [Backups y restauración desde SuperAdmin](MANUAL_SUPERADMIN_BACKUPS.md)
- [Paquetes cifrados en Google Drive](BACKUPS_GOOGLE_DRIVE.md)
- [Operación 1.0](OPERACION_1_0.md)

## 0. Antes de que ocurra una avería: preparar la custodia

No guardes secretos en Git, en esta guía ni en una issue. Mantén una copia fuera de `weirdfish` (idealmente gestor de contraseñas con recuperación y una copia offline cifrada) de:

- El contenido completo de `/etc/dietoexpress/dietoexpress.env`, con acceso restringido.
- `/home/joso/.config/rclone/rclone.conf` (modo 0600). Es imprescindible para acceder al remote `gdrive-crypt:`; contiene credenciales y la configuración/clave de cifrado del remote.
- Credenciales de DNS/dominio y la información necesaria para volver a emitir el certificado TLS.
- Credenciales de servicios externos que no puedan regenerarse automáticamente: SMTP/Brevo, Stripe y webhooks, Google OAuth/Calendar y credenciales de Firebase/FCM.
- Acceso de administrador a GitHub y los datos necesarios para recuperar los secretos de Actions y el acceso SSH de despliegue.
- La lista de valores no secretos específicos del servidor: dominio, usuario/BD PostgreSQL, rutas persistentes, URL pública y puertos internos.

No hagas una copia indiscriminada de claves privadas TLS: normalmente es más seguro volver a emitir el certificado mediante Certbot. Si decides respaldarlas, trátalas como secretos de máxima sensibilidad.

**Prueba previa recomendada:** desde otro dispositivo, comprueba que puedes acceder a esta custodia y que la contraseña/clave de cifrado se puede recuperar. Una copia de `rclone.conf` sin la contraseña que desbloquea el gestor de secretos no es suficiente.

## 1. Preparar Fedora limpio

1. Instala una versión de Fedora compatible con los paquetes del proyecto, aplica actualizaciones y configura acceso SSH seguro.
2. Comprueba que el DNS del dominio apunta a la nueva IP. No cambies DNS hasta que el nuevo host esté listo si el anterior aún funciona.
3. Clona el repositorio o descarga un checkout de la rama estable.
4. Ejecuta el bootstrap incluido en el repositorio:

   ```bash
   git clone https://github.com/jl-hita/DietoExpress.git
   cd DietoExpress
   sudo bash scripts/bootstrap-dietoexpress-fedora.sh
   ```

El script instala ASP.NET Core Runtime 10, PostgreSQL, Nginx, Certbot, rclone y herramientas auxiliares; prepara directorios y habilita PostgreSQL/Nginx. Solo inicializa PostgreSQL si no encuentra un clúster existente. No despliega código, no abre puertos del firewall, no crea credenciales, no configura Nginx/HTTPS y no restaura datos.

Si la aplicación requiere una versión de runtime distinta en una futura versión, actualiza el bootstrap junto con el `TargetFramework` y las pruebas correspondientes.

## 2. Recuperar configuración y secretos

1. Crea `/etc/dietoexpress` y restaura `dietoexpress.env` desde la custodia segura. Conserva todos los valores existentes y adapta únicamente los que deban cambiar en el nuevo host.
2. Revisa que la configuración identifique correctamente la base de datos, puerto PostgreSQL, ruta documental, almacén de backups y remote cifrado.
3. Instala `rclone.conf` en `/home/joso/.config/rclone/rclone.conf`, propiedad `joso:joso`, modo `0600`. No muestres su contenido en terminales compartidos, logs ni incidencias.
4. Verifica los remotes sin imprimir secretos:

   ```bash
   sudo -u joso env RCLONE_CONFIG=/home/joso/.config/rclone/rclone.conf rclone listremotes
   sudo -u joso env RCLONE_CONFIG=/home/joso/.config/rclone/rclone.conf rclone lsd gdrive-crypt:
   sudo stat -c '%U:%G:%a %n' /home/joso/.config/rclone/rclone.conf
   ```

La lista debe incluir `gdrive-crypt:` y el archivo debe mostrar `joso:joso:600`. Si no puedes recuperar la configuración o las claves de cifrado, detente: no podrás asumir que el contenido remoto es recuperable.

## 3. Recuperar el último paquete completo

Sigue la sección **Recuperar un paquete desde Drive** de [BACKUPS_GOOGLE_DRIVE.md](BACKUPS_GOOGLE_DRIVE.md). En resumen:

1. Lista los paquetes completos disponibles en `gdrive-crypt:`.
2. Elige el último paquete cuya fecha sea adecuada; descarga tanto el `.tar.gz` como su checksum.
3. Verifica SHA-256 antes de extraer.
4. Inspecciona el contenido del archivo antes de extraerlo y rechaza rutas absolutas o componentes `..`.
5. Valida dump, metadatos, manifiestos y documentos con los helpers de la aplicación.
6. Transfiere el paquete validado al almacén local de backups sin sobrescribir silenciosamente otros snapshots.

No restaures directamente desde un archivo que no haya superado estas comprobaciones.

## 4. Configurar PostgreSQL y restaurar datos

- Confirma la versión de PostgreSQL instalada y consulta compatibilidad con la versión que creó el dump.
- Configura la autenticación local y las credenciales según la guía de despliegue. No expongas el puerto 5432 a Internet.
- Sigue el procedimiento controlado de [MANUAL_SUPERADMIN_BACKUPS.md](MANUAL_SUPERADMIN_BACKUPS.md) o la restauración documentada en [BACKUPS_GOOGLE_DRIVE.md](BACKUPS_GOOGLE_DRIVE.md).
- No ejecutes `pg_restore` manualmente contra una base que pueda contener datos que quieras conservar.
- Restaura también el snapshot documental correspondiente al mismo paquete. PostgreSQL por sí solo no recupera PDF ni otros archivos persistentes.
- Conserva el dump original y los logs hasta terminar las comprobaciones.

La restauración real de PostgreSQL ya fue probada anteriormente, pero en una reconstrucción nueva hay que validar también la descarga remota, la recuperación de documentos y la coherencia entre datos y archivos.

## 5. Reinstalar servicios y desplegar

Usa la guía de despliegue para configurar, con los valores de este host:

- `/etc/systemd/system/dietoexpress.service`, usuario `joso`, working directory `/opt/dietoexpress` y variables de entorno protegidas.
- Configuración de Nginx para el dominio y proxy a Kestrel.
- Certificado TLS mediante Certbot.
- Permisos y contexto SELinux de las rutas de aplicación y persistencia.
- Firewall con acceso solo a SSH y HTTP/HTTPS; PostgreSQL y Kestrel deben seguir siendo internos.
- Acceso SSH/secretos de GitHub Actions necesarios para el despliegue.

Despliega una versión conocida y estable mediante el workflow del repositorio. No ejecutes desde CI como root scripts de `/opt/dietoexpress`. Después del despliegue, instala los helpers y timers de backup con el procedimiento de la guía de Google Drive:

```bash
sudo /opt/dietoexpress/scripts/provision-dietoexpress-backup-automation.sh
sudo systemctl daemon-reload
sudo systemctl status dietoexpress-backup.timer --no-pager
sudo systemctl status dietoexpress-backup-drive.timer --no-pager
```

El provisioning de backups valida requisitos de configuración; puede fallar deliberadamente si no están listos los remotes, permisos o archivos esperados. No fuerces el proceso relajando los permisos.

## 6. Validación antes de cambiar DNS o declarar recuperado el servicio

Registra el resultado de cada punto en el informe de recuperación:

- [ ] PostgreSQL activo y accesible localmente; base de datos y migraciones correctas.
- [ ] Documentos y PDF restaurados y legibles por la aplicación.
- [ ] `dietoexpress.service` activo y estable tras reiniciarlo.
- [ ] Nginx pasa `sudo nginx -t` y sirve el dominio correcto.
- [ ] HTTPS válido y renovación automática de Certbot configurada.
- [ ] Login de SuperAdmin correcto; dashboard y datos principales visibles.
- [ ] Smoke tests de endpoints críticos, sin errores de bootstrap ni migraciones.
- [ ] Creación y lectura de un documento de prueba, sin alterar datos reales.
- [ ] Backup local manual ejecutado y verificado.
- [ ] Servicio de subida externa y timers activos; comprobar logs.
- [ ] Monitor externo configurado para la nueva URL/IP y prueba de alerta recibida.
- [ ] Flujos de Android comprobados si los cambios de host implican URL, TLS, endpoints o autenticación.
- [ ] Firewall revisado; PostgreSQL y Kestrel no accesibles públicamente.

Solo después de pasar las pruebas cambia el DNS o dirige el tráfico al nuevo host. La monitorización externa ya funciona en el entorno actual, pero su configuración debe actualizarse si cambia el host o la URL y volver a comprobarse.

## 7. Cierre de la recuperación

- Guarda fuera del servidor el commit desplegado, fecha del backup restaurado, resultado de las validaciones y tiempo total de recuperación.
- Ejecuta una copia nueva una vez comprobada la aplicación y verifica que se sube cifrada a Drive.
- Conserva el host antiguo apagado o aislado hasta confirmar que no faltan datos.
- Actualiza esta guía cuando cambie la arquitectura, los nombres de variables, los servicios o el procedimiento de restauración.

## Estado de automatización pendiente

Este runbook y el bootstrap reducen trabajo repetitivo, pero aún requieren configurar secretos, Nginx/TLS, desplegar el artefacto y validar manualmente. No afirmar que la recuperación integral está automatizada hasta ejecutar el procedimiento completo en una máquina virtual o equipo limpio y registrar evidencias.
