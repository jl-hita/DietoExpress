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
- Calendario semanal con navegación entre semanas, retorno a la semana actual, siete columnas por día, acceso rápido para editar/registrar sesiones y resumen semanal de sesiones programadas, minutos planificados y sesiones completadas; vista de lista alternativa para editar en detalle.
- Registro de duración real y RPE separado de la prescripción.
- Prescripción de ejercicios con series, repeticiones, descansos y observaciones, visible tanto al profesional como al paciente desde su portal.
- Tendencia descriptiva de carga de las últimas 12 semanas por plan, calculada como duración real × RPE, sin generar recomendaciones automáticas.
- Exportación CSV de sesiones planificadas y realizadas para análisis externo e intercambio con hojas de cálculo o herramientas deportivas.
- Jerarquía de periodización con mesociclos dentro del plan y microciclos dentro de cada mesociclo, con creación, edición, reordenación, objetivo/enfoque, fechas y notas.
- Borrado seguro: no se permite eliminar un mesociclo que aún contenga microciclos; se deben eliminar explícitamente antes. Las fechas se validan contra el periodo del padre y se rechazan solapamientos entre ciclos hermanos.
- Las sesiones se validan contra el periodo del plan; el orden se actualiza de forma transaccional para no dejar secuencias parciales si falla la operación.
- Flujo básico de colaboración: crear una invitación identificando por correo a un profesional de la misma clínica, consultar estado, aceptar invitaciones y revocar acceso. La colaboración activa tiene acceso de consulta y no puede gestionar el plan.
- Comprobaciones de acceso del backend basadas en relación profesional-cliente, asignación, tenant, administrador de clínica/superadministrador y colaboración activa.
- Pestaña deportiva opcional en el portal del paciente, con consulta de sesiones y registro de duración/RPE mediante la sesión autenticada del propio paciente.

## Límites conocidos y siguientes fases
- El flujo de colaboración requiere pruebas integrales con cuentas reales de dos profesionales, especialmente para comprobar límites entre clínicas y los casos de revocación. Las invitaciones envían un aviso por correo a través del servicio SMTP existente; un fallo de email no invalida la invitación interna. Las cuentas sin tenant de clínica no se pueden invitar como colaboradoras.
- Falta un onboarding específico de entrenador y una definición final de permisos granulares para profesionales externos.
- Queda probar en entorno real los recorridos de edición/reordenación/borrado y reforzar pruebas E2E del módulo. La ampliación multimedia depende de la disponibilidad de RepDB; quedan pendientes filtros anatómicos avanzados, variantes/sustituciones y progresiones explícitas. La generación de sesiones sigue siendo determinista y requiere revisión profesional.
- Biblioteca de ejercicios integrada en el editor de sesiones, con búsqueda por texto y filtros por objetivo/material; permite añadir ejercicios directamente a la prescripción y evita duplicados evidentes. La selección curada se amplía bajo demanda con el catálogo público de RepDB (650 ejercicios, nombres/instrucciones en español e ilustraciones); si la fuente externa no responde, la biblioteca básica sigue disponible. Se muestra atribución visible y no se incluye una copia masiva del dataset en el repositorio.
- Generación asistida determinista de borradores para fuerza, hipertrofia, resistencia y movilidad, ajustada por nivel y material seleccionado. No es una prescripción clínica automática: el profesional debe revisar y personalizar cada propuesta.
- Reutilización de sesiones guardadas como plantillas: copia tipo, duración, intensidad, instrucciones y ejercicios de una sesión existente del mismo plan. La plantilla queda basada en una sesión persistida, sin una entidad de plantillas independiente.
- Las zonas de potencia/ritmo se pueden indicar de forma libre en el campo de zona objetivo; queda pendiente la configuración estructurada por deporte y disciplina.
- La tendencia de carga es descriptiva. Quedan integraciones con dispositivos/plataformas externas y sincronización contextual con la nutrición; requieren seleccionar proveedores, definir permisos de consentimiento y probar credenciales/API reales antes de habilitarlas.

El módulo no prescribe actividad automáticamente ni modifica dietas. La decisión de anunciar el servicio, la decisión por cliente y el estado de colaboración son conceptos distintos.
