# PAT-010 — Registro de seguimiento y evolución

> Plantilla funcional de DietoExpress. Requiere revisión profesional antes de uso asistencial.

## 1. Identificación

- Paciente: {{patient.full_name}}
- Profesional: {{professional.full_name}}
- Fecha de seguimiento: {{document.date}}
- Tipo de contacto: {{consultation.type}}

## 2. Evolución desde el último seguimiento

{{clinical.follow_up_summary}}

## 3. Adherencia y dificultades

{{clinical.adherence}}

## 4. Cambios relevantes

- Datos antropométricos: {{biometrics.changes}}
- Hábitos: {{clinical.habit_changes}}
- Medicación/suplementación comunicada: {{clinical.medication_changes}}
- Incidencias o síntomas comunicados: {{clinical.reported_incidents}}

## 5. Valoración profesional

{{clinical.professional_assessment}}

## 6. Ajustes acordados

{{clinical.plan_adjustments}}

## 7. Próximo seguimiento

{{consultation.next_steps}}

## 8. Observaciones

{{clinical.notes}}

**Profesional responsable:** {{professional.full_name}}

**Fecha:** {{document.date}}

> Este registro debe conservarse como parte de la documentación asistencial y mantener la trazabilidad de sus modificaciones.
