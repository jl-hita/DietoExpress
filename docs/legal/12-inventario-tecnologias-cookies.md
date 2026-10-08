# Inventario técnico de cookies y tecnologías similares — DietoExpress

Este documento es un **inventario técnico**, no una declaración jurídica definitiva. La clasificación de cada tecnología, su base jurídica y la necesidad de consentimiento deben validarse antes de publicar la política definitiva.

## Tecnologías identificadas en el código actual

| Tecnología | Finalidad técnica observada | Tipo inicial | Observación |
|---|---|---|---|
| dietoexpress_professional_session | Mantener la sesión autenticada del profesional | Necesaria | Cookie de sesión/seguridad. |
| dietoexpress_patient_session | Mantener la sesión autenticada del paciente | Necesaria | Cookie de sesión/seguridad. |
| localStorage de comidas completadas | Recordar localmente comidas completadas en el portal | Funcional | Se ha localizado la clave completed_meals_<clientId>_<date>. Debe revisarse su necesidad y alcance. |
| Google Calendar OAuth | Integración solicitada por el profesional | Integración opcional | Revisar información intercambiada y documentación contractual/privacidad. |
| LiveKit | Consulta online/videollamada | Integración opcional | Mantener revisión contractual, DPA y transferencias pendiente del cierre 1.0. |
| Stripe | Suscripciones/pagos | Integración opcional | Revisar cookies/tecnologías que intervengan en las páginas o componentes de pago según la integración efectiva. |
| Proveedor SMTP | Envío de correo | Servicio de comunicaciones | Revisar proveedor real, ubicación y condiciones contractuales. |

## Regla de publicación

No se debe afirmar que una tecnología requiere o no requiere consentimiento únicamente por aparecer en este inventario. El análisis final debe considerar finalidad, configuración efectiva, terceros, duración, acceso a datos y normativa aplicable.

## Qué debe comprobarse antes de cerrar 1.0

- Inspección del navegador en producción para confirmar cookies reales, atributos, dominios, duración y SameSite/Secure.
- Revisión de localStorage, sessionStorage e IndexedDB.
- Inventario de scripts de terceros cargados en producción.
- Identificación del proveedor efectivo de analítica, si se habilita.
- Revisión de Google, Stripe, LiveKit y proveedor SMTP con sus configuraciones reales.
- Clasificación jurídica y base jurídica de cada tecnología.
- Mecanismo de consentimiento y revocación únicamente para las tecnologías que lo requieran.
- Actualización de la política de cookies publicada y de su versión registrada.

## Criterio de seguridad

Las cookies de sesión no deben utilizarse como mecanismo de consentimiento comercial. La baja comercial se gestiona mediante preferencias específicas y un enlace de baja independiente.