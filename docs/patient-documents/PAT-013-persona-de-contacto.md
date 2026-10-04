# PAT-013 — Persona de contacto autorizada por el paciente

> Plantilla funcional de DietoExpress. Requiere revisión jurídica antes de uso.

## 1. Persona designada

El paciente puede designar una persona de contacto para determinadas comunicaciones relacionadas con el servicio, dentro del alcance indicado expresamente.

- Paciente: {{patient.full_name}}
- Persona de contacto: {{contact.name}}
- Relación: {{contact.relationship}}
- Teléfono: {{contact.phone}}
- Email: {{contact.email}}

## 2. Alcance de la autorización

La persona indicada podrá recibir únicamente las comunicaciones expresamente autorizadas:

{{contact.authorized_scope}}

## 3. Límites

Esta designación no autoriza automáticamente el acceso al historial completo del paciente, datos de salud, documentación privada ni a cualquier otra información no incluida en el alcance anterior.

Cuando sea necesario, se deberá utilizar una autorización específica para intercambio de información asistencial.

## 4. Retirada o modificación

El paciente podrá solicitar la modificación o retirada de esta designación cuando corresponda. La retirada no afecta a comunicaciones realizadas legítimamente antes de hacerse efectiva.

## 5. Aceptación

- Paciente: {{patient.full_name}}
- Persona designada: {{contact.name}}
- Fecha y hora: {{document.accepted_at}}
- Profesional: {{professional.full_name}}
