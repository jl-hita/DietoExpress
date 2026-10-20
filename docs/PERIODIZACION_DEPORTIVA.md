# Periodización deportiva opcional — DietoExpress

El módulo deportivo es **opt-in por cliente**. Los clientes de nutrición no reciben un perfil deportivo automáticamente y el expediente nutricional continúa funcionando si no se activa el módulo.

## Alcance implementado en este PR
- Bootstrap SQL idempotente para perfiles deportivos, planes, sesiones, registros de ejecución y vinculaciones de colaboración.
- Activación/desactivación explícita desde el expediente.
- En la anamnesis inicial se puede elegir entre solo nutrición, planificación a cargo del profesional o colaboración. La elección se guarda en un campo específico y se puede modificar desde el expediente; no se mezcla con las notas libres.
- El profesional puede anunciar en el directorio que ofrece planificación deportiva. Es un distintivo informativo independiente de la activación por cliente.
- Objetivos de resistencia, fuerza, hipertrofia, pérdida de peso, salud y condición física general; nivel de experiencia y modelo de periodización configurable.
- Creación y edición segura de sesiones con fecha, tipo, duración estimada, intensidad/zona objetivo e instrucciones; la fecha debe permanecer dentro del plan.
- Editor visual de sesiones con tarjetas por fecha, iconografía por tipo, estados programada/realizada, resumen de duración real y desviación respecto a lo previsto; edición en contexto sin perder el registro de realización.
- Registro de duración real y RPE separado de la prescripción.
- Jerarquía de periodización con mesociclos dentro del plan y microciclos dentro de cada mesociclo, con creación, edición, reordenación, objetivo/enfoque, fechas y notas.
- Borrado seguro: no se permite eliminar un mesociclo que aún contenga microciclos; se deben eliminar explícitamente antes. Las fechas se validan contra el periodo del padre y se rechazan solapamientos entre ciclos hermanos.
- Las sesiones se validan contra el periodo del plan; el orden se actualiza de forma transaccional para no dejar secuencias parciales si falla la operación.
- Flujo básico de colaboración: crear una invitación identificando por correo a un profesional de la misma clínica, consultar estado, aceptar invitaciones y revocar acceso. La colaboración activa tiene acceso de consulta y no puede gestionar el plan.
- Comprobaciones de acceso del backend basadas en relación profesional-cliente, asignación, tenant, administrador de clínica/superadministrador y colaboración activa.
- Pestaña deportiva opcional en el portal del paciente, con consulta de sesiones y registro de duración/RPE mediante la sesión autenticada del propio paciente.

## Límites conocidos y siguientes fases
- El flujo de colaboración requiere pruebas integrales con cuentas reales de dos profesionales, especialmente para comprobar límites entre clínicas y los casos de revocación. La invitación se muestra dentro de DietoExpress; todavía no envía un email automático al profesional invitado. Las cuentas sin tenant de clínica no se pueden invitar como colaboradoras.
- Falta un onboarding específico de entrenador y una definición final de permisos granulares para profesionales externos.
- Queda probar en entorno real los recorridos de edición/reordenación/borrado y reforzar pruebas E2E del módulo.
- Quedan el editor avanzado de ejercicios, zonas de potencia/ritmo, tendencias, integraciones con dispositivos y sincronización contextual con la nutrición.

El módulo no prescribe actividad automáticamente ni modifica dietas. La decisión de anunciar el servicio, la decisión por cliente y el estado de colaboración son conceptos distintos.
