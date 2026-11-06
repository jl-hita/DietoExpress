# Auditoría inicial del catálogo RepDB — 06/11/2026

## Fuente y alcance

- Fuente: exportación completa de `/api/exercise-catalog/audit-export`, generada el 06/11/2026 a las 11:17:56 UTC.
- La exportación declara `totalRecords = returned = 466` y `complete = true`.
- Se reconstruyó y validó el JSON completo de las 466 entradas del PDF; los 466 IDs son únicos y cada `sourcePayload.id` coincide con el ID exterior.
- Esta es una auditoría del catálogo de origen y de su idoneidad para el generador general de fuerza/hipertrofia. No equivale a una validación clínica ni a una revisión presencial de técnica por un profesional.

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

La selección explícita pasa de 84 a **91 ejercicios**. No se intenta importar indiscriminadamente los 466 registros: el generador recibe una biblioteca deliberadamente curada, progresable y con alternativas de equipamiento.

Composición de los 91 IDs frente al estado que tenían en la exportación:

| Estado en la exportación | Seleccionados |
|---|---:|
| approved | 48 |
| pending | 41 |
| rejected, con excepción y corrección explícita | 2 |
| **Total** | **91** |

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

Los 40 pendientes restantes no se incorporan al generador inicial: son variantes redundantes de ejercicios ya representados, habilidades/posiciones específicas o movimientos accesorios menos prioritarios para el alcance de esta primera biblioteca. Se mantienen fuera de la lista permitida; no se convierten automáticamente en ejercicios aprobados ni se presentan como clínicamente incorrectos. La lista permitida es la frontera efectiva de lo que puede seleccionar el generador.

De los 91 seleccionados, 89 tienen las dos imágenes `start` y `peak`; `dumbbell-farmers-walk` y `side-plank` solo tienen `main`. La presencia de una referencia en el JSON no demuestra todavía que el archivo remoto cargue correctamente ni que Android lo represente; esas comprobaciones siguen pendientes en sus fases específicas.

## Hallazgos anatómicos y de metadatos

- No existe ningún ejercicio con `tibialis_anterior` como músculo primario en los 466 registros. Solo aparece en dos asignaciones secundarias. El catálogo necesita un ejercicio propio de dorsiflexión/tibial anterior, con metadatos e imagen verificados; no se debe inventar un ID de RepDB.
- El catálogo de origen contiene 16 registros con romboides como músculo primario y 16 con trapecio como primario. La selección añade `shrug` y conserva remos con romboides/espalda alta, pero el volumen efectivo de espalda alta debe validarse en el generador, no inferirse por el número de ejercicios.
- Isquiotibiales aparecen como primarios en 44 registros; cuádriceps en 69. La biblioteca seleccionada incluye curls femorales, Nordic, bisagras, sentadillas, prensa y extensiones. Las advertencias de volumen del informe de generación no se resolverán simplemente acumulando variantes: habrá que revisar el asignador y las restricciones por escenario.
- Se detectaron tres diferencias entre la musculatura exterior curada y el payload original: `sit-ups`, `hex-bar-deadlift` y `cossack-squat`. Deben conservarse como correcciones explícitas trazables, no sobrescribirse silenciosamente con los datos externos.
- La exportación antigua contiene falsos positivos de patrón: `cable-reverse-fly`, `close-grip-barbell-curl`, `drag-curl` y `zottman-curl` aparecen como tracción horizontal; `single-leg-calf-raise` aparece como ejercicio unilateral; `crow-pose` se confunde con una tracción; y varias flexiones laterales se confunden con empujes. Las reglas actuales deben clasificarlas como accesorios o excluirlas cuando no sean ejercicios progresables adecuados.
- También hay inconsistencias de normalización entre variantes de remo (por ejemplo, músculos anatómicos sin normalizar frente a los grupos controlados `Lats`/`UpperBack`). La migración de refresco de metadatos automáticos de PR #503 debe integrarse con el bootstrap de PR #504 y comprobarse con tests; no se deben sobrescribir revisiones humanas.

## Criterios que siguen abiertos

1. Integrar y validar conjuntamente las correcciones de metadatos de PR #503 y el catálogo curado de PR #504.
2. Verificar en el informe de evaluación que los 91 ejercicios seleccionados son candidatos reales y que las imágenes/instrucciones se conservan tras importar.
3. Añadir un ejercicio propio de tibial anterior con anatomía, progresión, instrucciones e imagen correctas.
4. Comprobar la disponibilidad real de las imágenes y su visualización en portal y Android.
5. Revisar los avisos de volumen y los escenarios con restricciones de equipamiento en la siguiente fase del checklist.

**Conclusión:** la exportación de 466 registros está inventariada y la selección inicial se ha ampliado a 91 ejercicios con criterios explícitos. Esto no cierra el P1: quedan las correcciones de metadatos integradas, la carencia de tibial anterior, la verificación real de medios y, sobre todo, la auditoría del generador/adaptador y sus escenarios.


### Disposición individual de los 40 pendientes no seleccionados

Estos registros no entran en la biblioteca inicial porque su función ya está cubierta por una opción más progresable, son variantes secundarias o pertenecen a otro tipo de entrenamiento. No se marcan como inseguros:

| ID | Decisión para el generador inicial |
|---|---|
| `sit-ups` | Cubierto por crunch en máquina/cable. |
| `rear-delt-fly` | Cubierto por reverse fly con mancuerna y máquina. |
| `db-fly` | Cubierto por aperturas en polea y pec-deck. |
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

La selección de 91 no pretende ser el catálogo universal de todo ejercicio útil. Define el conjunto inicial que el motor puede prescribir con reglas, progresión y equipamiento conocidos. Las alternativas fuera de alcance podrán incorporarse después de una revisión específica.
