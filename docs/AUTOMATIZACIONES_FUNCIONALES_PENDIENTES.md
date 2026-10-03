# Automatizaciones funcionales pendientes

Este documento recoge el backlog funcional de automatizaciones identificado sobre el motor persistente existente. No incluye la infraestructura ya implementada (eventos, cola persistente, worker, idempotencia, reintentos, historial y aislamiento por tenant).

## Prioridad inmediata

1. Flujo completo de nuevo paciente
   - formulario inicial
   - consentimiento
   - datos/biometría pendientes
   - recordatorios
   - detección de ficha completa
   - paso a primera cita

2. Completar información inicial
   - recordar al paciente
   - escalado al profesional
   - detener recordatorios al completar

3. Primera cita
   - detectar ficha completa sin primera cita
   - ofrecer/recordar reserva
   - detener recordatorios al reservar

4. Flujo post-cita
   - seguimiento posterior
   - próxima cita
   - check-in cuando corresponda
   - actualización de dieta cuando corresponda

5. Flujo post-check-in
   - revisión profesional
   - feedback/acción posterior
   - nueva cita o modificación de dieta cuando corresponda

6. Escalado de check-in atrasado
   - secuencia de recordatorios
   - escalado progresivo
   - integración con lifecycle

7. Recuperación de pacientes sin seguimiento
   - recordatorio al paciente
   - tarea al profesional
   - escalado
   - reactivación

8. Secuencia de caducidad de dieta
   - avisos previos
   - aviso de caducidad
   - comprobar existencia de nueva dieta
   - detener avisos al publicar una nueva

9. Recuperación de dieta caducada
   - tarea profesional
   - aviso al paciente cuando proceda
   - seguimiento hasta resolver

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
