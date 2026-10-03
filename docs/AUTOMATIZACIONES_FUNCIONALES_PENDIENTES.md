# Automatizaciones funcionales pendientes

Este documento recoge el backlog funcional de automatizaciones identificado sobre el motor persistente existente. No incluye la infraestructura ya implementada (eventos, cola persistente, worker, idempotencia, reintentos, historial y aislamiento por tenant).

## Prioridad inmediata

1. Flujo completo de nuevo paciente — backend de onboarding implementado; pendiente integración visual del formulario en portal
   - formulario inicial
   - consentimiento
   - datos/biometría pendientes
   - recordatorios
   - detección de ficha completa
   - paso a primera cita

2. Completar información inicial — recordatorios y escalado implementados
   - recordar al paciente
   - escalado al profesional
   - detener recordatorios al completar

3. Primera cita — detección, recordatorio y cancelación al reservar implementados
   - detectar ficha completa sin primera cita
   - ofrecer/recordar reserva
   - detener recordatorios al reservar

4. Flujo post-cita — check-in posterior, tarea de revisión y planificación de próxima cita implementados
   - seguimiento posterior
   - próxima cita
   - check-in cuando corresponda
   - actualización de dieta cuando corresponda

5. Flujo post-check-in — revisión, aviso al paciente y tarea de siguiente acción implementados
   - revisión profesional
   - feedback/acción posterior
   - nueva cita o modificación de dieta cuando corresponda

6. Escalado de check-in atrasado — secuencia 10/14/21 días implementada
   - secuencia de recordatorios
   - escalado progresivo
   - integración con lifecycle

7. Recuperación de pacientes sin seguimiento — aviso al paciente y escalado profesional implementados
   - recordatorio al paciente
   - tarea al profesional
   - escalado
   - reactivación

8. Secuencia de caducidad de dieta — avisos 7/3/1/0 y cancelación al publicar nueva dieta implementados
   - avisos previos
   - aviso de caducidad
   - comprobar existencia de nueva dieta
   - detener avisos al publicar una nueva

9. Recuperación de dieta caducada — aviso al paciente y tarea profesional implementados
   - tarea profesional
   - aviso al paciente cuando proceda
   - seguimiento hasta resolver

## Estado de implementación actual

### Bloque implementado en esta iteración

- Onboarding persistente: fecha de nacimiento, género, biometría mínima y consentimiento versionado.
- Endpoint de portal para consultar/guardar el onboarding.
- Sweep horario de onboarding con recordatorios al paciente y escalado al profesional.
- Detección de ficha completa y guía automática hacia la primera cita.
- Cancelación de recordatorios de primera cita cuando existe una reserva futura.
- Cancelación de recordatorios de onboarding cuando se completa el flujo.

## Implementado durante la fase actual

- Configuración persistente por tenant para reglas de automatización mediante `automation_rules`.
- Activación/desactivación y retardo configurable para `client.created`, `patient.checkin.submitted`, `appointment.completed` y `appointment.no_show`.
- Los recordatorios de cita de 24 h y 2 h tienen configuración independiente (`appointment.reminder.24h` y `appointment.reminder.2h`), incluido el tiempo de antelación.
- Onboarding, seguimiento y caducidad de dietas ya exponen reglas separadas para activar/desactivar recordatorios y escalados: `onboarding.info.reminder`, `onboarding.info.escalation`, `onboarding.first_appointment.reminder`, `onboarding.first_appointment.escalation`, `followup.checkin.reminder`, `followup.checkin.escalation`, `diet.expiry.reminder` y `diet.expired`.
- El endpoint de configuración devuelve también las reglas soportadas que todavía no tienen fila persistida, usando sus valores por defecto; así la interfaz puede mostrar el catálogo completo desde el primer acceso.
- `recipient_scope` y `channels` ya se aplican al generar los jobs: `assigned_professional`, `clinic_admin`, `patient` y `both` controlan el destinatario; `in_app`, `email` y `push` controlan el canal. El push de paciente mantiene también la notificación persistida in-app, que es el canal durable.
- Plantillas por tenant mediante `automation_templates`: permiten personalizar títulos y mensajes de paciente/profesional y asunto/HTML de email. Los tokens disponibles inicialmente son `{title}`, `{message}` y `{action_url}`; si no existe plantilla se conserva el contenido actual.
- Preferencias de comunicación por paciente mediante `patient_communication_preferences`: `in_app`, `email` y `push` son independientes, con valores por defecto activados. El worker y `NotificationService` las vuelven a comprobar en ejecución para respetar cambios realizados después de programar un job.
- API profesional `GET /api/professional/automation/rules` y `PUT /api/professional/automation/rules/{ruleKey}`.
- Sin configuración explícita se mantienen los comportamientos actuales por defecto, evitando cambios funcionales al actualizar instalaciones existentes.

## Segunda fase: sistema configurable

10. Reglas configurables por nutricionista
11. Plantillas configurables con variables
12. Preferencias de comunicación por paciente/profesional

## Tercera fase

13. Reactivación automática de pacientes inactivos
14. Automatizaciones avanzadas de citas y dietas
15. Recordatorios de revisiones antropométricas
16. Recordatorios de objetivos y mediciones
17. Campañas de recuperación de pacientes
18. Automatizaciones basadas en evolución del paciente
19. Análisis de check-ins y sugerencias asistidas por IA

## Ya implementado y no pendiente

- Alta de evento client.created y tarea inicial.
- Check-in enviado y tarea de revisión.
- Revisión/cierre transaccional del check-in.
- Recordatorios de cita a 24 h y 2 h.
- Cancelación de recordatorios de cita.
- Cita completada y tarea de seguimiento.
- No-show y tarea profesional.
- Seguimiento semanal básico (>7 días y >10 días).
- Lifecycle de pacientes.
- Aviso de dieta próxima a caducar.
- Tarea de dieta expirada.
- Notificación de dieta publicada/modificada.
- Eventos y acciones de Stripe/billing.
