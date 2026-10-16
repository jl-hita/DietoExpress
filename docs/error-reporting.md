# Errores normalizados e informe a soporte

## Auditoría inicial (fase 1)

- La API ya dispone de soporte interno en `/api/support` y permite crear tickets mediante `POST /api/support/tickets`; no hace falta crear otro sistema de tickets ni un proveedor externo.
- El contrato existente admite asunto, cuerpo, categoría y prioridad. La incidencia técnica se adjunta como texto depurado en el primer mensaje del ticket, sin cambios de esquema.
- Angular ya tiene un interceptor HTTP de observación (`zoneChangeDetectionInterceptor`), pero su función actual es diagnóstica y no normaliza errores ni los enlaza con tickets.
- El backend usa Serilog y middleware de seguridad en `Program.cs`; hasta esta fase no había un middleware común visible en el código auditado que convirtiera excepciones no controladas en un contrato seguro con correlación.

## Contrato común (fase 2)

Las excepciones no controladas se devuelven como `application/problem+json` con un mensaje seguro, `code=INTERNAL_ERROR`, `correlationId` y cabecera `X-Correlation-ID`. El detalle de excepción solo se registra en logs protegidos; no se devuelve al navegador.

Angular captura errores HTTP 5xx de rutas propias de la API, sin cuerpos de petición/respuesta ni query strings. Se limita la captura a una incidencia visible y se deduplican repeticiones del mismo estado/ruta durante 30 segundos. Las validaciones, errores 4xx y fallos de las propias rutas de soporte no generan este aviso global.

## Componente reutilizable (fase 3)

El componente global `app-error-report-banner` muestra una notificación con identificador, estado HTTP y acción para crear un ticket. Reutiliza el endpoint de soporte existente, con categoría `problem`, prioridad alta para errores 5xx y un cuerpo que contiene solo metadatos técnicos permitidos. No adjunta tokens, parámetros de consulta, datos de formularios ni respuestas del servidor.

## Alcance y siguiente paso

Esto prepara la infraestructura común, no migra todos los mensajes funcionales de todos los módulos. En la siguiente fase se podrán asignar códigos estables a errores de dominio concretos y ofrecer el mismo componente desde sus pantallas. La validación de compilación y pruebas se delega al CI de la rama.
