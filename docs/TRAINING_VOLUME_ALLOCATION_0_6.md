# Asignación semanal de volumen y accesorios — motor 0.8.0

## Alcance

`TrainingProgramVolumeAllocator` reemplaza la asignación uniforme de series y la descarga automática fija cada cuarta semana. Se ejecuta después de seleccionar ejercicios aprobados/propios compatibles y antes de validar y persistir el borrador. No publica ni aplica planes por sí solo.

## Cómo distribuye las series

1. Cada ejercicio empieza con una serie prescrita. El motor añade series hasta el objetivo central de cada grupo o hasta alcanzar el límite de cinco series por ejercicio o la duración máxima de la sesión.
2. Los objetivos de hipertrofia son rangos semanales, no mínimos rígidos: principiante 8–12 (objetivo central 10), intermedio 10–16 (central 13) y avanzado 12–20 (central 16). La tolerancia predeterminada es, respectivamente, ±2, ±3 y ±4 series. Un objetivo explícito del profesional usa ±2; por ejemplo, 20 significa un rango aceptable de 18–22. La prioridad muscular desplaza el objetivo central, pero no convierte el extremo superior en obligación.
3. Los objetivos se expresan en series con estímulo hipertrófico efectivo estimado. Una serie cuenta completa para cada músculo que recibe una contribución dinámica relevante; no se aplica un crédito universal de 0,5 a todo músculo secundario. La categoría anatómica primaria/secundaria se conserva para trazabilidad, pero no determina por sí sola el cómputo.
4. TrainingExerciseStimulus contiene una auditoría conservadora por ID de ejercicio para las contribuciones secundarias relevantes del catálogo curado. Por defecto solo cuentan los músculos primarios. Se incluyen como contribuciones efectivas secundarias, cuando están declaradas en el catálogo, los tríceps en presses, los bíceps en remos/jalones/dominadas, cuádriceps y glúteos en variantes de sentadilla/prensa/zancada, glúteos e isquiotibiales en bisagras, y espalda alta en reverse fly/face pull. No se cuentan como series hipertróficas la estabilización, la participación incidental ni el agarre estático.
5. Cada nueva serie se asigna al ejercicio que más reduce el déficit relativo de los músculos que reciben estímulo efectivo según la auditoría. Un compuesto puede contribuir a varios grupos a la vez; no se duplican series dentro del mismo músculo por aparecer en las dos listas anatómicas.
6. El generador permite preferencias por grupo muscular. El campo histórico TargetWeeklyPrimarySets se conserva por compatibilidad de API, pero su valor se interpreta ahora como objetivo central de series efectivas estimadas, no como cuota exclusiva de músculos primarios. Los objetivos explícitos prevalecen sobre la prioridad.
7. La asignación se recalcula por semana y respeta la duración máxima de cada sesión. En hipertrofia no se reducen silenciosamente los objetivos por tener poco tiempo: se informa de la dosis alcanzada y de si queda por debajo del rango. Para fuerza y condición general se mantienen los objetivos conservadores anteriores.
8. Los grupos menores o especializados (p. ej. tibial anterior, manguito rotador, aductores, abductores, flexores de cadera y oblicuos) no reciben automáticamente una cuota general de hipertrofia. Se entrenan directamente cuando el objetivo, la prioridad profesional y el contexto lo justifican.

## Fundamento y límites de la evidencia

Referencia principal: Currier BS et al. (2026), ACSM Position Stand, *Resistance Training Prescription for Muscle Function, Hypertrophy, and Physical Performance in Healthy Adults: An Overview of Reviews*, DOI [10.1249/MSS.0000000000003897](https://doi.org/10.1249/MSS.0000000000003897).

El Position Stand sintetiza 137 revisiones sistemáticas y más de 30.000 participantes. Para hipertrofia, identifica una relación dosis-respuesta y una mejora con volúmenes de al menos 10 series semanales; el análisis discute rendimientos decrecientes aproximadamente a partir de 18–20 series semanales. No define un mínimo universal obligatorio, no prescribe rangos diferentes por experiencia y no afirma que cada músculo deba recibir 20 series. Los rangos de DietoExpress son una política inicial de producto informada por esa evidencia, no una tabla oficial de ACSM.

La distinción de estímulo por ejercicio también es una decisión de programación, no una equivalencia fisiológica cuantificada por el Position Stand. Se evita atribuir un porcentaje fijo a toda participación secundaria. El mapeo es deliberadamente conservador y debe revisarse si se añaden ejercicios o cambian sus metadatos.

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

`TrainingProgramVolumeAllocatorTests` cubre el estímulo efectivo auditado por ejercicio, exclusión de co-contracción incidental, rangos de hipertrofia, objetivos/prioridades, determinismo, límites de series, duración y preferencias inválidas. `TrainingProgramGeneratorTests` cubre estabilidad de dosis entre semanas, cobertura y matriz de objetivo/experiencia/días/duración. `TrainingAdaptationReviewWorkflowTests` comprueba que no se aplican recortes globales y que los ejercicios compuestos no se alteran si alguno de sus grupos no está seleccionado.


## Auditoría con catálogo RepDB real — ejecución 06/11/2026 (artefacto detallado v2)

La ejecución CI #42187931616, asociada exactamente al commit `6944a4b6620c099809cb429f2cab455f20fd4c8c`, evaluó el catálogo aislado tras importar y ejecutar la autocuración real. El artefacto `training-generator-evaluation` contiene el informe matricial y el informe de catálogo real con el detalle de sesiones, ejercicios y series por músculo.

- 301 ejercicios RepDB importados; 214 aprobados y disponibles como candidatos.
- 81/81 escenarios generados sin errores estructurales (27 combinaciones por objetivo, experiencia, días y presupuesto).
- 2.244 advertencias acumuladas: 1.892 déficits frente a objetivos orientativos de series primarias, 324 avisos de ausencia de cobertura en metadatos y 28 avisos de volumen efectivo estimado alto. Las advertencias se repiten por semana; no equivalen a 2.244 defectos independientes.
- Presupuesto de duración: para 35 min, máximo estimado 35; para 60 min, máximo 60; para 90 min, máximo 88. Todos los escenarios respetaron el límite. Algunos planes de 90 min terminan bastante antes porque el presupuesto es un máximo y el motor no debe rellenarlo artificialmente.
- La cobertura primaria de aductores, abductores, tibial anterior y manguito rotador falta en los 81 escenarios. Es una carencia persistente del catálogo/selección actual, no una obligación de añadir aislamiento de esos grupos a todas las rutinas. Debe mantenerse visible y resolverse solo cuando haya ejercicios aprobados, apropiados y útiles.
- Los déficits de series más frecuentes aparecen en espalda alta, erectores espinales, flexores de cadera, cuádriceps, tríceps, pecho y femorales. Son objetivos orientativos del motor, no mínimos universales de ACSM; deben revisarse junto a la dosis real, frecuencia, ejercicios y tiempo.
- El informe v2 permite auditar series primarias y exposiciones secundarias por semana, además de cada sesión con nombre/ID de ejercicio, series, repeticiones y músculos. Se identificaron 55 ejercicios distintos en el conjunto de escenarios, con básicos cargables como press banca/Smith, jalones, remos, prensa, bisagras y aislamientos de máquina/cable cuando son compatibles.

La evaluación confirma que 81/81 estructural no basta. La revisión debe considerar calidad de la selección, déficits persistentes, distribución entre sesiones, dosis y duración; no se debe optimizar para eliminar advertencias a costa de añadir volumen o ejercicios sin justificación.
 
### Corrección de selección por prioridad

Se ha detectado que muchos ejercicios empataban en prioridad y el desempate alfabético por ID podía seleccionar variantes asistidas, bandas o peso corporal antes que alternativas más progresables. La nueva curation de prioridad es una heurística de producto explícita:

- Favorece movimientos básicos cargables y medibles cuando son compatibles (p. ej. press, jalón, remo, sentadilla/prensa y bisagra).
- Favorece aislamientos con carga externa o máquina/cable frente a variantes de banda o peso corporal cuando compiten por el mismo déficit.
- No excluye automáticamente ejercicios de peso corporal o bandas; siguen siendo opciones válidas cuando el contexto lo requiere.
- Solo modifica aprobaciones automáticas RepDB; conserva intactas las decisiones manuales de profesionales.
- Las prioridades no representan una jerarquía científica universal ni sustituyen la revisión de metadatos y selección.

La auditoría debe repetirse con el artefacto detallado después de esta corrección. No se considera superada por el mero 81/81 estructural: hay que revisar los déficits, la selección concreta, el presupuesto temporal y la cobertura real del catálogo.


## Auditoría anatómica y prioridad del catálogo — bloqueo P1 (06/11/2026)

La evaluación con catálogo real selecciona 55 IDs distintos en sus 81 escenarios, sobre un catálogo de 214 candidatos aprobados. La aprobación automática actual no equivale a una validación anatómica individual: `RepDbExerciseAutoReviewer` traduce los campos `primary_muscles` y `secondary_muscles` de RepDB y asigna prioridad mediante reglas de texto/patrón; no constituye una revisión biomecánica por ejercicio.

La primera comprobación del catálogo fuente ya detecta inconsistencias entre variantes de sentadilla:
- `squat` (sentadilla trasera con barra) declara glúteo mayor y cuádriceps como principales, y erectores espinales e isquiotibiales como secundarios; no incluye aductores.
- `barbell-box-squat`, `squat-hold` y `squat-pulse` sí incluyen aductores secundarios.
- `db-squat` tampoco los incluye.
- `db-goblet-squat` sí incluye aductores secundarios.

Esto no demuestra que todas las variantes deban tener exactamente la misma lista: la profundidad, la postura, el apoyo y la mecánica cambian la contribución muscular. Sí demuestra que no debemos aceptar sin revisión las etiquetas del proveedor ni trasladarlas directamente al cómputo de volumen.

Criterio de auditoría pendiente:
1. Revisar individualmente los 214 candidatos aprobados que puede utilizar el generador; empezar por los 55 IDs observados en las evaluaciones y continuar con el resto.
2. Asignar prioridad de selección por utilidad para fuerza/hipertrofia general, capacidad de sobrecarga y progresión, estabilidad, facilidad de aprendizaje y redundancia; no por popularidad ni por coincidencia de palabras.
3. Revisar músculos principales/secundarios por ejecución concreta. Un secundario solo debe incluirse si recibe una contribución dinámica relevante y suficiente; si la recibe, cuenta como serie completa. No añadir estabilizadores menores por defecto.
4. Separar metadatos anatómicos del rol del ejercicio (básico, variante, aislamiento, accesorio, regresión/técnica). No excluir automáticamente aislamientos útiles ni variantes con peso corporal cuando tengan una función clara.
5. Añadir correcciones explícitas y versionadas por ID, con pruebas de regresión que impidan que una actualización de RepDB sobrescriba la curación local.
6. Repetir la evaluación de los 81 escenarios y comparar selección, cobertura y volumen antes/después.

La prioridad actual de `GetSelectionPriority` es una heurística de texto/patrón, no una valoración manual completa por ID. Por tanto, **la auditoría del catálogo es bloqueante para dar P1 por terminado**.

Acceso de auditoría: el propietario ha confirmado que dispone de permiso para exportar el catálogo. Se habilita `GET /api/exercise-catalog/export`, restringido a `superadmin`, que devuelve todos los registros (aprobados, pendientes, rechazados y contribuciones profesionales), los campos normalizados/revisados y el `source_payload` original. La exportación incluye contadores por estado y total devuelto. No debe publicarse sin autenticación ni utilizarse como API pública; el permiso de exportación confirmado se limita a esta auditoría controlada.


### Exportación completa para la auditoría anatómica

- Endpoint: `GET /api/exercise-catalog/export`.
- Autorización: rol `superadmin` obligatorio; la ruta no está disponible para profesionales ordinarios.
- Incluye todos los estados y los metadatos originales y curados para poder comparar el dato de origen con la corrección local.
- La descarga debe realizarse desde un entorno autorizado y conservarse como material de trabajo de auditoría, no publicarse ni redistribuirse.
- La auditoría debe abarcar los 214 candidatos aprobados y registrar por separado el resto de registros del catálogo; no se debe confundir “todo el catálogo exportado” con “todo el catálogo apto para el generador”.


## Accesorios específicos recuperados de RepDB (06/11/2026)

La revisión del manifiesto público de RepDB (701 ejercicios) detectó dos movimientos de fuerza útiles que la curación anterior podía dejar fuera del conjunto automático:

- `kettlebell-tibialis-raise`: RepDB lo agrupaba en `calf-raise`; el filtro de variantes podía excluirlo por no estar en la lista de elevaciones de gemelos. Se reasigna localmente a `tibialis-raise` y se reconoce como accesorio para dar cobertura específica al tibial anterior.
- `cable-external-rotation`: estaba excluido expresamente pese a ser un aislamiento cargable y específico de rotadores externos del hombro (infraespinoso y redondo menor). Se recupera como accesorio opcional; no sustituye empujes/tracciones ni se prescribe indiscriminadamente.

La máquina de aducción de cadera (`hip-adduction`) también queda identificada como opción de aislamiento útil para aductores. La abducción de cadera ya estaba contemplada. Estos accesorios solo pueden entrar después de los patrones básicos, si el déficit muscular, el material y el tiempo lo justifican.

La corrección no afirma que estos ejercicios falten físicamente en la fuente RepDB: estaban en su manifiesto. Corrige su elegibilidad y clasificación dentro del generador DietoExpress. La comprobación de que existan ya como filas aprobadas en la PostgreSQL desplegada sigue requiriendo ejecutar la exportación administrativa completa.
