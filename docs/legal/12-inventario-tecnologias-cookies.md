# Inventario técnico de cookies y tecnologías similares — DietoExpress

Este documento refleja el inventario que puede verificarse en el código actual de DietoExpress. No sustituye la clasificación jurídica final ni la inspección del entorno de producción.

## 1. Cookies propias detectadas

| Cookie | Proveedor | Finalidad | Duración | Propia/tercero | Necesaria |
|---|---|---|---|---|---|
| `dietoexpress_professional_session` | DietoExpress | Sesión autenticada del profesional, seguridad y autorización | 3 horas | Propia | Sí, para las áreas autenticadas |
| `dietoexpress_patient_session` | DietoExpress | Sesión autenticada del paciente, seguridad y autorización | 30 días desde el último inicio de sesión; revocable al cerrar sesión | Propia | Sí, para el portal autenticado |

Ambas cookies se emiten con `HttpOnly`, `Secure`, `SameSite=Strict` y `Path=/`. No se utilizan para registrar preferencias comerciales.

## 2. Almacenamiento local detectado

| Tecnología | Clave/patrón | Finalidad | Alcance | Necesidad |
|---|---|---|---|---|
| localStorage | `completed_meals_<clientId>_<date>` | Recordar comidas completadas del día | Navegador/dispositivo | Funcional |
| localStorage | `shopping_<clientId>` | Recordar las casillas marcadas de la lista de la compra | Navegador/dispositivo | Funcional |
| sessionStorage | — | Ya no se utiliza para analítica pública | — | — |

Ambas entradas son estados funcionales, no cookies, y no se usan para publicidad. Al cerrar sesión se eliminan las claves `completed_meals_<clientId>_...` y `shopping_<clientId>` asociadas al paciente, para evitar que queden estados funcionales en un dispositivo compartido.

## 3. Analítica pública

La analítica del embudo público se realiza mediante peticiones al backend (`/api/public-funnel/events`) y almacenamiento server-side. El cliente no utiliza cookies ni almacenamiento del navegador para deduplicar esos eventos.

No se han localizado en el cliente referencias a Google Analytics, Google Tag Manager, gtag, Meta Pixel, Hotjar, Matomo o Plausible.

## 4. Recursos de terceros que deben figurar en la revisión de privacidad

El inventario distingue los recursos servidos localmente de las integraciones de terceros; ninguno debe confundirse automáticamente con una cookie:

- **Outfit** y **Material Icons** se sirven desde activos estáticos del propio sitio; no se espera una conexión del navegador a Google Fonts ni a `fonts.gstatic.com` para estos recursos.
- Google Identity Services (`accounts.google.com/gsi/client`) se carga de forma diferida al entrar en las pantallas de inicio de sesión y crear cuenta, no desde la página pública inicial. En producción se observó `g_state` en el dominio de DietoExpress, con una expiración aproximada de seis meses; `HttpOnly`, `Secure` y `SameSite` aparecían vacíos.
- **Prueba de aislamiento de `g_state`:** en incógnito limpio, la landing pública no creó la cookie; cargar solo el script GIS tampoco. En una prueba controlada sobre la landing, tras obtener el client ID y cargar GIS, ejecutar `google.accounts.id.initialize(...)` sí la creó, sin ejecutar `renderButton()`, llamar a `prompt()` ni pulsar un botón. En las rutas de login/registro la aplicación ejecuta esa inicialización. Google documenta `g_state` como estado de cierre de sesión de GIS, pero no se ha demostrado con esta prueba si tiene otros usos en esta configuración. La clasificación como estrictamente necesaria o sujeta a consentimiento permanece pendiente de validación jurídica.

- **Google Calendar:** flujo OAuth de redirección a Google; el backend intercambia el código y conserva los tokens. No se ha encontrado un SDK de Calendar embebido en el navegador.
- **Stripe Checkout:** el backend crea una sesión de Checkout y devuelve su URL; el frontend navega a esa URL. El servicio no envía `ui_mode`, por lo que se aplica el modo alojado por defecto documentado por Stripe. No se ha localizado Stripe.js/Elements en el cliente de DietoExpress.
- **LiveKit:** el componente `video-consultation` se carga mediante ruta diferida. Al entrar en esa pantalla, el frontend pide al backend un token y conecta con el servidor LiveKit; no se ha identificado una carga de LiveKit desde la landing pública.
- **Web Push:** `/sw.js` se sirve desde el mismo origen y el worker solo maneja eventos `push` y `notificationclick`; no usa `Cache API` ni `IndexedDB` en el código revisado. El portal consulta si ya existe un registro/suscripción, pero solo crea el registro persistente del worker dentro de la acción explícita «Activar avisos», después de conceder permiso. La suscripción se envía al backend de DietoExpress.

La existencia de estas integraciones no implica por sí sola que DietoExpress coloque cookies no necesarias; las cookies y el almacenamiento que use cada proveedor deben verificarse en su flujo real.

## 5. Resultado técnico actual

Con el código auditado:

- Se han identificado **dos cookies propias de sesión**, ambas necesarias para las áreas autenticadas.
- La cookie `g_state` observada está asociada a Google Identity Services. Las pruebas aisladas indican que se crea al ejecutar `google.accounts.id.initialize(...)`, no solo al descargar el script ni al renderizar el botón. Su necesidad y régimen de consentimiento siguen pendientes; el dato técnico no basta por sí solo para concluir que haga falta un banner ni para descartarlo.
- No hay un SDK de analítica publicitaria/tercera cargado en el cliente.
- Las fuentes Outfit y los iconos Material se sirven localmente; Google Identity Services se carga de forma diferida en login y creación de cuenta, no en la página pública inicial.
- Se ha eliminado el uso de `sessionStorage` para deduplicar analítica pública, evitando convertir esa deduplicación en una tecnología de almacenamiento del navegador.
- El almacenamiento local restante es funcional para el portal de paciente: comidas completadas y casillas de la lista de la compra. Ambos se limpian al cerrar sesión.
- El service worker Web Push se registra tras una acción explícita para activar avisos; el estado «activado» depende de que exista una suscripción real, no solo de que el navegador exponga `PushManager`.

Las pruebas manuales en incógnito limpio mostraron que la landing pública no tenía `g_state` y que descargar solo el script GIS tampoco la creaba. En una prueba controlada, obtener el client ID, cargar GIS y ejecutar `google.accounts.id.initialize(...)` sí creó la cookie, sin renderizar ni pulsar el botón ni invocar `prompt()`. Esto es compatible con la observación de que la cookie aparece al abrir el login, porque la aplicación inicializa GIS en esa ruta. Google documenta `g_state` como parte del estado de cierre de sesión de GIS y la relaciona con One Tap o el inicio de sesión automático; la prueba no demuestra por sí sola todos sus usos ni resuelve su clasificación jurídica. La necesidad/consentimiento siguen pendientes.

**Las pruebas manuales de producción se completaron el 18/10/2026** después del despliegue: landing pública sin carga de GIS ni `g_state` en sesión limpia; GIS y autenticación Google operativos en login/registro; datos funcionales de `localStorage` limpiados al cerrar sesión; `/sw.js` registrado solo tras activar los avisos, con suscripción push real; redirección OAuth de Google Calendar, Checkout alojado por Stripe y carga de LiveKit bajo demanda comprobados. No se detectaron fallos en estas comprobaciones.

**La decisión sobre la necesidad de un banner sigue pendiente** de resolver la clasificación jurídica de `g_state`. No se debe concluir que el banner es innecesario únicamente porque no haya analítica publicitaria en el cliente.

## 6. Verificación técnica de producción — completada el 18/10/2026

Las comprobaciones manuales se ejecutaron tras el despliegue en producción y se comunicaron como correctas:

1. La landing pública en una sesión de incógnito limpia no solicitó `fonts.googleapis.com`, `fonts.gstatic.com` ni el script `accounts.google.com/gsi/client`, y no presentó `g_state`.
2. Login y creación de cuenta cargaron GIS; el inicio de sesión con Google completó correctamente.
3. Se comprobó el comportamiento de `g_state` durante la carga/inicialización de GIS en el flujo de autenticación. Se mantienen registrados los atributos observados y la discrepancia con la documentación de Google; la clasificación jurídica no se deduce de la prueba técnica.
4. Los estados `completed_meals_<clientId>_...` y `shopping_<clientId>` se eliminaron al cerrar sesión del paciente.
5. Web Push no registró `/sw.js` antes de la acción de activación; tras conceder permiso y activar los avisos, el registro y la suscripción real funcionaron.
6. Google Calendar, Stripe Checkout alojado y LiveKit bajo demanda se comprobaron en sus flujos correspondientes, sin finalizar una compra real ni usar una consulta clínica real.

## 7. Pendiente jurídico

Antes de cerrar definitivamente el punto de cookies, debe evaluarse la clasificación de `g_state`: Google documenta la cookie como estado de cierre de sesión relacionada con One Tap/inicio de sesión automático, mientras que DietoExpress utiliza el botón de acceso normal y no invoca esos flujos de manera explícita. No se ha determinado todavía si su uso efectivo entra en la excepción de estricta necesidad del artículo 22.2 LSSI. La decisión sobre el banner queda condicionada a esa clasificación y a la validación jurídica final.

Esta comprobación es una validación de despliegue, no una razón para inventar cookies en la política.

## 7. Criterio de seguridad

Las cookies de sesión no son el mecanismo de consentimiento comercial. La baja comercial se gestiona mediante las preferencias específicas de comunicaciones y su enlace de baja seguro.
