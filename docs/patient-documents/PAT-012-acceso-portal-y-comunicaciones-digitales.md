# PAT-012 — Acceso al portal y comunicaciones digitales

> Plantilla funcional de DietoExpress. Requiere revisión jurídica y técnica antes de uso.

## 1. Finalidad

El portal de DietoExpress permite al paciente consultar documentación, recibir indicaciones relacionadas con el servicio y, cuando esté habilitado, completar procesos de aceptación o firma.

## 2. Cuenta y acceso

El acceso es personal. El paciente debe mantener bajo su control los mecanismos de acceso y comunicar al profesional cualquier sospecha de acceso no autorizado.

- Paciente: {{patient.full_name}}
- Identificador de acceso: {{patient.portal_identifier}}
- Fecha de alta del acceso: {{document.date}}

## 3. Uso del portal

El paciente se compromete a facilitar información veraz, mantener actualizados sus datos de contacto y utilizar el portal únicamente para las finalidades relacionadas con el servicio.

El portal no sustituye los canales de emergencia ni debe utilizarse para comunicar una urgencia sanitaria.

## 4. Comunicaciones

Las comunicaciones necesarias para prestar el servicio pueden incluir avisos de citas, respuestas del profesional, documentación pendiente e incidencias del servicio. Las comunicaciones comerciales se gestionan separadamente.

## 5. Incidencias de acceso

{{portal.access_instructions}}

## 6. Aceptación

- Paciente: {{patient.full_name}}
- Fecha y hora: {{document.accepted_at}}

> Este documento no convierte por sí mismo una comunicación en comercial ni modifica las bases jurídicas aplicables a cada tratamiento de datos.
