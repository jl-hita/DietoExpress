# Asignación semanal de volumen y accesorios — motor 0.8.0

## Alcance

`TrainingProgramVolumeAllocator` reemplaza la asignación uniforme de series y la descarga automática fija cada cuarta semana. Se ejecuta después de seleccionar ejercicios aprobados/propios compatibles y antes de validar y persistir el borrador. No publica ni aplica planes por sí solo.

## Cómo distribuye las series

1. Cada ejercicio empieza con una serie primaria prescrita. El motor añade series una a una hasta aproximarse a los objetivos semanales de series primarias, o hasta alcanzar el límite de series por ejercicio (5) o la duración máxima de la sesión.
2. Los objetivos de asignación se expresan en series primarias directas. Una serie de un músculo primario suma 1 al cómputo estimado; una participación secundaria suma 0,5 solo para el informe de volumen efectivo estimado y nunca sustituye una serie primaria para alcanzar el objetivo. Se conservan también los contadores brutos de series y frecuencia primaria/secundaria.
3. Cada nueva serie se asigna al ejercicio que más reduce el déficit relativo de los músculos que declara en sus metadatos revisados. El desempate es estable por orden de sesión y ejercicio.
4. La UI del generador permite seleccionar cualquiera de los grupos musculares soportados y establecer prioridad (0–3; añade 2 series primarias al objetivo por nivel) o un objetivo explícito de series primarias (1–20). El objetivo explícito prevalece sobre la prioridad. Se validan enteros y rangos antes de enviar la solicitud; el backend rechaza preferencias duplicadas o fuera de rango.
5. La asignación se recalcula por semana y respeta la duración máxima de cada sesión. Si no puede alcanzar un objetivo sin superar los límites, informa de la dosis primaria y de la razón; no añade volumen ilimitado. Si no existe un ejercicio aprobado con ese músculo como primario, informa de la falta de cobertura en vez de contabilizar trabajo secundario como si fuera directo.
6. Las semanas mantienen la distribución salvo que cambien los parámetros. No hay una descarga obligatoria por calendario: una descarga debe justificarse con evolución, fatiga, recuperación y decisión profesional, no con el mero número de semana.

## Qué es evidencia y qué es una heurística

Referencia central: Currier BS et al. (2026), ACSM Position Stand, *Resistance Training Prescription for Muscle Function, Hypertrophy, and Physical Performance in Healthy Adults: An Overview of Reviews*, DOI [10.1249/MSS.0000000000003897](https://doi.org/10.1249/MSS.0000000000003897).

ACSM 2026 sintetiza 137 revisiones sistemáticas y más de 30.000 participantes. Para hipertrofia, un mayor volumen (incluyendo la categoría de ≥10 series semanales) se asocia con mayores resultados en los análisis; el documento también prioriza individualización, consistencia y adherencia. No convierte 10 series en mínimo universal ni prescribe los números concretos de este motor.

Por tanto, las siguientes decisiones son **heurísticas de ingeniería iniciales**, no prescripciones fisiológicas demostradas ni reglas atribuidas a ACSM:

- Objetivo orientativo de series primarias directas: hipertrofia 6/8/10 según experiencia principiante/intermedia/avanzada; fuerza 6/8; condición general 4/6.
- Crédito secundario de 0,5. La literatura no establece una equivalencia universal entre una serie primaria y una secundaria; el factor sirve para no ignorar solapamientos y debe calibrarse.
- Prioridad muscular: +2 series primarias por nivel.
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

La adaptación usa los registros estructurados por ejercicio/serie (exercise_feedback) y la prescripción de esa sesión para recuperar el ID estable, series planificadas/realizadas, motivo de series omitidas y RIR objetivo/real. Se inspeccionan hasta las 12 sesiones registradas más recientes del plan y se agrupan las tres exposiciones más recientes de cada ejercicio.

El motor TrainingExerciseVolumeAdapter propone revisar una reducción de una serie únicamente cuando:
- En al menos dos de las tres exposiciones recientes hay series omitidas por fatiga o el RIR medio real queda al menos una repetición por debajo del objetivo.
- La exposición más reciente todavía muestra esa señal; no mantiene recortes cuando el problema parece haberse resuelto.
- El ejercicio tiene más de una serie prescrita.

El motor no interpreta falta de tiempo, material, enfermedad o motivación como evidencia de que el ejercicio tenga demasiado volumen. El dolor produce PauseAndReview, nunca una reducción ordinaria: debe revisarse el ejercicio y la seguridad antes de reanudarlo. Una exposición incompleta no cuenta como sesión completada para esta evaluación. Las sesiones con resultado `partial` o `not_completed` no cuentan como exposición completada. Para usar el RIR medio deben estar registrados los valores RIR de todas las series de trabajo completadas; un dato parcial o ausente se trata como desconocido, nunca se extrapola.

Si una de las sesiones recientes no contiene una prescripción estructurada válida, la evaluación se bloquea: no se reutilizan exposiciones antiguas como si fueran las tres más recientes. Si una sesión sí tiene prescripción pero falta feedback de un ejercicio, ese ejercicio recibe una exposición neutral desconocida; nunca se interpreta como fatiga.

La propuesta se muestra al profesional con el motivo y los ejercicios candidatos. La aprobación requiere seleccionar explícitamente los IDs de ejercicio que se van a adaptar. El ajustador cambia una serie solo en esos IDs estables y durante un máximo de las tres próximas exposiciones futuras planificadas por ejercicio; después se necesita nuevo feedback y una nueva revisión. No modifica ejercicios parecidos por nombre, sesiones ya registradas, planes inactivos ni el historial completado. Sin evidencia suficiente o sin selección no se aplica ningún recorte.

Los dos de tres registros, la diferencia de 1 RIR y la reducción máxima de una serie son heurísticas operativas conservadoras, no umbrales clínicos ni prescripciones de ACSM. La decisión sigue requiriendo revisión profesional.
## Accesorios opcionales — motor 0.8.0

Los patrones básicos se seleccionan primero. Después, el generador puede añadir accesorios opcionales con patrón `Accessory` para cubrir déficits de grupos musculares primarios que los ejercicios base no cubren suficientemente. No son obligatorios: si el catálogo no ofrece un accesorio aprobado/propio compatible, el plan sigue generándose y comunica la cobertura insuficiente.

- Selección posterior a los patrones básicos; no sustituye sentadilla, bisagra, empujes ni tracciones.
- La selección considera el déficit de series primarias, el grupo corporal de la sesión (superior/inferior), el material, objetivo, experiencia y exclusiones. La prioridad del catálogo solo desempata opciones compatibles; no es una puntuación de calidad. Para ordenar déficits se ponderan los grupos musculares principales con peso 1 y los grupos estabilizadores/menores con 0,6; es una heurística de producto explícita, no una equivalencia fisiológica validada ni una recomendación de ACSM.
- Se reservan hasta tres huecos de accesorio por sesión en planes de dos días y hasta dos en planes de tres o cuatro días. Se omiten si no caben dentro del límite temporal.
- Un accesorio puede repetirse en días distintos si sigue existiendo déficit; no se duplica dentro de una misma sesión. El asignador reparte las series entre sesiones con menor volumen local para evitar concentrarlas en el primer día.
- El informe avisa cuando la estimación de volumen efectivo supera 20 series semanales por grupo muscular. Es un umbral conservador de revisión del producto, no un límite fisiológico ni una cifra prescrita por ACSM; el cálculo secundario sigue siendo una heurística.
- Prescripción inicial de 10–15 repeticiones (8–15 en fuerza), RIR 3 en principiantes y 2 en los demás, con 90 s de descanso. Son valores operativos iniciales, no rangos óptimos establecidos por ACSM.
- El patrón se puede asignar desde la gestión/revisión del catálogo. El enriquecimiento RepDB puede proponerlo de forma determinista para elevaciones laterales, extensiones de cuádriceps, curls, aperturas y aislamientos similares; sigue siendo metadato pendiente de revisión humana cuando corresponde. Una migración de inicio reclasifica los registros RepDB que todavía conservan metadatos automáticos antiguos, siempre que sigan intactos (prioridad automática 55 y autor automático); no sobrescribe decisiones profesionales ni ejercicios propiedad de profesionales.

La evaluación automática incluye ahora extensiones de cuádriceps, curl femoral, elevaciones laterales, curl de bíceps, extensión de tríceps, gemelos, aperturas y abducción de cadera. Los resultados deben analizarse por grupo muscular y duración, no solo por éxito estructural.

## Pruebas

`TrainingProgramVolumeAllocatorTests` cubre crédito secundario, objetivos/prioridades, determinismo, límites de series, duración y preferencias inválidas. `TrainingProgramGeneratorTests` cubre estabilidad de dosis entre semanas, cobertura y matriz de objetivo/experiencia/días/duración. `TrainingAdaptationReviewWorkflowTests` comprueba que no se aplican recortes globales y que los ejercicios compuestos no se alteran si alguno de sus grupos no está seleccionado.
