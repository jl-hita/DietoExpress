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
| localStorage | `completed_meals_<clientId>_<date>` | Recordar comidas completadas en el portal | Navegador/dispositivo | Funcional |
| sessionStorage | — | Ya no se utiliza para analítica pública | — | — |

El estado de comidas es funcional y no contiene una cookie. Al cerrar sesión del paciente se eliminan las claves locales de comidas asociadas a ese paciente para evitar que queden datos funcionales en un dispositivo compartido.

## 3. Analítica pública

La analítica del embudo público se realiza mediante peticiones al backend (`/api/public-funnel/events`) y almacenamiento server-side. El cliente no utiliza cookies ni almacenamiento del navegador para deduplicar esos eventos.

No se han localizado en el cliente referencias a Google Analytics, Google Tag Manager, gtag, Meta Pixel, Hotjar, Matomo o Plausible.

## 4. Recursos de terceros que deben figurar en la revisión de privacidad

El inventario distingue los recursos servidos localmente de las integraciones de terceros; ninguno debe confundirse automáticamente con una cookie:

- **Outfit** y **Material Icons** se sirven desde activos estáticos del propio sitio; no se espera una conexión del navegador a Google Fonts ni a `fonts.gstatic.com` para estos recursos.
- Google Identity Services (`accounts.google.com/gsi/client`) se carga de forma diferida al entrar en la pantalla de inicio de sesión, no desde la página pública inicial.

Además existen integraciones funcionales con Google Calendar, Stripe y LiveKit que deben revisarse según el flujo concreto en el que se utilicen. Su existencia no implica por sí sola que DietoExpress coloque una cookie no necesaria.

## 5. Resultado técnico actual

Con el código auditado:

- Se han identificado **dos cookies propias de sesión**, ambas necesarias para las áreas autenticadas.
- No se ha identificado una cookie propia no necesaria.
- No hay un SDK de analítica publicitaria/tercera cargado en el cliente.
- Las fuentes Outfit y los iconos Material se sirven localmente; Google Identity Services se carga solo en el flujo de acceso.
- Se ha eliminado el uso de `sessionStorage` para deduplicar analítica pública, evitando convertir esa deduplicación en una tecnología de almacenamiento del navegador.
- El almacenamiento local restante es funcional para el portal de paciente y se limpia al cerrar sesión.

En la prueba manual de producción realizada antes de estos cambios se observaron cookies con dominio de Google en una sesión de incógnito antes de iniciar sesión, al mismo tiempo que la página cargaba Google Identity Services y recursos de fuentes. La observación no permite atribuir esas cookies a una solicitud concreta ni determinar por sí sola si la integración las originó.

**La decisión sobre la necesidad de un banner de consentimiento queda pendiente** de repetir la prueba tras desplegar estos cambios, revisar los flujos de Google/Stripe/LiveKit y completar la validación jurídica. No se debe concluir que el banner es innecesario únicamente porque no haya analítica publicitaria en el cliente.

## 6. Verificación pendiente de producción

Antes de marcar el punto legal como definitivamente cerrado hay que comprobar en el dominio de producción, con un navegador limpio:

1. Confirmar que la página pública ya no solicita `fonts.googleapis.com` ni `fonts.gstatic.com` y que las fuentes/iconos se renderizan correctamente.
2. Confirmar que `accounts.google.com/gsi/client` no se solicita en la página pública y sí al abrir el login; probar el inicio de sesión con Google.
3. Repetir el inventario de cookies antes y después de login en incógnito limpio y revisar atributos `Secure`, `HttpOnly`, `SameSite`, dominio, ruta y expiración.
4. Revisar localStorage/sessionStorage/IndexedDB y los flujos de Google Calendar, Stripe y LiveKit.
5. Completar la validación jurídica antes de decidir si hace falta banner y publicar una versión definitiva de la política.

Esta comprobación es una validación de despliegue, no una razón para inventar cookies en la política.

## 7. Criterio de seguridad

Las cookies de sesión no son el mecanismo de consentimiento comercial. La baja comercial se gestiona mediante las preferencias específicas de comunicaciones y su enlace de baja seguro.
