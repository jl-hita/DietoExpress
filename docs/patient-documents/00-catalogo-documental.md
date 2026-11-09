# Catálogo inicial de documentación del paciente

> Catálogo de documentos de paciente. Las variables se resuelven al emitir cada documento; sin configuración legal completa no se emiten.

## Objetivo

La documentación del paciente debe integrarse con el alta y con el inicio de cada consulta. Los documentos obligatorios no serán un apartado aislado: el sistema deberá provisionarlos automáticamente, informar de los pendientes y bloquear el paso correspondiente cuando proceda.

## Documentos iniciales

| Código | Documento | Alta | Antes de consulta | Aceptación | Observaciones |
|---|---|---:|---:|---:|---|
| PAT-001 | Información y consentimiento informado del servicio de nutrición | Sí | No | Sí | Documento principal del servicio |
| PAT-002 | Información sobre tratamiento de datos personales y datos de salud | Sí | No | Sí | Adaptar a responsable/base jurídica reales |
| PAT-003 | Comunicaciones del servicio | Sí | No | Sí | Informativo; lo comercial va solo en PAT-008 |
| PAT-004 | Consentimiento para teleconsulta | No | Si teleconsulta | Sí | Solo cuando se utilice |
| PAT-005 | Consentimiento para fotografías de seguimiento | No | Cuando proceda | Sí | Separado de la prestación |
| PAT-007 | Representación de menores o personas representadas | Cuando proceda | No | Sí | Solo cuando corresponda |
| PAT-008 | Consentimiento para comunicaciones comerciales | No | No | Sí | Opcional y separado; nunca preseleccionado por defecto |
| PAT-009 | Anamnesis y antecedentes nutricionales | No | No | No | Registro asistencial; no convertir automáticamente en consentimiento |
| PAT-010 | Registro de seguimiento y evolución | No | No | No | Registro asistencial por seguimiento/consulta |
| PAT-011 | Autorización para intercambio de información asistencial | Cuando proceda | No | Sí | Destinatario, finalidad y alcance concretos |
| PAT-012 | Acceso al portal y comunicaciones digitales | Cuando proceda | No | Sí | Acceso personal y comunicaciones del servicio |
| PAT-013 | Persona de contacto autorizada | Cuando proceda | No | Sí | Alcance limitado; no sustituye una autorización sanitaria general |

## Principios

1. Cada documento tiene versión propia.
2. Cambiar una versión que requiera nueva aceptación genera una nueva instancia para el paciente sin borrar la evidencia histórica.
3. La aceptación debe quedar vinculada a paciente, tenant, versión y documento concreto.
4. Las comunicaciones asistenciales necesarias no dependen del consentimiento comercial.
5. El profesional puede desactivar documentos opcionales.
6. Los documentos específicos de un profesional/centro pueden añadirse sin modificar el catálogo global.
7. Los documentos deben poder incorporarse al flujo automático de alta y consulta.
8. La aplicación debe impedir publicar plantillas con variables obligatorias sin resolver.

## Flujo objetivo

**Alta:** crear paciente → provisionar PAT-001, PAT-002 y PAT-003 → enviar aviso al portal → completar/aceptar → alta documental completa.

**Consulta:** iniciar cita → provisionar los documentos configurados para consulta → comprobar pendientes → solicitar aceptación → permitir continuar cuando los obligatorios estén completos.

**Histórico:** conservar la versión aceptada y su evidencia aunque posteriormente se publique una nueva versión.

**Revisión:** estos textos requieren validación jurídica y adaptación a cada profesional o clínica antes de su uso real.
