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

Quedan como deuda técnica explícita la actualización de dependencias de desarrollo/deprecadas y la migración futura del JWT profesional desde localStorage a una sesión basada en cookie HttpOnly. *(Actualización 2026-11-11: esa migración ya está hecha; ver la sección «Trabajo posterior» más abajo y la «Auditoría global de 2026-11-11».)*

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


## Automatizaciones de billing y Stripe

- Conecté los eventos de Stripe con el motor persistente de automatizaciones sin duplicar el estado de facturación: `subscriptions` y los webhooks de Stripe siguen siendo la fuente de verdad.
- Se generan eventos de automatización para activación de suscripción, pagos correctos/fallidos, cambios de plan, cancelaciones y problemas con el método de pago.
- Añadí persistencia de `trial_end` para poder automatizar el aviso de finalización de pruebas y generar eventos de prueba finalizada cuando corresponde.
- Los avisos de billing se envían al contacto profesional de la cuenta por el scheduler persistente y los problemas relevantes generan además una tarea profesional con prioridad alta.
- Las acciones utilizan la misma idempotencia, reintentos, recuperación tras reinicio y aislamiento por tenant que el resto del motor.
- Un fallo del sistema de automatizaciones no revierte un webhook de Stripe ya procesado: primero se confirma el estado de billing y después se registra el evento de automatización.


## Integración de Google Calendar

- Implementada conexión OAuth 2.0 por profesional con Google Calendar.
- Tokens de acceso y refresh token almacenados cifrados; no se exponen al frontend.
- Añadido estado de conexión, cuenta conectada, sincronización manual y desconexión desde la agenda.
- Añadida sincronización periódica persistente cada 5 minutos.
- Las citas de DietoExpress se reflejan en Google Calendar con identificador estable.
- Los cambios de fecha/hora realizados sobre esas citas en Google Calendar se sincronizan de vuelta a DietoExpress.
- Los eventos externos de Google Calendar se almacenan como bloqueos de disponibilidad y evitan nuevas reservas solapadas.
- Se utilizan estados OAuth de un solo uso, hash del state y caducidad para evitar reutilización del callback.
- La integración mantiene aislamiento por tenant y profesional.
- La configuración sensible de Google se realiza mediante configuración/variables de entorno del servidor.


---

## Auditoría global de 2026-11-11

Revisión transversal (backend, frontend, despliegue, tests y operación). Sustituye al estado «cerrada» anterior: la auditoría de aislamiento multi-tenant sigue vigente, pero esta pasada añade hallazgos de autenticación, operación y producto. Es una revisión por muestreo hecha internamente; **no sustituye a un pentest externo**, que se recomienda antes de la v1.0 comercial.

### Corregida en esta pasada

| Hallazgo | Estado | Detalle |
|---|---|---|
| Sin 2FA/MFA (prioridad alta) | **Corregido** | Verificación en dos pasos TOTP (RFC 6238) opcional para cualquier profesional, compatible con Google/Microsoft Authenticator, Authy o 1Password. Alta con QR + confirmación de un código; 10 códigos de recuperación de un solo uso (almacenados como hash SHA-256); anti-replay del código TOTP (paso de tiempo aceptado una sola vez, con compare-and-set atómico en BD); desactivación y regeneración de códigos exigen contraseña (salvo cuentas Google) y un código vigente; avisos por email al activar, desactivar o usar un código de recuperación; auditoría de las operaciones. |
| Cuentas de Google evitaban el 2FA | **Corregido** | El login con Google devuelve el mismo desafío 2FA si la cuenta lo tiene activo; no se emite ninguna cookie de sesión antes del segundo factor. |
| Sin bloqueo por cuenta en el login (alta) | **Corregido** | Bloqueo progresivo por cuenta además del límite por IP: a partir del 5.º fallo seguido bloquea 1, 2, 4, 8 y 15 min (máximo). Aplica a contraseña y a códigos 2FA. El contador se actualiza con una sola sentencia SQL atómica, de modo que peticiones paralelas no multiplican los intentos. Se limpia al iniciar sesión correctamente o al restablecer la contraseña (por email o por superadmin). |
| Enumeración de usuarios por tiempo de respuesta (media) | **Corregido** | Se ejecuta siempre una verificación BCrypt (real o contra un hash ficticio) y se devuelve un único mensaje genérico para usuario inexistente, contraseña incorrecta y cuenta bloqueada. |

### Login de pacientes (email o teléfono + PIN de 6 dígitos)

Revisado a petición del propietario. Era el punto más débil de autenticación: un PIN de 6 dígitos son solo 1.000.000 de combinaciones y el único freno era el límite de 10 intentos/min por IP, insuficiente frente a un atacante con varias IP y email conocido del paciente (datos de salud).

| Hallazgo | Estado | Detalle |
|---|---|---|
| Sin bloqueo por paciente (alta) | **Corregido** | Tras 5 fallos seguidos el acceso con PIN se bloquea 1, 2, 4, 8, 16, 32 y 60 min (máximo). A ese ritmo la fuerza bruta lleva años aunque se rote de IP. El contador es atómico (una sola sentencia SQL). Se limpia con un acceso correcto (PIN o enlace de email) o cuando el profesional asigna un PIN nuevo. |
| El bloqueo no deja al paciente fuera | **Diseño** | El enlace de acceso enviado por email (`request-access-link` y el enlace mágico) no pasa por el bloqueo, así que un atacante no puede dejar a un paciente sin acceso. |
| Enumeración por tiempo de respuesta (media) | **Corregido** | Email inexistente, email ambiguo y paciente sin PIN respondían sin ejecutar BCrypt (más rápido). Ahora siempre se verifica un hash (real o ficticio) y las tres respuestas, más el bloqueo y el PIN erróneo, comparten el mismo mensaje y código 401. |
| PIN triviales (media) | **Corregido** | Con 5 intentos libres, 123456/000000/121212… eran la vía realista de entrada. Al asignar el PIN se rechazan repeticiones, secuencias (también con vuelta), mitades repetidas, capicúas y pares repetidos (~0,5 % del espacio). La validación solo aplica al asignar; los PIN ya existentes siguen funcionando. |

Impacto en la app Android: el contrato no cambia (`POST /api/portal/auth`, 401 con cuerpo de texto). Solo cambia el texto del mensaje de error, que ahora menciona el bloqueo temporal y el enlace por email. La app ya trata el 401 como credenciales inválidas.

Pendiente del portal de pacientes (no corregido):
- Un mismo email en dos pacientes (p. ej. en dos clínicas) hace que el login con PIN falle para ambos (`candidates.Count != 1`). No es un agujero, pero es un fallo funcional que conviene resolver con un identificador de clínica o un selector.
- La sesión del paciente dura 30 días sin renovación deslizante ni vista de «dispositivos activos»; valorar acortarla en la web y mantener 30 días en la app.
- Notificar al profesional o al paciente por email cuando se produzcan bloqueos repetidos.
- Valorar sustituir el PIN por un código de un solo uso enviado por email en la web (la app puede conservar el PIN con sesión larga y biometría del dispositivo).

### Acceso biométrico de pacientes (app Android)

Funcionalidad nueva pedida por el propietario. Se ha diseñado como credencial ligada al dispositivo (estilo passkey) y no como «recordar el PIN»:

- La app crea en el Keystore un par EC P-256 cuya clave privada solo se puede usar tras huella o rostro (`BIOMETRIC_STRONG`) y no sale del móvil. El servidor guarda solo la clave pública.
- Para entrar, el servidor emite un desafío de 2 minutos firmado con HMAC; la app lo firma y el servidor verifica la firma. Cada desafío sirve una sola vez (el instante de emisión debe ser posterior al último usado por el dispositivo, comprobado con un UPDATE atómico).
- No hay ningún secreto reutilizable almacenado en el móvil ni en el servidor: un volcado de la BD no permite entrar.
- El alta exige una sesión de paciente vigente, está limitada a 5 dispositivos y avisa al paciente por email. Se revoca desde la app, y el servidor revoca todos los dispositivos cuando el profesional cambia el PIN del paciente.
- Si se añaden huellas nuevas en el móvil, la clave se invalida y la app pide entrar con el PIN y reactivarlo.
- El bloqueo por PIN no se aplica al acceso biométrico (prueba posesión del dispositivo, no del PIN), pero sí el límite de peticiones por IP.
- Cambio interno: todos los métodos de acceso del paciente emiten la sesión mediante `PatientSessionService`.
- Clave de derivación del desafío: `Security:BiometricKey` o, si no existe, `Jwt:Key`; rotarla solo invalida desafíos en vuelo (2 minutos).
- **No verificado:** el flujo completo con un móvil real y el servidor. Hay tests unitarios de la criptografía en ambos lados y compilación de la app, pero conviene probar alta, acceso, revocación y cambio de huellas en un dispositivo.

### Decisiones de diseño del 2FA

- El secreto TOTP se cifra con AES-GCM, ligado al id de usuario, con una clave derivada por HKDF de `Security:TwoFactorKey` (o de `Jwt:Key` si no existe). No depende del anillo de claves de DataProtection, que puede perderse al restaurar un servidor, y un volcado de la BD por sí solo no revela los secretos.
- **Operación:** si se rota `Jwt:Key` (o `Security:TwoFactorKey`), los secretos 2FA existentes dejan de ser legibles y los usuarios con 2FA tendrán que ser reseteados por soporte. Para una rotación planificada, fija primero `Security:TwoFactorKey` con el valor actual de `Jwt:Key`.
- El paso 1 del login con 2FA solo devuelve un desafío firmado (HMAC, caduca a los 5 minutos y queda ligado a `token_version`); la cookie de sesión solo se emite en `POST /api/auth/login/2fa`.
- **Pérdida del móvil y de los códigos:** un superadmin puede desactivar el 2FA de un profesional desde el panel de administración (`PUT /api/admin/users/{id}/reset-2fa`), lo que revoca sus sesiones y queda auditado. El superadmin no puede resetearse a sí mismo desde la aplicación; en ese caso hay que hacerlo con SQL, previa verificación de identidad:
  `UPDATE users SET totp_enabled=false, totp_secret_encrypted=NULL, totp_enabled_at=NULL, totp_last_step=NULL, token_version=token_version+1 WHERE username='<superadmin>'; DELETE FROM user_two_factor_recovery_codes WHERE user_id=<id>;`
- El 2FA es opcional. **Recomendación pendiente:** hacerlo obligatorio para `superadmin` y `clinic_admin` (hoy solo se anima desde Ajustes → Seguridad).
- Impacto en la app Android: ninguno. La app móvil es de pacientes y usa `/api/portal/*` con la cookie de paciente; los endpoints modificados (`/api/auth/*`, `/api/admin/*`) son solo del entorno profesional. Contratos añadidos: `POST /api/auth/login/2fa`, `/api/auth/2fa/*` y `twoFactorEnabled` en el listado de usuarios del admin.

### Falsas alarmas de la auditoría anterior retiradas

- **Backups sin cifrar:** descartado. Los backups a Google Drive se suben mediante el remote `crypt` de `rclone` (`docs/BACKUPS_GOOGLE_DRIVE.md`).

### Pendiente (por prioridad)

1. **Media** — Endpoints públicos de curación (`/api/food-curation`, `/api/exercise-curation`): `[AllowAnonymous]` protegidos por una clave compartida cuyo hash está hardcodeado como valor por defecto. Desactivarlos en producción salvo que exista `FOOD_CURATION_KEY_SHA256`, o moverlos a un rol de administración.
2. **Media** — Revisar el override de npm `braces` → `@dieub/braces-depth-guard` (fork de terceros) y confirmar su procedencia, o eliminarlo.
3. **Media** — Confirmar que producción usa una `Jwt:Key` distinta de la del `appsettings.json` local de desarrollo (el archivo está ignorado por git y no figura en el historial).
4. **Baja** — Rotación del refresh de sesión (`refreshSession` no limita la antigüedad total de una sesión), `style-src 'unsafe-inline'` en la CSP, `AllowedHosts: "*"` y volcado de host/usuario de BD en el log de arranque.
5. **Por verificar** — Ventana de tolerancia temporal (anti-replay) de la firma del webhook de Stripe.
6. **Antes de la v1.0 comercial** — Pentest externo, entorno de staging y dominio propio (hoy el despliegue usa un subdominio de DuckDNS).
