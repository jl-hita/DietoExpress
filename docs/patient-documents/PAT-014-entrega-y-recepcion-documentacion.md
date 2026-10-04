# PAT-014 — Constancia de entrega y recepción de documentación

> Plantilla funcional de DietoExpress. Requiere revisión profesional. No sustituye los requisitos de firma o consentimiento que correspondan al documento entregado.

## 1. Documento entregado

- Paciente: {{patient.full_name}}
- Documento: {{delivered_document.name}}
- Versión: {{delivered_document.version}}
- Fecha de entrega: {{delivered_document.delivered_at}}
- Medio: {{delivered_document.channel}}

## 2. Constancia

El paciente declara haber tenido acceso al documento identificado anteriormente y haber podido consultarlo.

La recepción o visualización de un documento **no equivale por sí misma a consentimiento o aceptación**, salvo que el propio documento y su base jurídica establezcan expresamente lo contrario.

## 3. Observaciones

{{delivered_document.notes}}

## 4. Trazabilidad

- Paciente: {{patient.full_name}}
- Fecha y hora de visualización/recepción: {{document.accepted_at}}
- Identificador de documento: {{delivered_document.identifier}}

> La aplicación debe conservar la evidencia técnica disponible de entrega, visualización y, cuando corresponda, aceptación o firma como eventos diferenciados.
