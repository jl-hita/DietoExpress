# Monitorización externa de DietoExpress con HetrixTools Free

## Objetivo

Detectar desde fuera de weirdfish la caída de la web, del backend y de la conexión de la aplicación con PostgreSQL. Enviar correo al detectar una incidencia y al recuperar el servicio, evitando avisos repetidos por una misma caída.

Proveedor elegido: [HetrixTools Uptime Monitor](https://hetrixtools.com/uptime-monitor/), plan Free. La cuenta gratuita ofrece hasta 15 monitores, comprobaciones cada minuto, comprobaciones desde varias ubicaciones, alertas por correo y un historial de disponibilidad sin límite. La cuenta debe abrirse al menos una vez cada 90 días para conservar los recursos activos.

## Endpoints de salud

- `/health/live`: confirma que el proceso ASP.NET Core responde. No consulta PostgreSQL.
- `/health/db`: comprueba la conectividad de PostgreSQL con un timeout de 3 segundos.
- `/health/ready`: indica si la aplicación está lista para operar, incluyendo PostgreSQL.

Las respuestas de error son HTTP 503 y no exponen cadenas de conexión ni detalles internos. No abrir el puerto 5432 a Internet: la comprobación de base de datos pasa por el backend.

## Configuración en HetrixTools

Crear una cuenta desde [Start free](https://hetrixtools.com/uptime-monitor/) y confirmar la dirección de correo. Usar la dirección en la que se quieran recibir las alertas.

Crear estos monitores de tipo Website/HTTP(S):

| Nombre | URL | Estado esperado |
|---|---|---|
| DietoExpress - Web | `https://jlhitap.duckdns.org/` | HTTP 200 |
| DietoExpress - Backend live | `https://jlhitap.duckdns.org/health/live` | HTTP 200 |
| DietoExpress - PostgreSQL | `https://jlhitap.duckdns.org/health/db` | HTTP 200 |
| DietoExpress - Ready | `https://jlhitap.duckdns.org/health/ready` | HTTP 200 |

Si el frontend redirige o la web devuelve un código distinto de 200 de forma intencionada, ajustar el código aceptado según la respuesta real verificada, no aceptar indiscriminadamente cualquier código 4xx/5xx.

Para cada monitor:
1. Intervalo de comprobación: 1 minuto (valor disponible en todos los planes).
2. Seleccionar cuatro ubicaciones de monitorización; priorizar Europa (Amsterdam, London y Frankfurt) y una ubicación adicional.
3. Activar las notificaciones por correo para DOWN y UP/recuperación.
4. Desactivar alertas repetidas mientras continúe la misma incidencia.
5. Usar la confirmación multiubicación/reintentos del proveedor para reducir falsos positivos. El plan comprueba cada minuto, pero el umbral efectivo de notificación puede superar 60 segundos según la confirmación de caída.

## Historial y mantenimiento

- Consultar el informe de cada monitor para ver incidentes, errores y tiempos de respuesta.
- El historial de uptime es ilimitado; el registro de actividad de cuenta del plan Free es de 15 días.
- Iniciar sesión en HetrixTools al menos cada 90 días para evitar la suspensión por inactividad.
- Antes de un mantenimiento planificado, activar Maintenance Mode para evitar alertas esperadas.

## Prueba controlada de caída y recuperación

No detener DietoExpress ni modificar el servicio real. Después de desplegar los endpoints y crear los monitores:

1. Crear un monitor temporal para una URL de prueba que responda HTTP 503 (por ejemplo, una ruta temporal controlada en un entorno de prueba separado; no crear una ruta pública de fallo en producción).
2. Confirmar que HetrixTools registra DOWN y que llega un único correo de caída.
3. Hacer que la URL de prueba responda HTTP 200.
4. Confirmar que HetrixTools registra UP y llega el correo de recuperación.
5. Revisar el informe y borrar el monitor temporal.

No marcar la configuración como verificada hasta haber recibido ambos correos y comprobado el historial. Si no se dispone de un endpoint de prueba seguro, usar la función de test de notificaciones del proveedor, si está disponible, y documentar que valida el correo pero no la detección real de una caída.

## Estado

- [x] Proveedor gratuito elegido.
- [x] Endpoints de salud añadidos al código.
- [ ] CI y revisión del cambio.
- [ ] Merge y despliegue de endpoints.
- [ ] Crear/verificar cuenta HetrixTools y correo de destino.
- [ ] Crear monitores y configurar alertas.
- [ ] Ejecutar prueba controlada y comprobar correo de caída, recuperación e historial.
