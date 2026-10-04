# Gate de mínimo operativo legal — DietoExpress

> Documento interno de lanzamiento. Define el mínimo técnico/documental que debe estar cerrado antes de presentar DietoExpress como plataforma preparada para operar con clientes reales en España.
>
> **No es certificación de cumplimiento ni asesoramiento jurídico.** Los elementos marcados como jurídicos requieren validación con los tratamientos, titularidad, proveedores y actividad reales.

## Objetivo

Separar el cumplimiento ya implementado técnicamente de las decisiones y documentos que requieren datos reales o revisión jurídica.

## Gate crítico

| # | Bloque | Estado | Cierre requerido |
|---|---|---|---|
| 1 | Identidad del titular | 🔴 | Datos definitivos |
| 2 | Aviso legal | 🔴 | Texto definitivo + publicación |
| 3 | Privacidad DietoExpress | 🔴 | RAT + bases jurídicas + texto definitivo |
| 4 | Responsable/encargado | 🔴 | Modelo jurídico validado |
| 5 | DPA | 🔴 | Contrato definitivo + anexos |
| 6 | Subencargados/transferencias | 🔴 | Inventario real + garantías |
| 7 | Conservación/eliminación | 🔴 | Matriz de plazos validada |
| 8 | Riesgos/EIPD | 🟡 | Evaluación final + decisión EIPD |
| 9 | Documentación del paciente | 🔴 | Catálogo mínimo definitivo |
| 10 | Contratación/pagos | 🔴 | Información precontractual y evidencias |
| 11 | Derechos y brechas | 🟢 | Infraestructura operativa; completar datos reales |
| 12 | Seguridad/evidencias | 🟡 | Matriz final + backups/restauración |

## Cierre

El gate será 🟢 cuando no queden placeholders publicados, el RAT y bases jurídicas estén validados, el DPA sea contractual, el inventario de proveedores sea real, las transferencias estén evaluadas, exista matriz de conservación validada, riesgos/EIPD estén documentados, el catálogo de paciente esté validado, contratación/pagos estén cerrados y exista revisión jurídica final de los documentos críticos.

## Proveedores detectados

- Stripe — pagos/suscripciones.
- Google — OAuth/Google Calendar.
- Proveedor SMTP configurado externamente — correo.
- Infraestructura de alojamiento/PostgreSQL — servicio real a identificar.
- Backups — infraestructura real a identificar.

Para cada proveedor hay que confirmar entidad, servicio, datos, ubicación, subcontratación, transferencias, mecanismo jurídico, DPA, medidas y fecha de revisión.

## Conservación

No debe existir un TTL global. La matriz definitiva será: tratamiento → finalidad → evento inicial → plazo → bloqueo → supresión/anonimización → excepción.

Debe distinguir cuentas, facturación, expediente asistencial, aceptaciones legales, logs, backups, soporte y marketing. La conservación de documentación asistencial debe validarse según profesional/centro y territorio.

## Evidencias técnicas ya disponibles

- legal_documents y legal_acceptances.
- Versionado y hash.
- Fecha/hora, IP, user-agent y contexto.
- API pública de documentos y aceptación/historial.
- Pantalla pública /legal.
- Bloqueo de checkout por aceptación vigente.
- Exportación operativa.
- Solicitudes de derechos.
- Registro de brechas.
- Auditoría.
- Archivado y revocación de acceso.

## Fuera del gate

IA, marketplace, pagos de consultas, firma electrónica cualificada, analítica avanzada, VERI*FACTU y automatizaciones legales avanzadas no bloquean el mínimo operativo.

## Regla de producto

Toda nueva funcionalidad que trate datos personales debe pasar por el checklist legal-by-design antes de activarse en producción.

**Importante:** este documento es un gate interno de producto/compliance, no una certificación de cumplimiento.