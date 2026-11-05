# Asignación semanal de volumen — motor 0.6.0

## Alcance

`TrainingProgramVolumeAllocator` reemplaza la asignación uniforme de series y la descarga automática fija cada cuarta semana. Se ejecuta después de seleccionar ejercicios aprobados/propios compatibles y antes de validar y persistir el borrador. No publica ni aplica planes por sí solo.

## Cómo distribuye las series

1. Cada ejercicio empieza con una serie efectiva prescrita. El motor añade series una a una hasta aproximarse a los objetivos semanales, o hasta alcanzar el límite de series por ejercicio (5) o la duración máxima de la sesión.
2. Una serie de un músculo primario suma 1 al cómputo estimado. Una participación secundaria suma 0,5. Se conservan también los contadores brutos de series y frecuencia primaria/secundaria.
3. Cada nueva serie se asigna al ejercicio que más reduce el déficit relativo de los músculos que declara en sus metadatos revisados. El desempate es estable por orden de sesión y ejercicio.
4. Las preferencias profesionales pueden establecer prioridad (0–3; añade 2 series efectivas estimadas por nivel) o un objetivo explícito (1–20). Se rechazan preferencias duplicadas o fuera de rango.
5. La asignación se recalcula por semana y respeta la duración máxima de cada sesión. Si no puede alcanzar un objetivo sin superar los límites, informa de la dosis estimada y de la razón; no añade volumen ilimitado.
6. Las semanas mantienen la distribución salvo que cambien los parámetros. No hay una descarga obligatoria por calendario: una descarga debe justificarse con evolución, fatiga, recuperación y decisión profesional, no con el mero número de semana.

## Qué es evidencia y qué es una heurística

Referencia central: Currier BS et al. (2026), ACSM Position Stand, *Resistance Training Prescription for Muscle Function, Hypertrophy, and Physical Performance in Healthy Adults: An Overview of Reviews*, DOI [10.1249/MSS.0000000000003897](https://doi.org/10.1249/MSS.0000000000003897).

ACSM 2026 sintetiza 137 revisiones sistemáticas y más de 30.000 participantes. Para hipertrofia, un mayor volumen (incluyendo la categoría de ≥10 series semanales) se asocia con mayores resultados en los análisis; el documento también prioriza individualización, consistencia y adherencia. No convierte 10 series en mínimo universal ni prescribe los números concretos de este motor.

Por tanto, las siguientes decisiones son **heurísticas de ingeniería iniciales**, no prescripciones fisiológicas demostradas ni reglas atribuidas a ACSM:

- Objetivo efectivo orientativo de partida: hipertrofia 6/8/10 según experiencia principiante/intermedia/avanzada; fuerza 6/8; condición general 4/6.
- Crédito secundario de 0,5. La literatura no establece una equivalencia universal entre una serie primaria y una secundaria; el factor sirve para no ignorar solapamientos y debe calibrarse.
- Prioridad muscular: +2 series efectivas estimadas por nivel.
- Máximo de 5 series por ejercicio dentro de este asignador y presupuesto de duración como límites operativos.
- Estimación temporal: duración de la serie, descanso entre series, transiciones y cinco minutos de preparación. Es aproximada, no una promesa de duración real.

Estos valores son puntos de partida conservadores que deben quedar editables/revisables por el profesional. Un músculo puede quedar por debajo del objetivo si el catálogo aprobado, el reparto de patrones o el tiempo disponible no permiten cubrirlo; se informa en vez de inventar ejercicios o incumplir la disponibilidad.

## Limitaciones explícitas

- La calidad de la asignación depende de los músculos primarios/secundarios revisados en el catálogo. Un metadato ausente no se infiere desde el nombre.
- El factor de contribución secundaria no modela longitud muscular, perfil de resistencia, biomecánica individual ni proximidad real al fallo.
- Los objetivos de dosis no sustituyen una evaluación individual de tolerancia, dolor, recuperación, preferencias o adherencia.
- La duración es una estimación y puede variar según descansos reales, preparación, ocupación del material y nivel técnico.
- El asignador no diagnostica ni decide por sí mismo una descarga clínica. El borrador requiere revisión profesional.

## Adaptación de volumen futuro

El feedback global de recuperación no identifica qué músculo necesita menos trabajo. Por ello, la aprobación de una propuesta de reducción ya no recorta todas las sesiones/ejercicios. El profesional debe seleccionar los grupos afectados. Solo se reduce una serie en un ejercicio cuando todos los grupos primarios y secundarios declarados para ese ejercicio están incluidos en la selección; los ejercicios compuestos con grupos no seleccionados se conservan. Sin selección, metadatos completos o series por encima de una, no se modifica nada.

Solo se consideran sesiones futuras con estado planificado y sin registro asociado. La actualización de prescripción estructurada y texto legado ocurre dentro de la transacción de revisión. El historial completado no se reescribe.

Este mecanismo es una salvaguarda de revisión profesional, no todavía una autorregulación automática plenamente individualizada: para automatizar recomendaciones por músculo hacen falta registros longitudinales de recuperación/rendimiento específicos por músculo o ejercicio. No se infiere esa granularidad a partir de RPE global.

## Pruebas

`TrainingProgramVolumeAllocatorTests` cubre crédito secundario, objetivos/prioridades, determinismo, límites de series, duración y preferencias inválidas. `TrainingProgramGeneratorTests` cubre estabilidad de dosis entre semanas, cobertura y matriz de objetivo/experiencia/días/duración. `TrainingAdaptationReviewWorkflowTests` comprueba que no se aplican recortes globales y que los ejercicios compuestos no se alteran si alguno de sus grupos no está seleccionado.
