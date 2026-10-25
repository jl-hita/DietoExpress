# Idempotencia y transactional outbox

## Contrato HTTP

Los endpoints anotados con `[Idempotent]` aceptan opcionalmente `Idempotency-Key`. El cliente debe generar una clave por operación lógica y conservarla en todos sus reintentos. No debe generar una clave nueva porque haya expirado el timeout HTTP.

- Misma clave, mismo usuario/tenant, ruta, método, tipo de contenido y cuerpo: el backend reproduce la respuesta satisfactoria almacenada.
- Misma clave con otra petición: `409 Conflict`.
- Sin cabecera: se mantiene la compatibilidad con clientes anteriores, pero no se activa la reproducción de respuesta.
- Las claves se conservan en PostgreSQL; no hay caducidad automática.
- La respuesta reproducida puede incluir `Idempotency-Replayed: true`.

La cabecera no cambia rutas, modelos JSON ni respuestas funcionales existentes.

## Outbox transaccional

`integration_outbox` se crea de forma idempotente durante el bootstrap. El mensaje/check-in y su fila outbox se confirman dentro de la misma transacción PostgreSQL. Un worker reclama filas mediante `FOR UPDATE SKIP LOCKED`, reintenta errores transitorios con backoff y marca los fallos definitivos tras diez intentos.

Eventos procesados por este worker:

- `patient_message_created`: crea la notificación in-app/push cuando escribe el profesional, o envía el aviso por email cuando escribe el paciente.
- `patient_checkin_submitted`: publica el evento de automatización semanal.

Las notificaciones usan claves persistentes. El email conserva un Message-ID estable por efecto, pero SMTP no garantiza entrega exactamente una vez: si el proceso cae después de que el servidor SMTP acepte el mensaje y antes de confirmar la fila outbox, puede producirse un reenvío.

Los errores definitivos quedan visibles en `integration_outbox.status='failed'` y en `last_error`; no se descartan silenciosamente.

## Impacto Android

No cambia ninguna ruta ni cuerpo JSON. Android puede adoptar la cabecera en las escrituras de mensajes y check-ins sin requerir un cambio de versión de API. Para que sea segura frente a reintentos, la clave debe generarse al crear la operación local, guardarse junto a ella en Room y reutilizarse en todos los intentos.

La cola Android no debe reintentar automáticamente el envío de mensajes hasta comprobar que la operación encolada contiene una clave durable y que la interfaz distingue borrador local de mensaje confirmado por el servidor. La operación de marcar mensajes como leídos ya usa la clave persistida de la operación pendiente.

## Cobertura auditada

### Protegidas por idempotencia en backend

Las operaciones de biometría (crear, actualizar, borrar e importar confirmado); envío de revisión del directorio; recetas (crear, actualizar, sustitución y borrar); planes y características de planes; alimentos personalizados (crear, actualizar, borrar y favoritos); actualización de perfil; resolución de alertas administrativas; marcar mensajes del paciente como leídos; envío de mensajes del paciente y del profesional; envío del check-in semanal.

La protección solo se activa en endpoints anotados con `[Idempotent]` y cuando el cliente proporciona la cabecera.

### Efectos que siguen requiriendo tratamiento específico

Pagos, suscripciones, calendario, LiveKit, emails transaccionales y otros proveedores externos no deben marcarse en bloque como idempotentes. Cada flujo debe reutilizar la clave idempotente del proveedor cuando exista y coordinar los cambios de base de datos con la outbox. La outbox de este PR cubre mensajes y check-ins, no convierte automáticamente todos los efectos externos del producto en exactly-once.

## Pruebas pendientes en entorno integrado

Las pruebas estáticas protegen el contrato y el cableado del worker. Para demostrar la garantía de extremo a extremo deben ejecutarse pruebas con PostgreSQL real que simulen reintentos concurrentes, pérdida de respuesta tras commit, reinicio del worker y caída entre aceptación SMTP y confirmación de outbox. La entrega SMTP exactamente una vez no se puede garantizar únicamente desde la aplicación.
