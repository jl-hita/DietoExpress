# Auditoría del portal del paciente

**Ámbito:** navegación y estados generales, acceso mediante enlace mágico, citas/videollamadas, consentimiento comercial, documentos, notificaciones y recorridos de seguimiento.

## Hallazgos priorizados

| Prioridad | Hallazgo | Tratamiento |
| --- | --- | --- |
| P0 | La API de preferencias comerciales comparaba el identificador del paciente con `clients.user_id`, que pertenece al profesional asociado. La lectura/actualización podía devolver 404 y dejar los controles deshabilitados. | Corregido: se valida el claim firmado `clientId` del rol paciente y se limita la operación a esa ficha activa. |
| P0 | La respuesta de preferencias confundía una preferencia inexistente con un rechazo porque ambos se devolvían como `emailEnabled=false`. | Corregido: la API devuelve también `hasDecision`; solo se considera decidida una preferencia persistida explícitamente. |
| P0 | El bloque de comunicaciones comerciales aparecía como un panel permanente y no ofrecía una elección clara de aceptar/rechazar; un fallo de carga podía dejar los controles deshabilitados sin salida visible. | Corregido: botones explícitos, mensaje accesible, reintento de carga, confirmación persistente y panel compacto de estado en inicio. El paciente puede cambiar la decisión después. |
| P0 | El acceso a videollamada necesitaba una ruta de error recuperable ante fallos de autorización y una regresión automatizada para reintentar. | Cubierto con prueba Playwright de error de autorización y reintento. La autorización real sigue comprobando cita, paciente, modalidad y ventana de acceso en servidor. |
| P1 | Los eventos de entrada a la sala generaban claves con marca temporal, por lo que una reconexión podía volver a disparar avisos. La rama de recordatorio podía asignar un mensaje al participante equivocado. | Corregido: clave estable por cita y rol; aviso inmediato a la contraparte y recordatorio de espera dirigido al participante correcto. Se conservan canales configurables mediante las reglas de automatización. |
| P1 | La prueba de enlace mágico comprobaba la retirada del token de la URL, pero no cubría la recuperación de preferencias ni el ciclo de aceptación/rechazo. | Añadidas pruebas Playwright para fallo temporal, reintento, aceptación, retirada y persistencia de la elección. |

## Decisiones de experiencia

- El consentimiento comercial es opcional y está separado de los mensajes y avisos asistenciales necesarios para prestar el servicio.
- La pantalla no debe bloquear el acceso a citas, documentos o seguimiento si falla una carga secundaria.
- Los errores de acceso a una videollamada deben mostrar el motivo devuelto por el servidor y permitir un reintento.
- La consulta guiada profesional ya admite una videollamada embebida y un control para mostrarla/ocultarla; la validación visual en sesión real sigue siendo necesaria.
- Los enlaces mágicos permanecen reutilizables hasta su caducidad, no se consumen al autenticarse y el token se retira de la URL después del inicio de sesión.

## Validación pendiente fuera de CI

- Ejecutar un recorrido real paciente/profesional con una cita online confirmada y credenciales activas de LiveKit, incluyendo cámara/micrófono denegados, reconexión y salida/reentrada.
- Verificar la entrega real de push/email en un entorno configurado; el registro de un job o la aceptación SMTP no garantizan recepción en bandeja.
- Repasar visualmente todas las pantallas del portal en dispositivos móviles y escritorio con datos reales de prueba. Playwright con APIs simuladas verifica los estados y flujos, pero no sustituye esta prueba manual.

No se han fusionado cambios ni desplegado a producción.
