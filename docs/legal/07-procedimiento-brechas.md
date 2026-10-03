# Procedimiento de brechas de datos personales — DietoExpress

> Procedimiento técnico-operativo. No constituye asesoramiento jurídico ni determina por sí mismo cuándo debe notificarse una brecha.

## Objetivo

Detectar, contener, investigar y documentar incidentes que puedan afectar a datos personales, distinguiendo especialmente los tratamientos en los que DietoExpress actúa como encargado.

## Flujo

1. **Detección** — registrar el incidente y preservar evidencias.
2. **Contención** — impedir acceso, extracción o propagación adicional.
3. **Clasificación** — identificar sistemas, tenants, categorías de datos y afectados potenciales.
4. **Evaluación** — analizar probabilidad y gravedad del riesgo.
5. **Responsabilidad** — determinar si afecta a un tratamiento propio o por cuenta de un cliente.
6. **Comunicación interna** — escalar al responsable correspondiente.
7. **Decisión** — documentar las obligaciones de comunicación/notificación aplicables.
8. **Remediación** — corregir la causa y reducir recurrencia.
9. **Cierre** — registrar evidencias, decisiones y medidas adoptadas.

## Información mínima del registro

- identificador del incidente;
- fecha/hora de detección;
- fecha/hora estimada de inicio, si se conoce;
- sistemas afectados;
- tenant(s) afectados;
- categorías de datos;
- categorías de interesados;
- descripción técnica;
- medidas de contención;
- evaluación de riesgo;
- comunicaciones realizadas;
- decisiones y responsables;
- medidas correctivas;
- fecha de cierre.

## Regla importante

No registrar secretos, contraseñas, tokens ni el contenido clínico completo dentro del registro de incidentes. Deben conservarse referencias suficientes para localizar las evidencias originales de forma controlada.

## Estado

Actualmente existe infraestructura de logging/auditoría de seguridad y acceso clínico. Falta un registro de incidentes de primera clase y un flujo operativo asociado.
