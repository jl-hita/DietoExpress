# Errores normalizados e informe a soporte

## Auditoría inicial (fase 1)

- La API ya dispone de soporte interno en `/api/support` y permite crear tickets mediante `POST /api/support/tickets`; no hace falta crear otro sistema de tickets ni un proveedor externo.
- El contrato existente admite asunto, cuerpo, categoría y prioridad. La incidencia técnica se adjunta como texto depurado en el primer mensaje del ticket, sin cambios de esquema.
- Angular utiliza el interceptor `zoneChangeDetectionInterceptor` para observar peticiones HTTP y enlazar los errores reportables con el sistema de tickets.
- El backend registra las excepciones con Serilog y conserva los detalles sensibles exclusivamente en logs protegidos.

## Contrato común (fase 2)

Las excepciones no controladas se devuelven como `application/problem+json` con un mensaje seguro, `code=INTERNAL_ERROR`, `correlationId` y cabecera `X-Correlation-ID`. Un filtro global normaliza también las respuestas HTTP 5xx explícitas de los controladores, asignando códigos estables (`INTERNAL_ERROR` o `SERVICE_UNAVAILABLE`) y `reportable=true`. La cabecera de correlación se expone en CORS para que Angular pueda leerla en los orígenes permitidos.

Angular captura errores HTTP 5xx de rutas propias de la API, errores 4xx únicamente cuando la API los marca explícitamente con `reportable=true` y errores JavaScript no controlados. Para los errores de cliente solo guarda código genérico y ruta actual; nunca el mensaje original de la excepción. Las validaciones normales y los errores de las propias rutas de soporte no generan el aviso. No se incluyen cuerpos de petición/respuesta ni parámetros de consulta.

## Componente reutilizable (fase 3)

El componente global `app-error-report-banner` muestra una notificación con identificador, estado HTTP y acción para crear un ticket. Reutiliza el endpoint de soporte existente, con categoría `problem`, prioridad alta para errores 5xx y prioridad normal para errores de dominio reportables. El ticket contiene metadatos técnicos depurados, nunca tokens, parámetros de consulta, datos de formularios ni respuestas del servidor. La acción de crear tickets solo se muestra a los roles que pueden crear tickets; SuperAdmin no ve una acción que la API rechazaría.

## Siguiente fase

La infraestructura ya permite que los controladores marquen errores de dominio concretos con códigos estables y `reportable=true`. La migración debe hacerse módulo a módulo, distinguiendo fallos que requieren soporte de validaciones esperadas y permisos, sin convertir indiscriminadamente todos los 4xx en tickets.


## Catálogo externo de alimentos

La consulta de productos por código de barras convierte fallos de transporte, timeout o respuesta JSON inválida de OpenFoodFacts en HTTP 502 con `code=FOOD_PROVIDER_ERROR` y `reportable=true`. El mensaje público es genérico: no incluye la respuesta del proveedor ni datos internos. Un código de barras válido sin producto sigue devolviendo 404 y no debe generar un ticket.


## Búsqueda de alimentos

La búsqueda combina el catálogo local con OpenFoodFacts y, cuando corresponde, USDA. Los errores de transporte, timeout o JSON inválido de proveedores externos se propagan al controlador y se convierten en HTTP 502 con `FOOD_PROVIDER_ERROR` y `reportable=true`; no se presentan como una búsqueda válida sin resultados. Los fallos no relacionados con proveedores conservan el manejo existente.


## Correo transaccional

Los fallos al enviar invitaciones de nutricionistas de clínica y enlaces de acceso al portal de pacientes devuelven HTTP 502 con `code=EMAIL_PROVIDER_ERROR` y `reportable=true`. La respuesta pública contiene un mensaje genérico y no expone el detalle del proveedor de correo; el detalle técnico queda en los registros internos.
