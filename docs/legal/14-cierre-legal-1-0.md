# Cierre legal 1.0 — estado técnico

## Cerrado técnicamente en este bloque

- Separación persistente entre preferencias asistenciales y comerciales.
- Opt-in comercial explícito por email.
- Baja comercial persistente.
- Token de baja opaco almacenado como hash.
- Endpoint público de baja sin autenticación.
- La baja comercial no desactiva las comunicaciones asistenciales.
- Inventario técnico inicial de cookies y tecnologías similares.
- Documentación de la separación asistencial/comercial.

## Sigue requiriendo decisión o revisión jurídica

- Titular, NIF/CIF, domicilio y datos de contacto reales.
- Textos jurídicos definitivos.
- Bases jurídicas definitivas por finalidad.
- Plazos de conservación y RAT definitivos.
- DPA y subencargados.
- Transferencias internacionales.
- Revisión contractual de LiveKit, Google, Stripe y proveedor SMTP.
- Clasificación jurídica definitiva de cookies/tecnologías y configuración del consentimiento.
- Condiciones económicas, cancelación y reembolso definitivas.
- Responsabilidades clínica ↔ nutricionista.
- Revisión jurídica externa y publicación definitiva.

Por tanto, este bloque **no certifica cumplimiento legal** ni permite marcar el Módulo 15 como cerrado por sí solo.

## Integración final de comunicaciones comerciales

La aplicación mantiene separadas las preferencias de comunicaciones asistenciales de las comerciales. Las comunicaciones asistenciales automatizadas continúan utilizando `patient_communication_preferences`; la preferencia comercial se almacena en `patient_commercial_communication_preferences`.

Las altas, bajas y revocaciones comerciales conservan evidencia de fecha, versión y origen en `patient_commercial_communication_consent_history`. Los envíos explícitamente comerciales deben programarse mediante la acción `commercial_email_patient`; el worker vuelve a comprobar el consentimiento inmediatamente antes del envío SMTP y añade automáticamente un enlace de baja.

La baja mediante enlace es anónima, utiliza un token opaco almacenado únicamente mediante hash y no desactiva comunicaciones asistenciales.

La existencia de este circuito técnico no sustituye la revisión jurídica de la base legal concreta, el texto de consentimiento ni las condiciones de las comunicaciones comerciales reales.