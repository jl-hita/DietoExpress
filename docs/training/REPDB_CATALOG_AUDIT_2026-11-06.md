# Auditoría inicial del catálogo RepDB — 06/11/2026

## Fuente y alcance

- Fuente: exportación completa de `/api/exercise-catalog/audit-export`, generada el 06/11/2026 a las 11:17:56 UTC.
- La exportación local declara `totalRecords = returned = 466` y `complete = true`. Es una fotografía de la base de datos local, no el total del catálogo oficial.
- Se reconstruyó y validó el JSON completo de las 466 entradas del PDF; los 466 IDs son únicos y cada `sourcePayload.id` coincide con el ID exterior.
- Esta es una auditoría del catálogo de origen y de su idoneidad para el generador general de fuerza/hipertrofia. No equivale a una validación clínica ni a una revisión presencial de técnica por un profesional.

## Resultado del inventario local (466) y contraste con la fuente oficial (701)

Se contrastó la exportación local con el repositorio oficial [RepDB/exercise-dataset](https://github.com/RepDB/exercise-dataset), snapshot `66c4298af8d99b9dc397dad6699e508fb871d249`. El JSON oficial contiene 701 ejercicios. Los 466 IDs locales existen en esa versión oficial; no hay IDs locales obsoletos ni desaparecidos. Sin embargo, la base local no contenía 235 ejercicios de la fuente oficial: 214 de fuerza, 16 olímpicos, 4 pliométricos y 1 de cardio.

Los 214 ejercicios de fuerza ausentes incluyen muchas variantes de patrones ya cubiertos y movimientos avanzados o especializados. Se compararon músculos primarios, material, dificultad, objetivos y grupos de variación. El catálogo oficial contiene `kettlebell-tibialis-raise`, con instrucciones en español y dos imágenes. Se conserva como accesorio especializado opcional, pero el trabajo directo de tibial anterior no se considera una carencia prioritaria ni un requisito para hipertrofia general o para cerrar P1.

La licencia de RepDB permite uso comercial dentro de aplicaciones con atribución visible, pero no redistribuir el dataset como producto independiente. Se mantiene la atribución visible en el editor de entrenamiento y también en el README público del proyecto. No se utilizarán los recursos de `premium-samples/`, que son solo de evaluación.

## Resultado del inventario completo

| Campo | Resultado |
|---|---:|
| Registros de origen | 466 |
| Estado `approved` en la exportación | 221 |
| Estado `pending` | 81 |
| Estado `rejected` | 164 |
| IDs duplicados | 0 |
| Nombres en español ausentes | 0 |
| Descripciones en español ausentes | 0 |
| Instrucciones en español ausentes | 0 |
| Referencias de imagen ausentes | 0 |
| Ejercicios con imagen `start` + `peak` | 318 |
| Ejercicios con una imagen `main` | 148 |
| Ejercicios sin material declarado | 171 (todos marcados como peso corporal) |
| Ejercicios con material declarado | 295 |
| Ejercicios cargados sin material declarado | 0 |

Los 221 registros aprobados en esta exportación no deben interpretarse como 221 revisiones clínicas humanas. El estado representa la revisión/metadatación existente en la base de datos en ese momento. En particular, la exportación no incluye identidad del revisor que permita atribuir una aprobación a una persona.

### Distribución de estados

- Los 221 aprobados se reparten en 220 ejercicios de fuerza y un ejercicio de cardio (`rowing-machine`), que no forma parte del generador de fuerza/hipertrofia.
- Los 81 pendientes son de categoría fuerza. Muchos son ejercicios válidos, pero el estado pendiente indica que el mapeo automático anterior no había podido resolverlos con suficiente confianza.
- Los 164 rechazados se reparten en 89 ejercicios de estiramiento, 17 de cardio y 58 de fuerza. Los rechazados de fuerza incluyen movimientos de yoga/Pilates, habilidades gimnásticas, trabajo de rehabilitación/activación y variantes redundantes. El rechazo para este generador no significa que el movimiento carezca de utilidad en cualquier contexto.

## Catálogo seleccionado para el generador

La selección explícita pasa de 84 a **97 ejercicios**. No se intenta importar indiscriminadamente los 701 registros: el generador recibe una biblioteca deliberadamente curada, progresable y con alternativas de equipamiento.

Composición de los 97 IDs frente al estado que tenían en la exportación local:

| Estado en la exportación | Seleccionados |
|---|---:|
| approved | 52 |
| pending | 42 |
| rejected, con excepción y corrección explícita | 2 |
| incorporado desde el upstream oficial (no estaba en la exportación local) | 1 |
| **Total** | **97** |

Los dos ejercicios rechazados que se mantienen mediante decisión explícita son:

- `cable-external-rotation`: se corrige la anatomía para asignar como principales infraespinoso y redondo menor, dejando el deltoides posterior como secundario. No se debe usar la clasificación anatómica errónea de la fuente.
- `hammer-curl`: se recupera como aislamiento útil y progresable para bíceps/braquial, con mancuernas. La variante de cable se mantiene fuera para evitar duplicación innecesaria.

Se incorporan además seis ejercicios pendientes que aportan alternativas reales, no simples duplicados:

- `hip-abduction`: máquina de abducción, carga progresable para glúteo medio.
- `plate-loaded-glute-drive`: alternativa de máquina para extensión de cadera/glúteos.
- `shrug`: trabajo directo y progresable de trapecio.
- `straight-arm-pulldown`: aislamiento de dorsal con polea.
- `overhead-tricep-extension`: alternativa con mancuerna para tríceps y usuarios sin polea.
- `incline-push-ups`: variante escalable de empuje para principiantes.

Se incorpora también `db-fly` (aperturas con mancuernas), que figuraba entre los pendientes no seleccionados en la primera revisión. La evaluación real detectó que los perfiles con mancuernas, banco y barra de dominadas no disponían de aislamiento de pecho compatible: las aperturas de cable y máquina no podían seleccionarse. RepDB documenta `db-fly` como aislamiento de pectoral, de nivel principiante, con mancuerna, instrucciones en español e imágenes start/peak. Es una alternativa complementaria, no un sustituto del press de banca.

Se recuperan además tres registros que ya estaban aprobados en la exportación local, pero que no habían entrado en la lista permitida pese a estar incluidos en el grupo de variación de curl femoral:

- `banded-standing-curl`: curl femoral de pie con banda; principiante, útil como opción doméstica de baja carga y con prioridad inferior a las variantes más progresables.
- `ball-leg-curl`: curl femoral con pelota de estabilidad; intermedio, con flexión de rodilla y una progresión bilateral a unilateral.
- `trx-hamstring-curl`: curl femoral en entrenador de suspensión; intermedio, con trabajo de flexión de rodilla.

Los tres tienen instrucciones en español e imágenes `start`/`peak` en el snapshot auditado. Además, se incorpora `reverse-nordic-curl`, ya aprobado en la exportación local: es una extensión de rodilla de peso corporal, intermedia y con imágenes/instrucciones, clasificada explícitamente como accesorio (no como sentadilla). Su prioridad es inferior a la extensión de cuádriceps de máquina y solo cubre el hueco cuando no hay una opción cargable compatible. Se clasifican explícitamente como accesorios y se normaliza `banded-standing-curl` al grupo `leg-curl`, corrigiendo su grupo de variación de origen. Su prioridad queda por debajo de las opciones de máquina/cable cuando estas están disponibles; no desplazan los básicos ni se ofrecen si falta el material requerido.

Los 39 pendientes restantes no se incorporan al generador inicial: son variantes redundantes de ejercicios ya representados, habilidades/posiciones específicas o movimientos accesorios menos prioritarios para el alcance de esta primera biblioteca. Se mantienen fuera de la lista permitida; no se convierten automáticamente en ejercicios aprobados ni se presentan como clínicamente incorrectos. La lista permitida es la frontera efectiva de lo que puede seleccionar el generador.

De los 97 seleccionados, 95 tienen las dos imágenes `start` y `peak`; `dumbbell-farmers-walk` y `side-plank` solo tienen `main`. El nuevo `kettlebell-tibialis-raise` tiene dos imágenes `start`/`peak` en el repositorio oficial. Las 192 rutas de imagen requeridas por los 97 seleccionados existen en el árbol del repositorio oficial. La presencia del archivo no demuestra todavía que el CDN remoto responda ni que Android lo represente; esas comprobaciones siguen pendientes en sus fases específicas.

## Hallazgos anatómicos y de metadatos

- En la exportación local de 466 registros no había ningún ejercicio con `tibialis_anterior` como músculo primario; solo aparecía en dos asignaciones secundarias. El contraste con los 701 registros oficiales encontró `kettlebell-tibialis-raise`. Se conserva como opción especializada, con sus instrucciones e imágenes oficiales y doble requisito de equipamiento (`bench` + `kettlebell`), pero no se usa como indicador de cobertura general del tren inferior.
- El catálogo de origen contiene 16 registros con romboides como músculo primario y 16 con trapecio como primario. La selección añade `shrug` y conserva remos con romboides/espalda alta, pero el volumen efectivo de espalda alta debe validarse en el generador, no inferirse por el número de ejercicios.
- Isquiotibiales aparecen como primarios en 44 registros; cuádriceps en 69. La biblioteca seleccionada incluye curls femorales en máquina, Nordic y alternativas con banda, pelota de estabilidad y suspensión; también incluye Nordic inverso como accesorio de cuádriceps sin material para perfiles domésticos. Se mantienen bisagras, sentadillas, prensa y extensiones como base. Las advertencias de volumen del informe de generación no se resolverán simplemente acumulando variantes: habrá que revisar el asignador y las restricciones por escenario.
- Se detectaron tres diferencias entre la musculatura exterior curada y el payload original: `sit-ups`, `hex-bar-deadlift` y `cossack-squat`. Se conservan como correcciones explícitas en `RepDbExerciseCuration.CurateMusclesJson` y quedan cubiertas por una prueba de regresión; no se sobrescriben silenciosamente con los datos externos.
- La exportación local antigua contiene falsos positivos de patrón: `cable-reverse-fly`, `close-grip-barbell-curl`, `drag-curl` y `zottman-curl` aparecen como tracción horizontal; `single-leg-calf-raise` aparece como ejercicio unilateral; `crow-pose` se confunde con una tracción; y varias flexiones laterales se confunden con empujes. Las reglas actuales deben clasificarlas como accesorios o excluirlas cuando no sean ejercicios progresables adecuados.
- También hay inconsistencias de normalización entre variantes de remo (por ejemplo, músculos anatómicos sin normalizar frente a los grupos controlados `Lats`/`UpperBack`). La migración de refresco de metadatos automáticos de PR #503 debe integrarse con el bootstrap de PR #504 y comprobarse con tests; no se deben sobrescribir revisiones humanas.

## Criterios que siguen abiertos

1. Integrar y validar conjuntamente las correcciones de metadatos de PR #503 y el catálogo curado de PR #504.
2. Verificar en el informe de evaluación que los 97 ejercicios seleccionados son candidatos reales y que las imágenes/instrucciones se conservan tras importar.
3. Validar que `kettlebell-tibialis-raise` permanece como accesorio especializado opcional y que no desplaza ejercicios prioritarios ni condiciona la generación de rutinas generales.
4. Comprobar la disponibilidad real de las imágenes y su visualización en portal y Android.
5. Revisar los avisos de volumen y los escenarios con restricciones de equipamiento en la siguiente fase del checklist.

**Conclusión:** la exportación local de 466 registros y el upstream oficial de 701 están contrastados, y la selección inicial se ha ampliado a 97 ejercicios con criterios explícitos. El tibial anterior queda como accesorio opcional, no como requisito de cobertura. Esto no cierra el P1: quedan las correcciones de metadatos integradas, la verificación real de medios y, sobre todo, la auditoría del generador/adaptador y sus escenarios.


### Disposición individual de los 39 pendientes no seleccionados

Estos registros no entran en la biblioteca inicial porque su función ya está cubierta por una opción más progresable, son variantes secundarias o pertenecen a otro tipo de entrenamiento. No se marcan como inseguros:

| ID | Decisión para el generador inicial |
|---|---|
| `sit-ups` | Cubierto por crunch en máquina/cable. |
| `rear-delt-fly` | Cubierto por reverse fly con mancuerna y máquina. |
| `db-fly` | Incorporado después de la evaluación de perfiles sin polea ni máquinas: aporta aislamiento de pecho con mancuernas. |
| `good-morning` | Bisagra más técnica; se priorizan peso muerto y RDL. |
| `sandbag-carry` | Transporte/condicionamiento, no prioridad de hipertrofia. |
| `dead-hang` | Isométrico pasivo de agarre, no una opción principal de progresión muscular. |
| `ez-bar-curl` | Variante redundante frente a curl con barra, polea, inclinado y predicador. |
| `bicep-curl` | Variante redundante frente a los curls seleccionados. |
| `reverse-curl` | Accesorio de antebrazo/braquial; se prioriza el curl martillo. |
| `close-grip-pull-ups` | Cubierto por dominadas y chin-ups. |
| `hanging-leg-raise` | Se priorizan elevaciones de rodillas colgado y silla romana, más escalables. |
| `db-shrug` | Se selecciona el encogimiento con barra; la variante con mancuerna queda como alternativa futura. |
| `back-extension` | Cubierto por extensión lumbar en máquina. |
| `ez-bar-lying-tricep-extension` | Redundante frente a skull-crusher y extensión por encima de la cabeza. |
| `decline-push-up` | Cubierto por flexión estándar e inclinada. |
| `ring-dips` | Variante avanzada e inestable; no es primera opción general. |
| `dips` | No priorizado frente a presses de pecho y aislamientos de tríceps. |
| `russian-twist` | Se priorizan Pallof y plancha lateral, con progresión más controlable. |
| `warrior-one` | Postura de yoga/equilibrio, fuera del generador de fuerza. |
| `kettlebell-halo` | Movilidad/prehabilitación de hombro, no trabajo principal de fuerza. |
| `planche-lean-floor` | Habilidad específica de calistenia. |
| `behind-the-neck-lat-pulldown` | Se descarta la variante tras nuca; se usa jalón al pecho. |
| `kettlebell-sumo-high-pull` | Tirón explosivo técnico, fuera de la primera biblioteca general. |
| `double-kettlebell-jerk` | Movimiento balístico/técnico de potencia. |
| `kettlebell-turkish-get-ups` | Habilidad compleja de cuerpo completo, no necesaria para la rutina general. |
| `hollow-body-hold` | Isométrico de core; menor prioridad que opciones cargables/escalables. |
| `dumbbell-windmill` | Movilidad y coordinación, no prioridad de hipertrofia. |
| `kettlebell-windmills` | Variante de movilidad/coordinación; misma decisión. |
| `sumo-deadlift` | Variante de peso muerto; ya hay peso muerto convencional, hex-bar y RDL. |
| `close-grip-bench-press` | Variante de énfasis en tríceps; se prioriza banca estándar y aislamientos. |
| `decline-bench-press` | Ángulo redundante frente a banca plana/inclinada y máquinas. |
| `seated-db-press` | Redundante frente a press de hombro con mancuerna y máquina. |
| `dumbbell-floor-press` | Redundante frente a press banca con mancuernas y máquina. |
| `barbell-pullover` | Variante redundante de aislamiento de dorsal. |
| `db-pullover` | Se prioriza jalón y jalón con brazos rectos, más fáciles de estandarizar. |
| `downward-dog-to-knee-drive` | Flujo de movilidad/condicionamiento, no fuerza progresiva principal. |
| `db-skull-crusher` | Redundante frente a skull-crusher y extensión overhead. |
| `landmine-single-arm-overhead-hold` | Isométrico de estabilidad, no prioridad de hipertrofia. |
| `toes-to-bar` | Habilidad avanzada de core; se priorizan elevaciones de rodillas. |
| `db-lunge` | Redundante frente a zancada inversa, búlgara y step-ups. |

### Disposición de los rechazados de fuerza

De los 58 registros rechazados de categoría fuerza, dos se incorporan con una decisión explícita y corrección (`cable-external-rotation` y `hammer-curl`). Los otros 56 quedan fuera de la biblioteca inicial por ser ejercicios de rehabilitación/activación, variantes redundantes de menor prioridad, habilidades avanzadas de calistenia, posiciones de yoga/Pilates o trabajo de potencia/condicionamiento. Esto incluye, entre otros, `banded-clamshell`, `bird-dog`, `sled-push`, `front-lever`, `human-flag`, `muscle-ups`, `handstand-hold`, `jefferson-curl`, `medicine-ball-slam`, `wall-ball` y `band-pull-apart`. El resto de rechazados son 89 ejercicios de estiramiento y 17 de cardio, ajenos al alcance de este generador.

La selección de 97 no pretende ser el catálogo universal de todo ejercicio útil. Define el conjunto inicial que el motor puede prescribir con reglas, progresión y equipamiento conocidos. Las alternativas fuera de alcance podrán incorporarse después de una revisión específica.


### Criterio de interpretación de déficits de volumen por músculo

La evaluación del generador no debe tratar todos los avisos de volumen como fallos equivalentes ni perseguir el cumplimiento mecánico de cada objetivo si eso empeora la rutina.

- **Prioridad principal de tren inferior:** cuádriceps y glúteos; se debe procurar una dosis razonable y sostenible en los escenarios compatibles.
- **Tríceps sural:** mantenerlo como grupo prioritario de cobertura directa, sin que ello obligue a añadir trabajo redundante cuando ya recibe una dosis adecuada.
- **Isquiotibiales:** grupo importante, pero con objetivo secundario y flexible en la primera versión del generador. Se debe incluir trabajo de bisagra de cadera y/o flexión de rodilla cuando exista una opción compatible y razonable; no es requisito alcanzar el objetivo nominal en todos los perfiles de equipamiento.
- Un déficit aislado de isquiotibiales en perfiles domésticos/restringidos **no bloquea la calidad ni el cierre de P1** si la rutina conserva los patrones básicos, una cobertura adecuada de cuádriceps y glúteos y no hay una alternativa de isquiotibiales que pueda añadirse sin sobrecargar otros grupos, exceder el tiempo o degradar la selección.
- No se deben introducir ejercicios de baja prioridad ni aumentar el volumen de glúteos/lumbar solo para hacer desaparecer el aviso. Los avisos deben seguir disponibles en el diagnóstico para que el profesional conozca la limitación.
- Esta jerarquía es un criterio de producto para la versión inicial, no una afirmación de que los isquiotibiales carezcan de importancia fisiológica ni una recomendación clínica universal.

Por tanto, los avisos de isquiotibiales se clasifican como **limitaciones aceptables/no bloqueantes** cuando cumplen las condiciones anteriores. Los déficits de cuádriceps, glúteos o tríceps sural sí requieren una revisión específica antes de considerar satisfactorios los escenarios afectados.
