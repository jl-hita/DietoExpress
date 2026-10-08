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

El cliente carga actualmente recursos de terceros que **no deben confundirse automáticamente con cookies**:

- Google Fonts (fuentes Inter/Outfit).
- Google Material Icons.
- Google Identity Services (`accounts.google.com/gsi/client`) para el acceso con Google.

Además existen integraciones funcionales con Google Calendar, Stripe y LiveKit que deben revisarse según el flujo concreto en el que se utilicen. Su existencia no implica por sí sola que DietoExpress coloque una cookie no necesaria.

## 5. Resultado técnico actual

Con el código auditado:

- Se han identificado **dos cookies propias de sesión**, ambas necesarias para las áreas autenticadas.
- No se ha identificado una cookie propia no necesaria.
- No hay un SDK de analítica publicitaria/tercera cargado en el cliente.
- Se ha eliminado el uso de `sessionStorage` para deduplicar analítica pública, evitando convertir esa deduplicación en una tecnología de almacenamiento del navegador.
- El almacenamiento local restante es funcional para el portal de paciente y se limpia al cerrar sesión.

Por tanto, **no se justifica actualmente un banner de consentimiento para una categoría de analítica/publicidad que no existe en el código auditado**. Si se incorpora una tecnología no estrictamente necesaria, deberá añadirse al inventario y bloquearse hasta disponer del mecanismo de consentimiento correspondiente.

## 6. Verificación pendiente de producción

Antes de marcar el punto legal como definitivamente cerrado hay que comprobar en el dominio de producción, con un navegador limpio:

1. Cookies antes y después del login.
2. Atributos `Secure`, `HttpOnly`, `SameSite`, dominio, ruta y expiración.
3. localStorage/sessionStorage/IndexedDB.
4. Peticiones y recursos de terceros.
5. Comportamiento de Google, Stripe y LiveKit en los flujos en los que se activan.

Esta comprobación es una validación de despliegue, no una razón para inventar cookies en la política.

## 7. Criterio de seguridad

Las cookies de sesión no son el mecanismo de consentimiento comercial. La baja comercial se gestiona mediante las preferencias específicas de comunicaciones y su enlace de baja seguro.
