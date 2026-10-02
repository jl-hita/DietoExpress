# Auditoría de seguridad y robustez

He realizado una auditoría progresiva de DietoExpress, centrada principalmente en seguridad, aislamiento multi-tenant, autorización, validación de entradas y operaciones sensibles.

## Cambios realizados

- Reforcé el aislamiento multi-tenant en clientes, dietas, recetas, alimentos, asignaciones y biometrías.
- Revisé y protegí endpoints frente a accesos IDOR mediante IDs de otros usuarios o tenants.
- Reforcé el acceso a alimentos locales para que solo sean utilizables por su creador dentro de su tenant, salvo administradores autorizados.
- Apliqué ese mismo control a generación automática de dietas, validación, PDFs, recetas y portal del paciente.
- Reforcé el acceso a biometrías comprobando siempre el cliente padre, tenant, permisos y estado no archivado.
- Bloqueé operaciones de biometrías sobre pacientes archivados y añadí límites para importaciones y valores numéricos.
- Limité recursos del parser de bioimpedancia para evitar entradas excesivamente grandes.
- Endurecí el portal del paciente: los tokens de acceso se almacenan hasheados y ya no se devuelve el hash almacenado.
- El consumo de magic links ahora compara contra el hash del token recibido.
- Reforcé la revocación de sesiones del portal mediante versión de token.
- Validé el passcode del paciente para que tenga exactamente 6 dígitos.
- Reforcé los permisos de administradores, nutricionistas y administradores de clínica, incluyendo las operaciones de activación, suspensión, reasignación y borrado.
- Reforcé la normalización y unicidad de emails y usernames, incluyendo comparación case-insensitive.
- El login permite usernames sin distinguir mayúsculas/minúsculas.
- Añadí restricciones y validaciones de tamaño en distintos DTOs y endpoints para reducir abusos de recursos.
- Añadí límites a consultas/listados potencialmente grandes.
- Reforcé la validación de URLs y configuración administrativa.
- Revisé CORS, JWT, sesiones, recuperación de contraseña, setup inicial y endpoints administrativos.
- Añadí protecciones y pruebas sobre operaciones concurrentes relacionadas con límites de licencia, creación de clientes/dietas y asignaciones.
- Reforcé la facturación con Stripe: idempotencia de Checkout, serialización por tenant, historial de suscripciones y control de eventos duplicados o antiguos.
- Reforcé la unicidad e identidad de alimentos externos y las operaciones concurrentes de OpenFoodFacts.
- Añadí protección a enlaces externos abiertos en nuevas pestañas (noopener noreferrer).
- Revisé el uso de credenciales y tokens en el frontend y eliminé el almacenamiento del token del portal del paciente en localStorage.
- Añadí una batería creciente de pruebas de regresión de seguridad para evitar que estas protecciones se pierdan en cambios futuros.

## Estado

La auditoría de seguridad queda cerrada en esta pasada. La pasada global final de endpoints no ha detectado un nuevo bypass de autorización o aislamiento multi-tenant que requiera cambios de código. Las superficies públicas revisadas (autenticación, setup inicial y webhook de Stripe) mantienen límites explícitos y su exposición es intencionada.

La revisión de dependencias también queda controlada por CI: el último despliegue pasó la auditoría .NET sin vulnerabilidades High/Critical y el npm audit de producción terminó con 0 vulnerabilidades. Los avisos restantes de npm install corresponden a dependencias del árbol de desarrollo o paquetes obsoletos/deprecados y quedan como deuda técnica, no como bloqueo de seguridad del despliegue.

Con esta revisión se da por cerrada la auditoría planificada. Se ha completado la pasada sistemática de aislamiento multi-tenant, autorización/IDOR, concurrencia y límites de licencia, integridad de BBDD, portal de pacientes, HTTP/infraestructura, dependencias y frontend, seguida de una pasada global de regresión. No queda un hallazgo de seguridad confirmado que requiera una nueva modificación de código para cerrar esta auditoría.

Quedan como deuda técnica explícita la actualización de dependencias de desarrollo/deprecadas y la migración futura del JWT profesional desde localStorage a una sesión basada en cookie HttpOnly, que requeriría un cambio arquitectónico y no se ha tratado como vulnerabilidad confirmada en esta auditoría.

## Revisión adicional — altas concurrentes de usuarios

- Se endureció la comprobación final de unicidad de nombres de usuario en el alta administrativa para que la revalidación dentro del bloqueo transaccional también sea *case-insensitive*.
- Esto mantiene coherente el comportamiento con el login y con las comprobaciones previas de unicidad, evitando que una variante de mayúsculas/minúsculas llegue a la fase de persistencia durante una carrera concurrente.


## Revisión adicional — límites de creación y edición de dietas

- Se limitaron los días, comidas, elementos por comida y número total de alimentos/intercambios que puede aceptar una dieta.
- También se limitaron las longitudes de nombres y notas y los valores de gramos/intercambios.
- Las mismas validaciones se aplican tanto al alta como a la edición para evitar que un payload grande pueda amplificarse en múltiples registros.
- Se añadió una prueba de regresión específica para estos límites.


## Revisión adicional — límites de payload de pacientes y entradas administrativas

- Se limitaron los campos de texto del expediente del paciente tanto al crear como al editar, incluyendo antecedentes, salud digestiva, preferencias y estilo de vida.
- Se añadieron comprobaciones defensivas para cuerpos nulos en las operaciones de alta de nutricionistas y asignación de pacientes.
- Estas validaciones evitan que entradas excepcionalmente grandes o incompletas lleguen innecesariamente a la lógica de persistencia.


## Revisión adicional — aislamiento de recetas entre profesionales

- Detecté que el listado y la lectura individual de recetas estaban limitados al tenant, pero no al nutricionista creador.
- Las recetas están modeladas como plantillas creadas por un profesional y no tienen un mecanismo explícito de compartición equivalente al de las dietas.
- Ajusté ambas lecturas para exigir tenant y usuario creador, evitando que un profesional pueda consultar por ID o listar recetas privadas de otro profesional de la misma clínica.
- Añadí una prueba de regresión para mantener este aislamiento.

## Revisión adicional — concurrencia en altas de nutricionistas

- Detecté que el alta de nutricionistas de una clínica usaba un bloqueo advisory basado en el tenant.
- El username y el email de las cuentas son identificadores globales de acceso, por lo que dos clínicas distintas podían ejecutar simultáneamente la generación/revalidación de esos identificadores.
- Cambié el alta para utilizar el mismo bloqueo global que los demás flujos de creación de cuentas y mantuve la revalidación de email dentro de la transacción.
- Añadí una regresión para evitar que el alta vuelva a utilizar accidentalmente un lock limitado al tenant.

## Revisión final — límites de autorización y dependencias

- Repasé los controladores HTTP activos para confirmar que las superficies profesionales, administrativas y del portal mantienen políticas/roles explícitos.
- Se mantuvieron como excepciones intencionadas los endpoints de login/setup, la consulta pública del Client ID de Google y el webhook de Stripe, que valida su propia firma y no depende de JWT.
- Añadí una regresión que protege estas fronteras de autorización frente a futuras eliminaciones accidentales de los atributos.
- El último pipeline ejecutó correctamente Angular, restauración, auditoría de dependencias .NET, compilación, pruebas de seguridad y despliegue.
- La auditoría de dependencias .NET reportó únicamente avisos Low de NuGet.Packaging/NuGet.Protocol; el pipeline bloquea explícitamente High y Critical.
- La auditoría de producción de npm terminó en 0 vulnerabilidades con el nivel High como umbral. Los avisos visibles durante npm install son principalmente deprecaciones del árbol de dependencias y no provocaron un hallazgo de seguridad de producción.


## Trabajo posterior a la auditoría — migración del JWT profesional a cookie HttpOnly

- Se inicia como tarea independiente posterior al cierre de la auditoría.
- El objetivo es eliminar el JWT profesional de `localStorage` y transportarlo mediante una cookie `HttpOnly`, `Secure` y `SameSite=Strict`, siguiendo el patrón ya utilizado por el portal de pacientes.
- Se conserva la validación JWT, los claims, el aislamiento multi-tenant, la revocación mediante `token_version` y las políticas de autorización existentes.
- La selección de cookie se realiza por superficie para permitir que una sesión profesional y una sesión de paciente coexistan en el mismo navegador.
- Esta migración se mantiene separada de la auditoría cerrada para probar el cambio de autenticación de extremo a extremo sin reabrir el alcance de aquella revisión.


## Trabajo posterior a la auditoría — automatizaciones: ciclo de vida del paciente

- Añadí estados persistentes del ciclo de vida: información pendiente, primera cita pendiente, activo, seguimiento, sin seguimiento reciente y archivado.
- El motor actualiza estos estados a partir de altas, check-ins y eventos de citas.
- Añadí una revisión periódica que recalcula el estado y crea una tarea profesional cuando un paciente lleva más de 30 días sin seguimiento reciente.
- Las tareas generadas usan claves de idempotencia por paciente y día.


## Trabajo posterior a la auditoría — tareas automáticas del ciclo de vida

- El barrido persistente del ciclo de vida ahora genera tareas profesionales cuando un paciente entra en información pendiente, primera cita pendiente o ausencia de seguimiento reciente.
- Las tareas se crean solo al producirse una transición de estado y utilizan idempotencia por paciente, estado y día, evitando duplicados durante ejecuciones repetidas del worker.
- La primera cita pendiente se mantiene como estado hasta que exista una cita completada; si ya hay una cita futura solicitada o confirmada no se genera una tarea innecesaria para proponer otra.


## Automatizaciones de seguimiento y check-in

- Añadido un barrido persistente horario para pacientes activos/en seguimiento que no han enviado el check-in semanal.
- Se genera un recordatorio al paciente una vez por semana mediante una tarea persistente e idempotente.
- Si el paciente supera 10 días sin check-in, se genera además una tarea profesional de revisión/seguimiento.
- Las automatizaciones respetan tenant, pacientes archivados y el scheduler persistente con reintentos y recuperación tras reinicio.

## Automatizaciones de seguimiento — revisión profesional de check-ins

- Añadí estado persistente de revisión (reviewed_at y reviewed_by_user_id) a los check-ins.
- Cada check-in enviado genera una tarea profesional idempotente de revisión, y la revisión completa automáticamente las tareas pendientes asociadas al seguimiento.
- Añadí endpoints profesionales para consultar check-ins pendientes y marcarlos como revisados.
- El acceso se limita al tenant y a la asignación activa del paciente al profesional autenticado.
- La migración es idempotente y también crea la tabla base de check-ins si todavía no existe, para soportar instalaciones nuevas desde cero.

## Automatizaciones de dietas

- La publicación de una dieta asignada a un paciente genera un evento persistente y una notificación en el portal.
- Los cambios de una dieta activa generan también una notificación al paciente.
- Añadí un barrido horario persistente para dietas activas próximas a finalizar: avisa al paciente y crea una tarea profesional cuando la dieta ya ha vencido.
- Las reglas usan claves de idempotencia y no generan dietas automáticamente: la decisión clínica sigue siendo del profesional.
