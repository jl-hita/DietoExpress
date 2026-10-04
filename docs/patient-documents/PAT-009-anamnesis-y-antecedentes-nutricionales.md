# PAT-009 — Anamnesis y antecedentes nutricionales

> Plantilla funcional de DietoExpress. Requiere revisión profesional y jurídica antes de uso asistencial.

## 1. Identificación

- Paciente: {{patient.full_name}}
- Fecha de alta: {{patient.registration_date}}
- Profesional responsable: {{professional.full_name}}
- Centro: {{tenant.name}}

## 2. Motivo de consulta

{{clinical.reason_for_consultation}}

## 3. Antecedentes relevantes

- Antecedentes médicos: {{clinical.medical_history}}
- Medicación y suplementos: {{clinical.medication_and_supplements}}
- Alergias e intolerancias conocidas: {{clinical.allergies}}
- Antecedentes familiares relevantes: {{clinical.family_history}}
- Intervenciones quirúrgicas relevantes: {{clinical.surgical_history}}

## 4. Hábitos y contexto

- Patrón alimentario habitual: {{clinical.dietary_pattern}}
- Actividad física: {{clinical.physical_activity}}
- Sueño y descanso: {{clinical.sleep}}
- Consumo de alcohol/tabaco u otros factores relevantes: {{clinical.lifestyle}}
- Contexto laboral/social que pueda afectar al plan: {{clinical.social_context}}

## 5. Objetivos acordados

{{clinical.goals}}

## 6. Observaciones profesionales

{{clinical.professional_observations}}

## 7. Confirmación del paciente

El paciente declara que la información facilitada para esta anamnesis es, según su conocimiento, completa y suficientemente actualizada, y se compromete a comunicar cambios relevantes.

**Fecha:** {{document.accepted_at}}

**Paciente:** {{patient.full_name}}

**Profesional:** {{professional.full_name}}

> Este documento forma parte de la historia/documentación asistencial de DietoExpress. No sustituye la valoración clínica que corresponda al profesional responsable.
