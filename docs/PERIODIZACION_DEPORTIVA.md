# Periodización deportiva opcional — DietoExpress

Este PR introduce la primera vertical funcional del módulo deportivo como **funcionalidad opt-in por cliente**. Los clientes de nutrición no reciben un perfil deportivo automáticamente y el expediente nutricional continúa funcionando sin cambios si no se activa el módulo.

## Alcance implementado
- Bootstrap idempotente para perfiles deportivos, planes, sesiones, registros de ejecución y vinculaciones de colaboración.
- Activación/desactivación explícita desde el expediente del cliente.
- Objetivos de resistencia, fuerza, hipertrofia, pérdida de peso, salud y condición física general.
- Creación de planes con modelo de periodización configurable.
- Creación de sesiones y registro de duración real y RPE.
- Comprobaciones de acceso en el backend usando la relación del cliente, asignación profesional, tenant y colaboración activa.
- Registro separado de prescripción y ejecución para no sobrescribir lo previsto al registrar lo realizado.

## Alcance pendiente de fases posteriores
- Portal del deportista para consultar sesiones y registrar duración real/RPE usando su propia sesión autenticada.
- Flujo de invitación y aceptación de entrenadores, onboarding de rol entrenador y guardas de navegación correspondientes.
- Bloques/mesociclos/microciclos editables, editor avanzado de ejercicios y métricas específicas de potencia/ritmo/zonas.
- Sincronización contextual con el generador de dietas, dashboards de tendencias, importaciones de dispositivos y alertas configurables.

El módulo es opcional por cliente, pero esta primera entrega no activa ni prescribe actividad física automáticamente. La pestaña deportiva del portal solo aparece si el perfil está activado por el profesional.
