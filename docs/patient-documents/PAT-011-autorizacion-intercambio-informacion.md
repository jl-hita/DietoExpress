# PAT-011 — Autorización para intercambio de información asistencial

> Plantilla funcional de DietoExpress. No debe utilizarse como autorización general. Requiere revisión jurídica y profesional antes de uso.

## 1. Finalidad

El paciente puede autorizar, cuando resulte apropiado y legalmente procedente, el intercambio de información asistencial concreta con el profesional o entidad indicada a continuación para una finalidad determinada.

## 2. Destinatario autorizado

- Nombre / entidad: {{third_party.name}}
- Profesional / cargo: {{third_party.role}}
- Medio de contacto: {{third_party.contact}}

## 3. Información que puede comunicarse

{{third_party.authorized_information}}

## 4. Finalidad concreta

{{third_party.purpose}}

## 5. Duración / límite temporal

{{third_party.duration}}

## 6. Límites

La autorización se limita a la información, destinatario, finalidad y periodo indicados. No constituye una autorización general para divulgar información sanitaria a terceros.

El paciente podrá retirar la autorización cuando proceda. La retirada no afecta a los intercambios realizados legítimamente antes de que sea efectiva.

## 7. Aceptación

- Paciente: {{patient.full_name}}
- Fecha y hora: {{document.accepted_at}}
- Profesional: {{professional.full_name}}

> Antes de activar este documento en producción debe comprobarse la base jurídica aplicable y la forma adecuada de documentar la autorización o consentimiento según el caso.
