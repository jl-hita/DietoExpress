# Política de cookies de DietoExpress

> **Versión técnica preparada — pendiente de validación jurídica y comprobación final en producción.**

## 1. Objeto

Esta política explica las cookies y tecnologías similares utilizadas por DietoExpress, su finalidad y la forma de gestionarlas.

## 2. Cookies utilizadas

DietoExpress utiliza actualmente las siguientes cookies propias de sesión:

| Cookie | Finalidad | Duración | Proveedor | Tipo |
|---|---|---|---|---|
| `dietoexpress_professional_session` | Mantener la sesión autenticada y aplicar autorización y seguridad | 3 horas | DietoExpress | Necesaria |
| `dietoexpress_patient_session` | Mantener la sesión autenticada del portal de paciente | 8 horas | DietoExpress | Necesaria |
| `g_state` | Estado de cierre de sesión de Google Identity Services | Aproximadamente 6 meses en la observación de producción | Google Identity Services; cookie en el dominio de DietoExpress | Pendiente de evaluación de necesidad/consentimiento |

Las dos cookies propias de sesión de DietoExpress se configuran como `HttpOnly`, `Secure` y `SameSite=Strict`. En producción también se observó `g_state` en el dominio de DietoExpress, con `Path=/` y expiración aproximada de seis meses; sus atributos `HttpOnly`, `Secure` y `SameSite` aparecían vacíos en DevTools. Google describe `g_state` como parte del estado de Google Identity Services relacionado con el cierre de sesión. La evidencia manual más reciente apunta a que se crea al inicializar GIS; su clasificación como tecnología estrictamente necesaria sigue pendiente de validación jurídica.

No se utilizan cookies propias de publicidad ni de analítica de terceros en el código cliente auditado.

## 3. Tecnologías similares

El portal utiliza `localStorage` para recordar las comidas completadas del día (`completed_meals_<clientId>_<date>`) y las casillas marcadas de la lista de la compra (`shopping_<clientId>`). Son estados funcionales del portal, no tecnologías publicitarias; se eliminan al cerrar sesión del paciente.

El *service worker* de notificaciones se registra al activar manualmente los avisos; la suscripción Web Push se crea después de conceder el permiso del navegador. No se registra automáticamente al abrir el portal de paciente.

La analítica pública del embudo se registra server-side y no utiliza cookies ni almacenamiento del navegador para su deduplicación.

## 4. Servicios y recursos de terceros

Outfit y Material Icons se sirven desde activos estáticos del propio sitio, sin cargar Google Fonts ni Material Icons desde servidores de Google. Google Identity Services se carga de forma diferida en las pantallas de inicio de sesión y creación de cuenta, no desde la página pública inicial. Google Calendar usa un flujo OAuth de redirección a Google y el intercambio de tokens se realiza en el backend. Stripe Checkout se crea desde el backend y el navegador se redirige a la URL de la sesión alojada por Stripe; no se ha localizado una integración de Stripe.js/Elements embebida en las páginas de DietoExpress. LiveKit se conecta desde el componente de videollamada, cargado bajo demanda, y solo al entrar en la ruta de consulta online.

Cada integración deberá revisarse según el flujo real y su configuración efectiva. Esta política no debe presentar una integración como una cookie si el servicio no la coloca.

## 5. Tecnologías no necesarias

No se ha identificado un SDK de analítica publicitaria en el cliente. En pruebas manuales comparativas con un navegador incógnito limpio, la landing pública no creó `g_state` y cargar únicamente el script GIS tampoco. En una prueba controlada, solicitar el client ID, cargar GIS y ejecutar `google.accounts.id.initialize(...)` hizo que apareciera `g_state`, sin renderizar el botón, invocar `prompt()` ni pulsarlo. La evidencia acota el desencadenante a la inicialización de GIS, pero no demuestra por sí sola todos los usos de la cookie ni su calificación jurídica.

La decisión sobre un banner queda pendiente de repetir las pruebas en producción tras estos cambios, revisar las integraciones de terceros por flujo y completar la validación jurídica. Si se determina que una tecnología requiere consentimiento, su carga deberá bloquearse hasta obtenerlo y se actualizarán el inventario y esta política.

## 6. Gestión y retirada

Las cookies necesarias para autenticación y seguridad se utilizan para prestar las funciones solicitadas y no se ofrecen como una categoría opcional de consentimiento.

Cuando DietoExpress incorpore tecnologías cuyo tratamiento dependa del consentimiento, el usuario podrá aceptar, rechazar o configurar las categorías correspondientes y retirar posteriormente su consentimiento de forma sencilla.

## 7. Cambios

El inventario se revisará cuando se introduzcan nuevas librerías, analítica, publicidad, widgets o servicios de terceros, y también cuando cambie la configuración de producción.

## 8. Control de versión

- Clave: `cookie_policy`
- Versión técnica: 2
- Estado: `pending_legal_validation`
- Fecha efectiva: pendiente de publicación definitiva.
- Pendiente: repetir pruebas de terceros/cookies en producción y resolver jurídicamente si procede banner de consentimiento.
