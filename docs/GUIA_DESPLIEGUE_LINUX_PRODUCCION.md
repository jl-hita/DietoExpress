# Guía de despliegue de DietoExpress en un servidor Linux nuevo

> Procedimiento operativo para instalar DietoExpress desde cero en una máquina Linux limpia y dejarla preparada para producción. También sirve como guía de reconstrucción después de perder un servidor.

**Regla principal:** el código desplegado puede reemplazarse; los datos persistentes nunca deben formar parte de una limpieza normal de despliegue.

## 1. Arquitectura

Internet → DNS → Nginx/HTTPS → ASP.NET Core → PostgreSQL.

Servicios externos actuales: Stripe, Google OAuth/Calendar, SMTP, USDA FoodData Central, Open Food Facts, Geoapify y LocationIQ (autocompletado de direcciones con failover controlado).

Rutas de producción actuales:

| Ruta | Uso | Persistente |
|---|---|---:|
| /opt/dietoexpress | Backend publicado | No |
| /opt/dietoexpress/wwwroot | Frontend dentro del backend | No |
| /var/www/dietoexpress | Frontend servido por Nginx | No |
| /var/lib/dietoexpress/Logs | Logs de aplicación | Sí |
| /var/lib/dietoexpress/AlertSpool | Cola persistente de alertas | Sí |
| /etc/dietoexpress/dietoexpress.env | Secretos/configuración del servicio | Sí, respaldar de forma segura |

La aplicación actual se ejecuta como el usuario joso y el servicio es dietoexpress.service.

---

# 2. Datos que hay que preparar antes de instalar

Crear una hoja operativa con:

- proveedor y servidor;
- IP pública;
- dominio;
- usuario administrativo;
- usuario de aplicación;
- nombre de base de datos;
- usuario PostgreSQL;
- puerto interno de ASP.NET Core;
- proveedor/cuenta SMTP;
- Google Client ID;
- USDA API key;
- Stripe Live Secret Key;
- Stripe Live Webhook Secret;
- URL pública;
- política de backups.

Los valores reales y secretos **no deben almacenarse en este documento ni en Git**.

---

# 3. Preparación inicial del Linux

Actualizar el sistema:

~~~bash
sudo apt update
sudo apt full-upgrade -y
sudo reboot
~~~

Los nombres de paquetes pueden variar entre Debian/Ubuntu, Fedora/RHEL u otra distribución.

Configurar hora:

~~~bash
timedatectl
sudo timedatectl set-timezone Europe/Madrid
timedatectl status
~~~

NTP debe estar activo.

---

# 4. Usuario de aplicación

DietoExpress no debe ejecutarse como root.

Si se utiliza joso:

~~~bash
sudo useradd --system --create-home --shell /usr/sbin/nologin joso
id joso
~~~

Si ya existe, no recrearlo.

El usuario de aplicación debe tener únicamente los permisos necesarios para ejecutar el backend y escribir en los directorios persistentes.

---

# 5. Directorios y permisos

Este paso es **obligatorio en un servidor nuevo**.

Crear directorios de aplicación:

~~~bash
sudo install -d -o joso -g joso -m 755 /opt/dietoexpress
sudo install -d -o joso -g joso -m 755 /var/www/dietoexpress
sudo install -d -o joso -g joso -m 750 /var/lib/dietoexpress
~~~

Crear logs:

~~~bash
sudo install -d -o joso -g joso -m 750 /var/lib/dietoexpress/Logs
~~~

Probar:

~~~bash
ls -ld /var/lib/dietoexpress/Logs
sudo -u joso sh -c 'touch /var/lib/dietoexpress/Logs/.write-test && rm /var/lib/dietoexpress/Logs/.write-test'
~~~

Crear AlertSpool:

~~~bash
sudo install -d -o joso -g joso -m 750 /var/lib/dietoexpress/AlertSpool
~~~

Probar:

~~~bash
ls -ld /var/lib/dietoexpress/AlertSpool
sudo -u joso sh -c 'touch /var/lib/dietoexpress/AlertSpool/.write-test && rm /var/lib/dietoexpress/AlertSpool/.write-test'
~~~

**Nunca crear estos directorios con mkdir desde el workflow de despliegue.** Se provisionan una sola vez en el servidor. El workflow solo comprueba que existan y sean escribibles.

AlertSpool es especialmente importante: ApplicationAlertService puede almacenar allí alertas cuando PostgreSQL no está disponible y recuperarlas posteriormente.

La ubicación puede fijarse mediante DIETOEXPRESS_ALERT_SPOOL; en Linux, si no se define, el valor actual es /var/lib/dietoexpress/AlertSpool.

Los logs pueden fijarse mediante DIETOEXPRESS_LOG_PATH; el valor actual por defecto en Linux es /var/lib/dietoexpress/Logs.

---

# 6. Firewall

Exponer únicamente:

- SSH, preferiblemente restringido;
- TCP 80;
- TCP 443.

No exponer públicamente:

- PostgreSQL 5432;
- puerto interno de Kestrel;
- herramientas administrativas;
- puertos del runner.

Ejemplo UFW:

~~~bash
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow 22/tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
sudo ufw status verbose
~~~

Adaptar SSH si utiliza otro puerto.

También configurar el firewall/security group del proveedor cloud.

---

# 7. SSH y endurecimiento

Antes de producción:

1. instalar una clave SSH administrativa;
2. comprobar acceso con clave desde una segunda sesión;
3. deshabilitar login remoto de root;
4. deshabilitar autenticación por contraseña cuando sea posible;
5. mantener OpenSSH actualizado;
6. restringir SSH por IP cuando sea viable.

No cerrar la única sesión hasta haber probado el acceso alternativo.

---

# 8. PostgreSQL

Instalar una versión soportada:

~~~bash
sudo apt install -y postgresql postgresql-contrib
sudo systemctl enable --now postgresql
sudo systemctl status postgresql
sudo -u postgres psql -c 'SELECT version();'
~~~

Crear usuario y base:

~~~bash
sudo -u postgres psql
~~~

En psql:

~~~sql
CREATE USER <DB_USER> WITH PASSWORD '<DB_PASSWORD>';
CREATE DATABASE <DATABASE> OWNER <DB_USER>;
\q
~~~

La aplicación no debe utilizar el usuario postgres.

Si PostgreSQL y DietoExpress están en el mismo servidor, 5432 no debe publicarse en Internet.

Probar:

~~~bash
psql "host=127.0.0.1 dbname=<DATABASE> user=<DB_USER>"
~~~

Revisar pg_hba.conf y listen_addresses según la topología real.

---

# 9. Bootstrap de la base de datos

DatabaseBootstrap es actualmente responsable de inicializar y actualizar el esquema.

En un servidor nuevo:

1. crear una base vacía;
2. configurar correctamente la cadena de conexión;
3. arrancar DietoExpress;
4. dejar que el bootstrap actual cree las tablas y aplique sus actualizaciones;
5. revisar los logs.

No reconstruir manualmente todas las tablas siguiendo una copia antigua del esquema.

El bootstrap actual también normaliza los roles:

- superadmin;
- nutritionist;
- clinic_admin.

Los roles heredados admin y user se normalizan durante el bootstrap.

Antes de cualquier actualización de producción debe hacerse un backup PostgreSQL.

---

# 10. .NET

El proyecto actual utiliza .NET 8.

Si el servidor únicamente recibe artefactos publicados, basta el runtime correspondiente.

Si el servidor también es el runner self-hosted que compila el proyecto, necesita el SDK.

Comprobar:

~~~bash
dotnet --info
dotnet --list-runtimes
~~~

La versión debe coincidir con el TargetFramework actual del proyecto.

---

# 11. Node.js / Angular

El frontend actual utiliza Angular 20 y TypeScript 5.8.x.

Si CI compila Angular en otro runner, Node.js no es necesario en el servidor de producción.

Si el servidor comercial también actúa como runner de GitHub Actions, deberá disponer de Node.js/npm, Git y el SDK .NET.

Arquitectura recomendada a medio plazo:

~~~text
GitHub Actions runner
        |
        | artefacto
        v
servidor de producción
~~~

Separar build y producción reduce el privilegio y la superficie de ataque del servidor comercial.

---

# 12. Configuración del backend

Crear:

~~~bash
sudo install -d -m 750 /etc/dietoexpress
sudo install -o root -g joso -m 640 /dev/null /etc/dietoexpress/dietoexpress.env
~~~

El archivo debe ser legible por el servicio y no por usuarios no autorizados.

Variables mínimas:

~~~text
ConnectionStrings__DefaultConnection=Host=127.0.0.1;Database=<DATABASE>;Username=<DB_USER>;Password=<DB_PASSWORD>
Jwt__Key=<SECRET_ALEATORIO_DE_AL_MENOS_32_BYTES>
Jwt__Issuer=Anguloso.Server
Jwt__Audience=Anguloso.Client
Cors__AllowedOrigins__0=https://<DOMINIO>
DIETOEXPRESS_LOG_PATH=/var/lib/dietoexpress/Logs
DIETOEXPRESS_ALERT_SPOOL=/var/lib/dietoexpress/AlertSpool
ASPNETCORE_ENVIRONMENT=Production
Geoapify__ApiKey=<GEOAPIFY_API_KEY>
LocationIQ__ApiKey=<LOCATIONIQ_API_KEY>
AddressProviders__Primary=Geoapify
AddressProviders__Fallback=LocationIQ
AddressProviders__WarningThreshold=0.80
AddressProviders__FailoverThreshold=0.90
AddressProviders__GeoapifyDailyLimit=3000
AddressProviders__LocationIqDailyLimit=5000
~~~

Generar una clave JWT aleatoria:

~~~bash
openssl rand -base64 48
~~~

Nunca reutilizar una clave de desarrollo.

appsettings.Example.json es la referencia pública del proyecto, pero los secretos de producción deben permanecer fuera del repositorio.

---

# 13. Configuración almacenada en PostgreSQL

No toda la configuración vive en appsettings.

ConfigServ utiliza la tabla config. Entre los parámetros actuales conocidos:

## SMTP

- smtpServer
- smtpPort
- smtpEnableSsl
- smtpFromEmail
- smtpFromName
- smtpUser
- smtpPwd

## Google

- googleClientId

## Autocompletado de direcciones y failover

La búsqueda de direcciones de pacientes utiliza Geoapify como proveedor principal y LocationIQ como respaldo. Las claves no se entregan al navegador.

La aplicación mantiene en PostgreSQL un contador diario por proveedor y operación. Antes de cada llamada reserva atómicamente una unidad de cuota. Por defecto se emite aviso al 80 % y se deja de consumir un proveedor al alcanzar el 90 % de su límite configurado, pasando al siguiente proveedor.

Configuración de producción:

~~~text
Geoapify__ApiKey=<GEOAPIFY_API_KEY>
LocationIQ__ApiKey=<LOCATIONIQ_API_KEY>
AddressProviders__Primary=Geoapify
AddressProviders__Fallback=LocationIQ
AddressProviders__WarningThreshold=0.80
AddressProviders__FailoverThreshold=0.90
AddressProviders__GeoapifyDailyLimit=3000
AddressProviders__LocationIqDailyLimit=5000
~~~

Los límites anteriores son valores de referencia para los planes gratuitos actuales; deben revisarse contra las condiciones de las cuentas contratadas antes de una puesta en producción. No se deben asumir como límites permanentes del proveedor.

En Geoapify se recomienda restringir la clave al servidor de producción mediante su IP pública y habilitar únicamente las APIs necesarias. LocationIQ también debe configurarse con las restricciones disponibles.

La aplicación restringe las consultas a España, aplica debounce en Angular, rate limiting en el backend y no envía las claves al cliente.

La interfaz muestra la atribución de ambos proveedores para que el cambio automático no elimine los requisitos de atribución del proveedor de respaldo.

El bootstrap crea además placeholders en la tabla config:

- geoapifyApiKey
- locationIqApiKey
- addressPrimaryProvider
- addressFallbackProvider
- addressWarningThreshold
- addressFailoverThreshold
- addressGeoapifyDailyLimit
- addressLocationIqDailyLimit

Las claves reales deben permanecer en /etc/dietoexpress/dietoexpress.env; los placeholders del bootstrap no contienen secretos.

Antes de producción:

1. Crear las cuentas/API keys de Geoapify y LocationIQ.
2. Verificar que el uso comercial del plan elegido y sus requisitos de atribución son compatibles con DietoExpress.
3. Añadir ambas claves al archivo de entorno.
4. Ajustar límites y umbrales si las cuentas contratadas tienen valores diferentes.
5. Reiniciar DietoExpress.
6. Probar una dirección española completa.
7. Comprobar en PostgreSQL que external_api_usage incrementa el proveedor utilizado.
8. Probar en un entorno controlado el failover cuando se alcanza el umbral.

## USDA

- usdaApiKey

## Frontend

- frontendUrl

## Web Push

- webPushSubject
- webPushPublicKey
- y las claves/valores adicionales requeridos por la implementación vigente.

Antes de una instalación definitiva, revisar las llamadas actuales a ConfigServ y DatabaseBootstrap para actualizar esta lista si se han añadido nuevas integraciones.

No poner secretos reales en este documento.

---

# 14. SMTP

Configurar un proveedor SMTP real antes de producción.

Valores conceptuales:

~~~text
smtpServer=<SMTP_SERVER>
smtpPort=587
smtpEnableSsl=1
smtpFromEmail=<SMTP_FROM>
smtpFromName=DietoExpress
smtpUser=<SMTP_USER>
smtpPwd=<SMTP_PASSWORD>
~~~

Probar:

- recuperación de contraseña;
- invitación/alta de profesionales;
- correos del portal;
- cualquier automatización que envíe correo.

---

# 15. Google OAuth y Calendar

Si se utiliza:

1. crear/configurar proyecto en Google Cloud;
2. configurar OAuth;
3. autorizar el dominio de producción;
4. registrar los redirect URI exactos;
5. configurar googleClientId;
6. configurar permisos de Calendar;
7. probar login;
8. probar conexión y renovación de Calendar.

Nunca publicar client secrets.

---

# 16. USDA y Open Food Facts

Para USDA:

1. obtener API key;
2. guardarla como usdaApiKey;
3. realizar una consulta real;
4. comprobar límites.

Open Food Facts no utiliza la misma API key de USDA en la implementación actual.

---

# 17. Stripe Live

Configurar:

~~~text
Stripe__SecretKey=sk_live_...
Stripe__WebhookSecret=whsec_...
~~~

Nunca usar claves de test en producción.

Los Price IDs de los planes comerciales se almacenan en subscription_plans. Los planes actuales incluyen nutri_full y clinic_full y la clínica puede tener Price IDs adicionales para puestos profesionales.

Después de crear los productos/precios en Stripe Live:

1. introducir los Price IDs;
2. comprobar intervalo mensual/anual;
3. comprobar precios de puestos adicionales;
4. configurar el webhook.

Webhook:

~~~text
https://<DOMINIO>/api/billing/stripe/webhook
~~~

Probar:

- checkout;
- activación;
- cambio de plan;
- cancelación de renovación;
- reactivación;
- puestos de clínica;
- pago fallido;
- webhook duplicado/repetido.

La aplicación utiliza la base local como fuente de estado operativo y sincroniza cambios confirmados mediante webhooks.

---

# 18. Nginx

Instalar:

~~~bash
sudo apt install -y nginx
sudo systemctl enable --now nginx
sudo systemctl status nginx
~~~

Nginx debe:

1. atender HTTP;
2. redirigir a HTTPS;
3. atender HTTPS;
4. servir /var/www/dietoexpress;
5. reenviar /api/ al backend;
6. pasar Host, X-Real-IP, X-Forwarded-For y X-Forwarded-Proto;
7. no exponer el puerto interno de ASP.NET Core.

Ejemplo conceptual:

~~~nginx
server {
    listen 80;
    server_name <DOMINIO>;

    location / {
        return 301 https://$host$request_uri;
    }
}

server {
    listen 443 ssl http2;
    server_name <DOMINIO>;

    root /var/www/dietoexpress;

    location /api/ {
        proxy_pass http://127.0.0.1:<PORT>;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }

    location / {
        try_files $uri $uri/ /index.html;
    }
}
~~~

El puerto real debe coincidir con la configuración del servicio. No copiar el ejemplo sin adaptarlo.

Comprobar:

~~~bash
sudo nginx -t
sudo systemctl reload nginx
~~~

---

# 19. DNS

Crear el registro A hacia la IP pública y AAAA solo si IPv6 está realmente configurado.

Comprobar:

~~~bash
dig +short <DOMINIO>
~~~

No solicitar Let's Encrypt hasta que DNS resuelva correctamente.

---

# 20. Let's Encrypt

Instalar Certbot según la distribución.

Después de configurar DNS y Nginx:

~~~bash
sudo certbot --nginx -d <DOMINIO>
sudo certbot certificates
sudo certbot renew --dry-run
~~~

La instalación actual utiliza un hook de despliegue:

/etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh

Contenido:

~~~bash
#!/bin/bash
set -e

/usr/bin/systemctl reload nginx
~~~

Permisos:

~~~bash
sudo chmod 755 /etc/letsencrypt/renewal-hooks/deploy/reload-nginx.sh
~~~

Comprobar desde el exterior:

- certificado válido;
- HTTP → HTTPS;
- sin contenido mixto;
- aplicación accesible.

---

# 21. systemd

Crear:

/etc/systemd/system/dietoexpress.service

Ejemplo:

~~~ini
[Unit]
Description=DietoExpress ASP.NET Core
After=network-online.target postgresql.service
Wants=network-online.target

[Service]
WorkingDirectory=/opt/dietoexpress
ExecStart=/usr/bin/dotnet /opt/dietoexpress/Anguloso.Server.dll
Restart=always
RestartSec=5
User=joso
Group=joso
EnvironmentFile=/etc/dietoexpress/dietoexpress.env
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:<PORT>

[Install]
WantedBy=multi-user.target
~~~

Adaptar la ruta de dotnet si procede.

Activar:

~~~bash
sudo systemctl daemon-reload
sudo systemctl enable dietoexpress.service
sudo systemctl start dietoexpress.service
sudo systemctl status dietoexpress.service
sudo journalctl -u dietoexpress.service -n 100 --no-pager
~~~

La aplicación no debe ejecutarse como root.

---

# 22. Primer arranque

Antes de arrancar comprobar:

- PostgreSQL activo;
- base creada;
- connection string correcta;
- JWT válido;
- CORS correcto;
- Logs existente y escribible;
- AlertSpool existente y escribible;
- configuración SMTP;
- frontendUrl;
- Stripe si aplica;
- Google/USDA/Web Push si aplican.

Arrancar:

~~~bash
sudo systemctl restart dietoexpress.service
sudo systemctl status dietoexpress.service
sudo journalctl -u dietoexpress.service --since "10 minutes ago" --no-pager
~~~

El servicio no se considera correcto si está reiniciándose en bucle.

---

# 23. Bootstrap y BEDCA

El arranque:

1. comprueba PostgreSQL;
2. crea/actualiza el esquema;
3. crea system_alerts;
4. ejecuta los upgrades de módulos;
5. sincroniza alertas pendientes;
6. si no hay alimentos BEDCA, intenta la importación inicial.

Comprobar:

~~~bash
sudo -u postgres psql -d <DATABASE> -c '\dt'
sudo -u postgres psql -d <DATABASE> -c 'SELECT COUNT(*) FROM users;'
sudo -u postgres psql -d <DATABASE> -c 'SELECT COUNT(*) FROM foods;'
sudo -u postgres psql -d <DATABASE> -c 'SELECT COUNT(*) FROM system_alerts;'
~~~

Revisar siempre el journal después del primer arranque.

---

# 24. Primer SuperAdmin

El sistema debe utilizar únicamente:

- superadmin;
- nutritionist;
- clinic_admin.

No deben quedar admin ni user.

Crear/recuperar el primer SuperAdmin mediante el mecanismo de setup/recovery de la versión desplegada.

Después:

1. comprobar login;
2. comprobar administración;
3. comprobar que un usuario normal no puede elevarse a SuperAdmin;
4. comprobar que no queda abierto el mecanismo de setup inicial más tiempo del necesario.

---

# 25. Frontend

El build de Angular se genera en:

anguloso.client/dist/anguloso.client/

El workflow lo copia a:

/var/www/dietoexpress/

y también a:

/opt/dietoexpress/wwwroot/

Comprobar:

~~~bash
find /var/www/dietoexpress -maxdepth 2 -type f | head
~~~

---

# 26. CI/CD

El workflow actual realiza:

1. checkout;
2. npm install;
3. npm audit;
4. build Angular;
5. dotnet restore;
6. auditoría de dependencias .NET;
7. build .NET;
8. pruebas de seguridad;
9. publish .NET;
10. preparación de artefactos;
11. comprobación de Logs;
12. comprobación de AlertSpool;
13. copia del backend;
14. copia del frontend;
15. reinicio systemd.

Los directorios persistentes **no se crean durante el deploy**. Si faltan, el deploy debe fallar antes de reemplazar la versión desplegada.

---

# 27. Self-hosted runner

El workflow actual utiliza:

~~~yaml
runs-on: [self-hosted, Linux, X64]
~~~

Si el runner está en el mismo servidor, necesita herramientas de build y permisos de despliegue.

No debe ejecutarse como root.

La situación ideal para un servidor comercial es separar runner y producción. Si se mantiene el runner en producción, documentar y auditar expresamente:

- usuario del runner;
- permisos sobre /opt/dietoexpress;
- permisos sobre /var/www/dietoexpress;
- permisos de systemctl;
- reglas sudoers;
- ausencia de permisos de escritura sobre /etc/ssh, /etc/sudoers y secretos salvo necesidad explícita.

El workflow actual utiliza sudo únicamente para habilitar/reiniciar el servicio. No debe añadirse sudo general al runner.

---

# 28. Prueba funcional completa

## Infraestructura

- [ ] DNS resuelve.
- [ ] HTTP redirige a HTTPS.
- [ ] certificado válido.
- [ ] Nginx activo.
- [ ] PostgreSQL activo.
- [ ] DietoExpress activo.
- [ ] puerto interno no público.

## Seguridad

- [ ] CORS limitado.
- [ ] JWT de producción.
- [ ] sin secretos en Git.
- [ ] roles normalizados.
- [ ] PostgreSQL no expuesto.
- [ ] runner sin privilegios excesivos.

## Aplicación

- [ ] landing;
- [ ] login;
- [ ] logout;
- [ ] recuperación de contraseña;
- [ ] SuperAdmin;
- [ ] Nutritionist;
- [ ] Clinic Admin;
- [ ] pacientes;
- [ ] dietas;
- [ ] asignaciones;
- [ ] PDFs;
- [ ] BEDCA;
- [ ] portal de paciente.

## Integraciones

- [ ] SMTP;
- [ ] Google;
- [ ] USDA;
- [ ] Open Food Facts;
- [ ] Web Push;
- [ ] Stripe.

---

# 29. Prueba específica de AlertSpool

En una ventana controlada:

1. confirmar que el spool es escribible;
2. provocar una alerta de persistencia de forma segura;
3. comprobar que aparece el JSON pendiente;
4. recuperar PostgreSQL;
5. reiniciar DietoExpress;
6. comprobar que FlushPendingAsync inserta la alerta;
7. comprobar que el archivo solo desaparece después de una inserción correcta.

No realizar esta prueba destructiva sobre datos reales.

---

# 30. Logs y retención

Serilog escribe diariamente en /var/lib/dietoexpress/Logs y la configuración actual no impone una retención finita.

Por tanto, el servidor comercial debe tener una política de retención/logrotate para evitar llenar el disco.

No aplicar logrotate sobre AlertSpool.

Monitorizar especialmente:

~~~bash
du -sh /var/lib/dietoexpress/Logs
du -sh /var/lib/dietoexpress/AlertSpool
df -h
~~~

Un crecimiento inesperado de AlertSpool puede indicar una caída de PostgreSQL o un problema persistente de escritura.

---

# 31. Backups

Un servidor de producción no se considera terminado hasta disponer de recuperación probada.

Backup PostgreSQL de ejemplo:

~~~bash
pg_dump -Fc -d <DATABASE> -f /ruta/segura/dietoexpress-$(date +%F).dump
~~~

El backup debe estar fuera del disco principal y, preferiblemente, fuera del propio proveedor.

Respaldar, según política:

- PostgreSQL;
- /etc/dietoexpress/dietoexpress.env;
- configuración Nginx;
- configuración systemd;
- material de recuperación de Let's Encrypt;
- Logs si deben conservarse;
- AlertSpool si contiene datos pendientes;
- cualquier nuevo almacenamiento persistente que aparezca.

Los secretos de los backups deben estar cifrados.

Probar periódicamente una restauración en un servidor independiente.

---

# 32. Monitorización

Revisar:

~~~bash
systemctl is-active dietoexpress.service
systemctl is-active nginx
systemctl is-active postgresql
df -h
free -h
journalctl -u dietoexpress.service --since "1 hour ago"
~~~

Debe existir alertado para:

- servicio caído;
- PostgreSQL caído;
- disco lleno;
- certificado próximo a caducar;
- backup fallido;
- errores críticos de bootstrap;
- crecimiento anormal de AlertSpool.

---

# 33. Actualización normal

Antes de cada actualización:

1. revisar el commit;
2. comprobar CI;
3. hacer backup;
4. comprobar espacio;
5. comprobar PostgreSQL;
6. desplegar;
7. comprobar build/test/publish;
8. comprobar systemd;
9. revisar journal;
10. probar HTTPS;
11. comprobar bootstrap;
12. comprobar funcionalidad crítica.

No borrar durante un deploy:

- /var/lib/dietoexpress/Logs
- /var/lib/dietoexpress/AlertSpool

---

# 34. Rollback

Si falla únicamente el código y el esquema sigue siendo compatible:

1. detener servicio;
2. restaurar artefacto anterior;
3. arrancar;
4. verificar.

Si la nueva versión ya cambió el esquema de forma incompatible, restaurar solo el binario no es suficiente.

En ese caso:

1. detener servicio;
2. restaurar backup PostgreSQL compatible;
3. restaurar backend/frontend anterior;
4. restaurar configuración si procede;
5. arrancar;
6. verificar.

Toda migración no reversible debe tener un procedimiento de rollback antes de entrar en producción.

---

# 35. Recuperación completa de un servidor perdido

1. Provisionar nuevo Linux.
2. Actualizarlo.
3. Configurar NTP/zona horaria.
4. Endurecer SSH.
5. Configurar firewall.
6. Crear usuario de aplicación.
7. Instalar PostgreSQL.
8. Crear base/usuario.
9. Restaurar backup.
10. Instalar .NET.
11. Crear /opt/dietoexpress.
12. Crear /var/www/dietoexpress.
13. Crear /var/lib/dietoexpress/Logs.
14. Crear /var/lib/dietoexpress/AlertSpool.
15. Restaurar dietoexpress.env desde backup seguro.
16. Instalar Nginx.
17. Configurar DNS.
18. Emitir certificado.
19. Configurar systemd.
20. Desplegar aplicación.
21. Revisar bootstrap.
22. Configurar SMTP/Google/USDA/Web Push/Stripe.
23. Configurar webhook.
24. Comprobar login/portal/billing.
25. Verificar backups.
26. Verificar monitorización.

La recuperación debe poder ejecutarse sin depender de archivos que solo existan en el servidor perdido.

---

# 36. Checklist de alta de un servidor comercial

## Sistema
- [ ] Linux actualizado.
- [ ] NTP activo.
- [ ] SSH endurecido.
- [ ] firewall activo.
- [ ] root remoto deshabilitado.
- [ ] usuario de aplicación sin privilegios administrativos.

## PostgreSQL
- [ ] instalado.
- [ ] base creada.
- [ ] usuario creado.
- [ ] 5432 no público.
- [ ] backup configurado.
- [ ] restauración probada.

## Aplicación
- [ ] .NET correcto.
- [ ] /opt/dietoexpress creado.
- [ ] /var/www/dietoexpress creado.
- [ ] /var/lib/dietoexpress/Logs creado.
- [ ] /var/lib/dietoexpress/AlertSpool creado.
- [ ] propietario/permisos correctos.
- [ ] /etc/dietoexpress/dietoexpress.env creado y protegido.
- [ ] systemd activo.

## Web
- [ ] DNS.
- [ ] Nginx.
- [ ] HTTPS.
- [ ] renovación Let's Encrypt.
- [ ] puerto interno no público.

## Integraciones
- [ ] SMTP.
- [ ] Google.
- [ ] USDA.
- [ ] Open Food Facts.
- [ ] Web Push.
- [ ] Stripe Live.
- [ ] Stripe webhook.

## CI/CD
- [ ] runner configurado si corresponde.
- [ ] runner sin privilegios excesivos.
- [ ] deploy con acceso a directorios de aplicación.
- [ ] deploy puede reiniciar solo DietoExpress.
- [ ] deploy verifica Logs y AlertSpool.
- [ ] deploy no crea datos persistentes.

---

# 37. Regla para futuras funcionalidades

Cualquier nueva funcionalidad que añada almacenamiento persistente fuera del directorio de aplicación debe actualizar esta guía antes de considerarse lista para producción.

Para cada nuevo recurso persistente documentar:

1. ruta;
2. propietario;
3. grupo;
4. permisos;
5. proceso que escribe;
6. contenido;
7. inclusión en backup;
8. posibilidad o imposibilidad de borrado;
9. comportamiento durante deploy;
10. recuperación.

La documentación de infraestructura debe evolucionar junto con el código.

---

# 38. Estado de referencia actual

- Backend: ASP.NET Core .NET 8.
- Frontend: Angular 20.
- Base de datos: PostgreSQL.
- Proxy: Nginx.
- HTTPS: Let's Encrypt.
- Ejecución: systemd.
- CI/CD: GitHub Actions.
- Backend publicado: /opt/dietoexpress.
- Frontend Nginx: /var/www/dietoexpress.
- Logs persistentes: /var/lib/dietoexpress/Logs.
- AlertSpool persistente: /var/lib/dietoexpress/AlertSpool.
- Configuración de servicio: /etc/dietoexpress/dietoexpress.env.
- Usuario de servicio de la instalación actual: joso.

Los datos específicos del servidor comercial no deben introducirse en este documento del repositorio.
