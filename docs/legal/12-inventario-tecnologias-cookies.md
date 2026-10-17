# Inventario técnico de cookies y tecnologías similares — DietoExpress

Este documento refleja el inventario que puede verificarse en el código actual de DietoExpress. No sustituye la clasificación jurídica final ni la inspección del entorno de producción.

## 1. Cookies propias detectadas

| Cookie | Proveedor | Finalidad | Duración | Propia/tercero | Necesaria |
|---|---|---|---|---|---|
| `dietoexpress_professional_session` | DietoExpress | Sesión autenticada del profesional, seguridad y autorización | 3 horas | Propia | Sí, para las áreas autenticadas |
| `dietoexpress_patient_session` | DietoExpress | Sesión autenticada del paciente, seguridad y autorización | 8 horas | Propia | Sí, para el portal autenticado |

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

La existencia de estas integraciones no implica por sí sola que DietoExpress coloque cookies no necesarias; las cookies y el almacenamiento que use cada proveedor deben verificarse en su flujo real.

## 5. Resultado técnico actual

Con el código auditado:

- Se han identificado **dos cookies propias de sesión**, ambas necesarias para las áreas autenticadas.
- La cookie `g_state` observada está asociada a Google Identity Services. Las pruebas aisladas indican que se crea al ejecutar `google.accounts.id.initialize(...)`, no solo al descargar el script ni al renderizar el botón. Su necesidad y régimen de consentimiento siguen pendientes; el dato técnico no basta por sí solo para concluir que haga falta un banner ni para descartarlo.
- No hay un SDK de analítica publicitaria/tercera cargado en el cliente.
- Las fuentes Outfit y los iconos Material se sirven localmente; Google Identity Services se carga de forma diferida en login y creación de cuenta, no en la página pública inicial.
- Se ha eliminado el uso de `sessionStorage` para deduplicar analítica pública, evitando convertir esa deduplicación en una tecnología de almacenamiento del navegador.
- El almacenamiento local restante es funcional para el portal de paciente: comidas completadas y casillas de la lista de la compra. Ambos se limpian al cerrar sesión.

En una prueba manual de producción en incógnito limpio, la landing no tenía `g_state`; la cookie apareció al abrir el login, antes de pulsar el botón de Google. La documentación oficial de Google identifica `g_state` como una cookie de Google Identity Services para conservar el estado de cierre de sesión y la asocia a One Tap o inicio de sesión automático. El código actual solo inicializa GIS y renderiza el botón; no se encontró una llamada explícita a `google.accounts.id.prompt()`, `disableAutoSelect()` ni configuración de inicio automático. El resultado acota el disparador a la carga/inicialización asociada al login, pero no identifica la operación exacta. La necesidad/consentimiento siguen pendientes.

**La decisión sobre la necesidad de un banner de consentimiento queda pendiente** de repetir la prueba tras desplegar estos cambios, revisar los flujos de Google/Stripe/LiveKit y completar la validación jurídica. No se debe concluir que el banner es innecesario únicamente porque no haya analítica publicitaria en el cliente.

## 6. Verificación pendiente de producción

Antes de marcar el punto legal como definitivamente cerrado hay que comprobar en el dominio de producción, con un navegador limpio:

1. Confirmar que la página pública ya no solicita `fonts.googleapis.com` ni `fonts.gstatic.com` y que las fuentes/iconos se renderizan correctamente.
2. Confirmar que `accounts.google.com/gsi/client` no se solicita en la página pública y sí al abrir las pantallas de login y crear cuenta; probar el inicio de sesión con Google.
3. Repetir el inventario de cookies antes y después de abrir login y crear cuenta en incógnito limpio; comparar carga del script, `initialize()` y renderizado del botón por separado; revisar atributos `Secure`, `HttpOnly`, `SameSite`, dominio, ruta y expiración.
4. Revisar localStorage/sessionStorage/IndexedDB y los flujos de Google Calendar, Stripe y LiveKit.
5. Completar la validación jurídica antes de decidir si hace falta banner y publicar una versión definitiva de la política.

Esta comprobación es una validación de despliegue, no una razón para inventar cookies en la política.

## 7. Criterio de seguridad

Las cookies de sesión no son el mecanismo de consentimiento comercial. La baja comercial se gestiona mediante las preferencias específicas de comunicaciones y su enlace de baja seguro.
