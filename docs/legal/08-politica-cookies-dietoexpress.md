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

Estas cookies se configuran como `HttpOnly`, `Secure` y `SameSite=Strict`.

No se utilizan cookies propias de publicidad ni de analítica de terceros en el código cliente auditado.

## 3. Tecnologías similares

El portal utiliza almacenamiento local para recordar las comidas completadas por el paciente. Es una función del propio portal, asociada al dispositivo/navegador, y no se utiliza para publicidad.

La analítica pública del embudo se registra server-side y no utiliza cookies ni almacenamiento del navegador para su deduplicación.

## 4. Servicios y recursos de terceros

La aplicación puede utilizar recursos o integraciones de terceros, entre ellos Google Identity Services, Google Fonts, Google Material Icons, Google Calendar, Stripe y LiveKit.

Cada integración deberá revisarse según el flujo real y su configuración efectiva. Esta política no debe presentar una integración como una cookie si el servicio no la coloca.

## 5. Tecnologías no necesarias

En la versión de código auditada no se ha identificado una categoría activa de cookies de publicidad o analítica que requiera un banner de consentimiento específico.

Si se incorpora una tecnología no estrictamente necesaria, su carga deberá quedar condicionada al mecanismo de consentimiento que corresponda y deberá actualizarse este inventario y la política antes de su publicación.

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
