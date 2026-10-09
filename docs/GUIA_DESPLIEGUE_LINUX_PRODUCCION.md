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


El directorio raíz `/var/lib/dietoexpress` también contiene el estado del aviso de mantenimiento usado durante las operaciones de restauración de backups (`maintenance.json`). El servicio se ejecuta como `joso`, por lo que debe poder crear el archivo temporal `maintenance.json.tmp` en ese directorio. Este permiso se prepara **una sola vez en el servidor**, no desde GitHub Actions.

Preparar o reparar los permisos (por ejemplo, si la restauración de un backup falla con `UnauthorizedAccessException` sobre `maintenance.json.tmp`):

~~~bash
sudo install -d -o joso -g joso -m 750 /var/lib/dietoexpress
sudo chown joso:joso /var/lib/dietoexpress/maintenance.json \\
  /var/lib/dietoexpress/maintenance.json.tmp 2>/dev/null || true
sudo -u joso test -w /var/lib/dietoexpress \\
  && echo "Directorio escribible por joso"
~~~

El mensaje `Directorio escribible por joso` confirma que el usuario del servicio puede escribir en el directorio. El `chown` sobre los dos archivos existentes es deliberadamente tolerante a que todavía no existan. No borrar `maintenance.json` manualmente durante una restauración o mientras una operación de mantenimiento esté activa.

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

La configuración de los proveedores de direcciones se almacena en la tabla `config` de PostgreSQL y se carga mediante `ConfigServ`. Las claves reales no se versionan ni se entregan al navegador.

Parámetros:

~~~text
geoapifyApiKey=<GEOAPIFY_API_KEY>
locationIqApiKey=<LOCATIONIQ_API_KEY>
addressPrimaryProvider=Geoapify
addressFallbackProvider=LocationIQ
addressWarningThreshold=0.80
addressFailoverThreshold=0.90
addressGeoapifyDailyLimit=3000
addressLocationIqDailyLimit=5000
~~~

Los límites anteriores son valores de referencia para los planes gratuitos actuales; deben revisarse contra las condiciones de las cuentas contratadas antes de una puesta en producción. No se deben asumir como límites permanentes del proveedor.

En Geoapify se recomienda restringir la clave al servidor de producción mediante su IP pública y habilitar únicamente las APIs necesarias. LocationIQ también debe configurarse con las restricciones disponibles.

La aplicación restringe las consultas a España, aplica debounce en Angular, rate limiting en el backend y no envía las claves al cliente.

La interfaz muestra la atribución de ambos proveedores para que el cambio automático no elimine los requisitos de atribución del proveedor de respaldo.

El bootstrap crea automáticamente los valores iniciales si todavía no existen en `config`, incluidos placeholders para las dos claves. Sustituir los placeholders por las claves reales mediante el mecanismo de administración de configuración antes de utilizar el autocompletado en producción.

Antes de producción:

1. Crear las cuentas/API keys de Geoapify y LocationIQ.
2. Verificar que el uso comercial del plan elegido y sus requisitos de atribución son compatibles con DietoExpress.
3. Configurar ambas claves en `config` mediante el mecanismo administrativo previsto.
4. Ajustar límites y umbrales si las cuentas contratadas tienen valores diferentes.
5. Reiniciar DietoExpress para que la nueva configuración quede cargada en los servicios singleton.
6. Probar una dirección española completa.
7. Comprobar en PostgreSQL que `external_api_usage` incrementa el proveedor utilizado.
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

Si se utiliza la integración con Google Calendar, la configuración de producción debe quedar documentada y separada de cualquier entorno de pruebas.

## Configuración del servidor

Definir en `/etc/dietoexpress/dietoexpress.env`:

~~~text
GoogleCalendar__ClientId=<CLIENT_ID>.apps.googleusercontent.com
GoogleCalendar__ClientSecret=<CLIENT_SECRET>
GoogleCalendar__RedirectUri=https://<DOMINIO>/api/google-calendar/callback
~~~

El `RedirectUri` debe coincidir exactamente con el URI autorizado en Google Cloud, incluyendo esquema HTTPS, dominio, ruta y ausencia/presencia de una barra final.

Nunca guardar el client secret en Git ni en el frontend.

## Google Cloud

1. Crear, o seleccionar, el proyecto de Google Cloud destinado a DietoExpress.
2. Configurar la pantalla de consentimiento OAuth.
3. Para las pruebas iniciales puede utilizarse el estado **Prueba** y añadir la cuenta que realizará las pruebas como usuario de prueba.
4. Autorizar el dominio de producción (`<DOMINIO>`).
5. Crear un cliente OAuth de tipo **Aplicación web**.
6. Añadir como URI de redirección autorizado exactamente:
   `https://<DOMINIO>/api/google-calendar/callback`
7. **Activar explícitamente la API de Google Calendar en el proyecto que contiene el OAuth Client ID.** No basta con crear el cliente OAuth ni con configurar la pantalla de consentimiento.
8. Comprobar que el Client ID y Client Secret utilizados por el servidor pertenecen al mismo proyecto y cliente OAuth.
9. Confirmar en **Google Cloud → APIs y servicios → APIs habilitadas** que aparece `Google Calendar API` y que está habilitada.
10. Si se utiliza el Client ID actual de DietoExpress, verificar el proyecto por el número de proyecto que aparece asociado al cliente OAuth; no asumir que el nombre visible del proyecto coincide con el número usado por la API.

## Comprobación obligatoria de Google Calendar API antes de probar la sincronización

La autorización OAuth y el acceso a Google Calendar son dos configuraciones distintas. Es posible completar correctamente todo el consentimiento OAuth y, aun así, recibir un `403 SERVICE_DISABLED` al sincronizar si **Google Calendar API no está habilitada en el proyecto del OAuth Client**.

Antes de dar por válida la instalación, comprobar:

1. Abrir Google Cloud con el proyecto que contiene el OAuth Client ID utilizado por `/etc/dietoexpress/dietoexpress.env`.
2. Ir a **APIs y servicios → Biblioteca**.
3. Buscar **Google Calendar API**.
4. Pulsar **Habilitar** si no está habilitada.
5. Volver a **APIs y servicios → APIs habilitadas** y confirmar que `Google Calendar API` aparece activa.
6. Esperar unos minutos si se acaba de activar.
7. En DietoExpress, comprobar primero **Conectar Google Calendar** y después **Sincronizar**.
8. Revisar el journal si la sincronización falla:

~~~bash
sudo journalctl -u dietoexpress.service --since "10 minutes ago" --no-pager | grep -E "Google OAuth|Google Calendar"
~~~

El error `SERVICE_DISABLED`, `accessNotConfigured` o el mensaje **"Google Calendar API has not been used in project ... or it is disabled"** significa que la API no está habilitada en el proyecto que está usando el OAuth Client. **No es necesario desconectar y volver a autorizar la cuenta** después de habilitar la API: la conexión OAuth existente puede reutilizarse.

Como comprobación de seguridad, el número de proyecto indicado por Google debe corresponder al proyecto del OAuth Client configurado en el servidor. Si no coincide, detener el diagnóstico y corregir el Client ID/proyecto antes de continuar.

## Pasar Google Cloud de Prueba a Producción

Antes de abrir DietoExpress a usuarios reales, **no dejar la aplicación OAuth en estado Prueba**.

El paso de producción debe realizarse explícitamente:

1. Terminar las pruebas de OAuth y Google Calendar en modo Prueba.
2. Revisar la pantalla de consentimiento, nombre de la aplicación, dominio autorizado, correo de soporte y datos de contacto.
3. Revisar los scopes solicitados y mantener únicamente los necesarios. DietoExpress utiliza el acceso de Calendar definido por la implementación vigente.
4. En Google Cloud, cambiar el estado de publicación de la pantalla de consentimiento de **Prueba** a **En producción**.
5. Si Google solicita verificación por el scope utilizado, completar el proceso de verificación antes de ofrecer la integración públicamente.
6. Volver a probar la autorización con una cuenta que no esté configurada como usuario de prueba.
7. Revocar y volver a autorizar una conexión de prueba si es necesario para comprobar el flujo completo de consentimiento.
8. Verificar especialmente que el refresh token funciona después de la publicación y que una sincronización posterior a la caducidad del access token puede renovarlo.
9. Documentar cualquier cambio de Client ID, proyecto o secret y actualizar `/etc/dietoexpress/dietoexpress.env`.

La publicación en producción no sustituye a las pruebas funcionales. Deben probarse al menos:

- conexión;
- desconexión;
- sincronización manual;
- sincronización automática;
- renovación del token;
- bloqueo de disponibilidad por eventos externos;
- aislamiento por tenant.

Durante la fase de Prueba, los refresh tokens de Google pueden tener una caducidad limitada. Para un SaaS real no se debe depender de ese comportamiento de pruebas.

## Diagnóstico de errores OAuth y Calendar

Si Google muestra el consentimiento correctamente pero DietoExpress vuelve a `/appointments?calendar=error`, revisar primero:

~~~bash
sudo journalctl -u dietoexpress.service --since "10 minutes ago" --no-pager | grep -E "Google OAuth|Google Calendar"
~~~

El backend registra el código HTTP y el `error`/`error_description` devueltos por el endpoint de token y, para errores de la API de Calendar, registra el código HTTP y la respuesta de Google sin tokens. Nunca registra el client secret ni los access/refresh tokens.

Para un `403` de sincronización, comprobar primero **Google Calendar API habilitada en el mismo proyecto del OAuth Client**. Para un `401`, revisar la conexión OAuth y la renovación del refresh token. Para errores `404` del calendario, comprobar el `calendarId` y la cuenta conectada.

La API de sincronización no debe considerarse correctamente instalada hasta que una sincronización manual termine con éxito y se observe `last_synced_at` actualizado.

Nunca publicar client secrets ni access/refresh tokens en tickets, logs, capturas o commits.

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

La instalación de producción necesita copias verificables y una restauración probada. La lectura y escritura privilegiadas de las copias se separa del usuario que ejecuta la aplicación.

Configurar en /etc/dietoexpress/dietoexpress.env:

~~~text
DIETOEXPRESS_BACKUP_DIR=/var/lib/dietoexpress-backups
DIETOEXPRESS_DATABASE=<DATABASE>
DIETOEXPRESS_DB_PORT=5432
~~~

El almacén recomendado es /var/lib/dietoexpress-backups, propiedad de root, grupo joso y modo 0750. Los archivos de copia son root:joso con modo 0640: la aplicación puede listarlos/descargarlos, pero no modificarlos. No utilices /var/lib/dietoexpress/backups porque el directorio padre /var/lib/dietoexpress es escribible por joso para el aviso de mantenimiento; un usuario de despliegue no debe poder sustituir el archivo que posteriormente leerá pg_restore como PostgreSQL superuser.

## Migración del almacén anterior

Conserva la carpeta anterior hasta verificar la migración. Primero crea el nuevo directorio y copia los dumps comprobando el checksum de origen. El siguiente bloque no elimina ni altera las copias antiguas:

~~~bash
sudo install -d -o root -g joso -m 0750 /var/lib/dietoexpress-backups
sudo bash -c '
set -euo pipefail
src=/var/lib/dietoexpress/backups
dst=/var/lib/dietoexpress-backups
shopt -s nullglob
for dump in "$src"/dietoexpress-postgresql-*.dump; do
  name=$(basename "$dump")
  manifest="${name%.dump}.sha256"
  (cd "$src" && sha256sum -c "$manifest")
  [[ ! -e "$dst/$name" ]] || { echo "Ya existe $dst/$name; se aborta para no sobrescribir." >&2; exit 1; }
  install -o root -g joso -m 0640 "$dump" "$dst/$name"
  (cd "$dst" && sha256sum "$name" > "$dst/${name%.dump}.sha256")
  if [[ -f "$src/${name%.dump}.txt" ]]; then
    size=$(stat -c "%s" "$dst/$name")
    hash=$(sha256sum "$dst/$name" | cut -d" " -f1)
    sed -e "s|^backup=.*|backup=$dst/$name|" \
        -e "s|^size_bytes=.*|size_bytes=$size|" \
        -e "s|^sha256=.*|sha256=$hash|" \
        "$src/${name%.dump}.txt" > "$dst/${name%.dump}.txt.tmp"
    install -o root -g joso -m 0640 "$dst/${name%.dump}.txt.tmp" "$dst/${name%.dump}.txt"
    rm -f "$dst/${name%.dump}.txt.tmp"
  fi
done
for env_backup in "$src"/dietoexpress-env-*; do
  [[ -f "$env_backup" ]] || continue
  name=$(basename "$env_backup")
  [[ ! -e "$dst/$name" ]] || { echo "Ya existe $dst/$name; se aborta." >&2; exit 1; }
  install -o root -g root -m 0600 "$env_backup" "$dst/$name"
done
'
sudoedit /etc/dietoexpress/dietoexpress.env
~~~

En el archivo, cambia únicamente DIETOEXPRESS_BACKUP_DIR a /var/lib/dietoexpress-backups y conserva los demás secretos/valores. Después ejecuta el provisioning descrito abajo y reinicia DietoExpress para recargar la configuración. Comprueba todos los checksums copiados; conserva el almacén antiguo hasta que la aplicación y la restauración hayan sido verificadas.

## Provisioning de la infraestructura privilegiada

Después de desplegar o actualizar los scripts privilegiados/unidades systemd, ejecutar como administrador:

~~~bash
sudo /opt/dietoexpress/scripts/provision-dietoexpress-backup-automation.sh
sudo visudo -cf /etc/sudoers.d/dietoexpress-restore
sudo systemctl restart dietoexpress.service
sudo systemctl status dietoexpress-backup.timer --no-pager
~~~

El provisioning comprueba que ni el directorio ni sus ancestros sean modificables por joso. Es idempotente y debe repetirse después de cada cambio en helpers root-owned o unidades systemd; copiar archivos a /opt/dietoexpress no actualiza automáticamente las copias instaladas en /usr/local/sbin.

## Creación de backups

El servicio semanal corre como joso, pero dietoexpress-backup.sh delega pg_dump mediante reglas sudoers restringidas a dietoexpress-pg-backup --backup y --backup-pre-restore. El helper root-owned ejecuta pg_dump como postgres y publica el dump, checksum y metadatos en el almacén root-owned.

Los dumps usan formato custom y conservan los propietarios originales; no se debe utilizar --no-owner. Comprueba:

~~~bash
systemctl status dietoexpress-backup.timer
systemctl list-timers dietoexpress-backup.timer
journalctl -u dietoexpress-backup.service --since "7 days ago" --no-pager
ls -lh /var/lib/dietoexpress-backups/
~~~

La retención normal conserva las ocho últimas copias. Los snapshots automáticos previos a restauración llevan el sufijo -pre-restore y tienen retención separada (cuatro por defecto). Los snapshots manuales cuyo nombre contiene -pre- no se eliminan con la rotación normal.

## Restauración y rollback

La restauración pasa por dietoexpress-restore@.service. La unidad detiene el backend; el script de usuario verifica la copia y genera un snapshot previo mediante el mismo helper privilegiado. El helper root-owned vuelve a validar checksum/formato, copia el dump a un directorio temporal accesible a postgres y ejecuta pg_restore sin --no-owner, preservando los propietarios.

Si falla la restauración solicitada, el script intenta rollback usando el snapshot previo. Si el rollback termina bien, systemd vuelve a iniciar DietoExpress aunque la operación quede marcada como fallida. Si también falla el rollback, la unidad deja el backend detenido y conserva el aviso de mantenimiento para evitar arrancar sobre una base potencialmente parcial.

Comando manual autorizado:

~~~bash
sudo /usr/local/sbin/dietoexpress-restore-web dietoexpress-postgresql-XXXXXXXX.dump --confirm
~~~

Tras restaurar, revisar estado de la unidad, dietoexpress.service, logs y smoke tests. No lanzar otra restauración ni iniciar manualmente el backend si consta que falló también el rollback.

Además de PostgreSQL, respalda según la política operativa /etc/dietoexpress/dietoexpress.env, configuración de Nginx y systemd, material de recuperación de Let's Encrypt, Logs si deben conservarse, AlertSpool si contiene datos pendientes y cualquier almacenamiento persistente nuevo. Cifra los backups que contengan secretos y conserva al menos una copia fuera del servidor/proveedor. Prueba periódicamente una restauración en una base o servidor independiente.

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

Si la versión nueva cambió el esquema de forma incompatible:

1. detener servicio;
2. seleccionar y verificar un backup PostgreSQL compatible;
3. restaurar el backend/frontend anterior;
4. restaurar la base con dietoexpress-restore-web, que genera su propio snapshot previo y ejecuta pg_restore como postgres;
5. comprobar propietarios, arranque y smoke tests;
6. conservar el snapshot previo hasta completar la validación.

Si también falla el rollback automático, la unidad mantiene el backend detenido y el aviso de mantenimiento. No reiniciar a ciegas ni borrar evidencia.

Toda migración no reversible debe tener un procedimiento de rollback antes de entrar en producción.

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
