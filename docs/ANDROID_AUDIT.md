# Auditoría técnica inicial de la app Android

Fecha: 2026-10-25  
Repositorio auditado: rama `main`, commit `f0e44834a1a0619e30b0346bdff6a1c707696682`.

## Resultado

El backend existente permite construir una app nativa sin duplicar reglas de negocio. La primera fase debe centrarse en el portal del cliente; la agenda profesional vendrá después. Esta revisión es estática: no sustituye a una compilación ni a pruebas contra un entorno desplegado.

## Endpoints y capacidades localizados

### Portal de paciente

- `POST /api/portal/auth`: acepta token de acceso o email/teléfono + passcode.
- `POST /api/portal/request-access-link`: solicita un enlace de acceso.
- `POST /api/portal/logout`: invalida sesiones anteriores incrementando `portal_token_version` y borra la cookie.
- `GET /api/portal/profile`, `/diet` y `/shopping-list`.
- `GET /api/appointments/mine`, `GET /api/appointments/slots`.
- `POST /api/appointments` y `POST /api/appointments/{id}/cancel`.
- `GET /api/appointments/{id}/video-access`, eventos de vídeo y finalización.
- El servicio Angular también referencia documentos, seguimiento, tareas, clientes, mensajería y otras funciones; cada contrato debe verificarse antes de integrarlo en Android.

### Agenda profesional

El servicio web referencia `GET /api/appointments/professional`, disponibilidad, actualización de estado, reenvío de email y endpoints de acciones profesionales. Deben probarse autorización por rol y tenant y las reglas actuales de concurrencia antes de reutilizarlos desde móvil.

## Hallazgos y decisiones

### A1. Sesión del portal basada en cookie

El login no devuelve el JWT en el cuerpo: devuelve los datos mínimos del paciente y establece la cookie `dietoexpress_patient_session`, configurada `HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/`, con caducidad. El logout revoca sesiones previas y borra esa cookie.

**Decisión:** usar un cliente HTTP nativo con gestión de cookies; no copiar el JWT a almacenamiento accesible por la UI ni almacenar el passcode. Probar persistencia, expiración y logout en Android. Si la biblioteca HTTP requiere persistencia de cookies, proteger el material persistido con cifrado respaldado por Android Keystore y no registrar valores de cookie en logs.

### A2. No se localizó integración FCM nativa

El repositorio contiene Web Push mediante suscripciones de navegador y reglas de notificación; la búsqueda estática no encontró un cliente Firebase Cloud Messaging nativo ni un registro de tokens de dispositivo.

**Decisión:** tratar FCM como una integración nueva. Diseñar registro/actualización/revocación del token por sesión y dispositivo, con autorización del backend. No enviar información clínica en el texto visible de las notificaciones.

### A3. Autorización y aislamiento

Los endpoints del portal usan el rol `patient` y derivan el ID del cliente de los claims. Los endpoints profesionales deben conservar las políticas existentes. La UI móvil nunca debe considerarse un límite de seguridad.

**Pruebas obligatorias:** sesión paciente accediendo a datos de otro paciente; sesión profesional accediendo a otro tenant; llamadas sin sesión; sesión caducada; logout seguido de reutilización de cookie; citas no autorizadas y acceso a vídeo fuera de su ventana.

### A4. Vídeo

Existe integración backend con un proveedor de vídeo y endpoints para solicitar acceso, registrar eventos y finalizar consulta. La app deberá usar los permisos y la ventana del servidor, sin generar credenciales de reunión por su cuenta.

### A5. Datos de salud y publicación

Antes de producción: política de privacidad pública y accesible en la app, declaraciones de Google Play aplicables a datos personales/salud, clasificación de contenido, contacto de soporte y pruebas de revisión con cuentas no productivas. No incluir datos clínicos en analytics, logs o notificaciones.

## Arquitectura propuesta

- Kotlin + Jetpack Compose.
- Separación UI/ViewModel, repositorios y cliente HTTP.
- Una única base URL configurable por variante; producción solo HTTPS.
- Sesión en cookie gestionada por cliente HTTP; cierre de sesión remoto.
- Backend ASP.NET Core como autoridad de permisos, tenant y reglas.
- FCM para notificaciones nativas, una vez implementado el registro backend.
- Deep links validados y resueltos tras comprobar la sesión y autorización.
- La web Angular seguirá siendo la interfaz de administración avanzada y edición compleja.

## Orden de implementación

1. Base Android compilable y navegación inicial.
2. Cliente HTTP y sesión, con pruebas de cookie y logout.
3. Login/enlace mágico y perfil del paciente.
4. Dieta y citas del cliente.
5. Pruebas de autorización y errores de red.
6. Agenda profesional.
7. FCM, enlaces profundos y videollamada.
8. Pruebas de dispositivo, accesibilidad, privacidad y publicación.

## Límites de esta auditoría

No se ejecutaron tests, build Android, pruebas de API ni análisis dinámico de seguridad. Los endpoints deben verificarse en el servidor de pruebas antes de considerar cerrada la auditoría.
