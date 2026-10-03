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
