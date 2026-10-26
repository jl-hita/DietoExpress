# Notificaciones push para pacientes (P1)

La app Android registra tokens FCM en `POST /api/portal/push-tokens`; al cerrar sesión revoca el token mediante `POST /api/portal/push-tokens/revoke`. Los endpoints requieren una sesión válida de paciente y el encabezado `X-DietoExpress-Patient-Portal: 1`.

## Configuración del servidor

El envío está desactivado de forma segura mientras falte la configuración. Para activarlo:

1. Crea o selecciona el proyecto Firebase que contiene `com.dietoexpress.mobile` y habilita Firebase Cloud Messaging.
2. Crea una cuenta de servicio con el permiso mínimo necesario para enviar mensajes FCM.
3. Guarda el JSON de la cuenta de servicio fuera del repositorio, por ejemplo en `/etc/dietoexpress/fcm-service-account.json`, con permisos restrictivos y lectura para el usuario del servicio.
4. Añade al archivo de entorno de systemd:

```ini
DIETOEXPRESS_FCM_PROJECT_ID=tu-project-id
DIETOEXPRESS_FCM_SERVICE_ACCOUNT_PATH=/etc/dietoexpress/fcm-service-account.json
```

5. Reinicia el servicio tras desplegar la versión que incluye este cambio.

También se admite `DIETOEXPRESS_FCM_SERVICE_ACCOUNT_JSON`, aunque se recomienda el archivo protegido para evitar problemas de escape en archivos de entorno. Nunca subas el JSON al repositorio ni lo incluyas en el APK.

## Alcance actual

- Los tokens se almacenan por paciente y se actualizan si FCM los rota.
- Un token se revoca al cerrar sesión en la app.
- Los mensajes nuevos enviados por un profesional generan un aviso push genérico, sin exponer el contenido del mensaje en la notificación.
- Los tokens que FCM informa como no registrados se eliminan.
- Si FCM no está configurado o falla, no se pierde el mensaje guardado ni se devuelve un error falso al emisor.
