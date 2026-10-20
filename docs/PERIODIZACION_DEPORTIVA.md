# Periodización deportiva opcional — DietoExpress

El módulo deportivo es **opt-in por cliente**. Los clientes de nutrición no reciben un perfil deportivo automáticamente y el expediente nutricional continúa funcionando si no se activa el módulo.

## Alcance implementado en este PR
- Bootstrap SQL idempotente para perfiles deportivos, planes, sesiones, registros de ejecución y vinculaciones de colaboración.
- Activación/desactivación explícita desde el expediente.
- En la anamnesis inicial se puede elegir entre solo nutrición, planificación a cargo del profesional o colaboración. La elección se guarda en un campo específico y se puede modificar desde el expediente; no se mezcla con las notas libres.
- El profesional puede anunciar en el directorio que ofrece planificación deportiva. Es un distintivo informativo independiente de la activación por cliente.
- Objetivos de resistencia, fuerza, hipertrofia, pérdida de peso, salud y condición física general; nivel de experiencia y modelo de periodización configurable.
- Creación de planes y sesiones; registro de duración real y RPE separado de la prescripción.
- Comprobaciones de acceso del backend basadas en relación profesional-cliente, asignación, tenant, administrador de clínica/superadministrador y colaboración activa.
- Pestaña deportiva opcional en el portal del paciente, con consulta de sesiones y registro de duración/RPE mediante la sesión autenticada del propio paciente.

## Límites conocidos y siguientes fases
- El estado de colaboración se contempla en el modelo de datos y en los controles de acceso, pero falta completar la invitación, aceptación/revocación y la experiencia de gestión de colaboradores.
- Falta un onboarding específico de entrenador y una definición final de permisos granulares para profesionales externos.
- La planificación actual es una primera versión: faltan mesociclos/microciclos editables, editor avanzado de ejercicios, zonas de potencia/ritmo, tendencias, integraciones con dispositivos y sincronización contextual con la nutrición.

El módulo no prescribe actividad automáticamente ni modifica dietas. La decisión de anunciar el servicio, la decisión por cliente y el estado de colaboración son conceptos distintos.
