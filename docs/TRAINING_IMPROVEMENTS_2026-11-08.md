# Mejoras del generador de entrenamiento — 2026-11-08

Todo lo marcado «umbral interno» es una heurística de producto, no una cifra prescrita por ACSM ni otra guía verificada.

## 1. Progresión intrabloque (umbral interno)
- Bloques de ≥3 semanas: la semana 1 es de introducción (+1 RIR, máx. 5). En bloques de ≥5 semanas la semana 2 suma +0,5 RIR.
- Bloques de ≥4 semanas: en la semana 1 los ejercicios con ≥3 series pierden 1 serie (nunca bajan de 2).
- Las semanas siguientes mantienen la dosis asignada: esfuerzo y volumen suben a lo largo del bloque. No es una descarga ni sustituye `IsDeload`; las descargas siguen siendo siempre explícitas.
- Bloques de 1–2 semanas y descargas no se modifican.

## 2. Borrador editable de macrociclo (umbral interno)
- Nuevo `POST api/training/macrocycle-draft` (aditivo, rol Professional, **no persiste nada**). Entrada: `startDate`, `totalWeeks` (2–52), `goal`, `experience`, `includeDeloadPhases` (por defecto `false`), `deloadEveryTrainingWeeks` (4–8, solo si se piden descargas). Devuelve un cuerpo con la forma de `SaveTrainingMacrocycleRequest`, listo para revisar y guardar con `PUT plans/{planId}/macrocycle`.
- Fuerza intermedio/avanzado con ≥6 semanas: mitad acumulación, mitad intensificación. Resto: acumulación.
- Sin descargas salvo petición explícita; el intervalo 4–8 es una referencia de planificación editable, no una regla fisiológica. Nunca se coloca una descarga en la última semana.

## 3. Perfil de fuerza más específico (umbral interno)
- Solo objetivo fuerza, niveles intermedio/avanzado: el primer básico de cada sesión (sentadilla, bisagra, empujes o tracciones) se prescribe a 3–6 repeticiones con 240 s de descanso (antes 5–8 y 180 s).
- Principiantes y demás ejercicios (resto de básicos, accesorios) y otros objetivos no cambian. Estas cifras son una decisión de producto, no una pauta ACSM verificada.

## 4. Más splits y aviso de exposición semanal única
- Cuerpo completo admite ahora 2–4 días (A/B/C/D) y Push/Pull/Legs 3, 5 o 6 días (5 días = Empuje, Tracción, Pierna, Empuje B, Tracción B: la pierna queda 1 vez por semana y lo avisa el informe). Torso/Pierna (2 o 4) y Automático (2–4) no cambian. Las combinaciones no soportadas siguen devolviendo error de distribución.
- El aviso «grupo con estímulo efectivo en 1 sola sesión semanal» se extiende de fuerza a hipertrofia y salud general. Las vistas previas de rutina manual (objetivo `Manual`) no lo reciben, porque analizan sesiones aisladas. Es un aviso interno de producto (no bloquea; la referencia a «dos días por semana» no está verificada aquí).

## 5. Rotación de ejercicios curada (criterio de producto)
- Nuevo campo opcional `RotationIndex` (por defecto 0) en `TrainingProgramGenerationRequest`. Por patrón, el pool son como máximo los 3 mejores candidatos con prioridad dentro de 20 puntos de la mejor. Índice 0 = comportamiento anterior; cada índice superior empieza una posición más adelante en el pool.
- Los ejercicios no cambian dentro de un bloque (la progresión necesita continuidad); se rota entre mesociclos pasando un índice distinto. Un ejercicio claramente peor nunca entra solo por variedad.
- Pendiente: que el flujo de generación de mesociclo pase el índice automáticamente (p. ej. nº de mesociclos previos del plan).

## 6. Series de aproximación
- `TrainingExercisePrescription` gana `WarmupSets` (0–3) y `WarmupNote` (≤300) opcionales. Un ejercicio recibe 1–2 series muy fáciles (2 el primero de la sesión) solo si trabaja como principal un músculo que ningún ejercicio anterior de la sesión ha trabajado como principal. Ej.: press banca → aperturas (sin aproximación) → elevaciones laterales (con aproximación).
- No cuentan como series de trabajo ni en el volumen semanal; su tiempo se considera cubierto por la holgura fija de calentamiento/transiciones. Convención de producto, no cifra de guía.

## 7. Prescripción de pesos desde el historial (criterio de producto)
- `TrainingExercisePrescription` gana `TargetLoadKg` y `LoadBasis` (opcionales). `TrainingLoadEstimator` (puro) calcula la carga: si el último rendimiento estuvo dentro del rango con el esfuerzo previsto, **mantiene la carga**; si quedó por debajo/por encima del rango (o el rango cambió, p. ej. bloque de fuerza nuevo), estima un 1RM con la relación tipo Epley (aproximada, menos fiable a muchas repeticiones) y recalcula la carga para el inicio del nuevo rango al RIR objetivo, con cambio máximo del 10 % y redondeo al incremento mínimo (2,5 kg por defecto). Sin historial no se inventa ninguna carga.
- El generador acepta `LoadReferences` y prescribe pesos **solo en la semana 1**; las siguientes dependen de cómo vaya esa semana y las propone el motor de progresión. `generate-mesocycle` rellena `LoadReferences` automáticamente con el último rendimiento registrado del cliente.
- `RotationIndex` pasa a ser opcional: `generate-mesocycle` lo deriva del número de mesociclo (`SequenceNumber - 1`) salvo que se indique explícitamente.

## 8. Adaptación al registro del usuario (umbrales internos de producto)
Todas las acciones son propuestas; solo un profesional puede aprobarlas y nunca se modifica el historial.
- **Peso demasiado alto → `reduce_load`:** si en las dos últimas exposiciones con la misma carga no se completó el trabajo (series o mínimo del rango) por un motivo no externo, se sugiere una carga menor (estimación Epley acotada al 10 %, o −5 % si la estimación no baja). Nuevo motivo de serie omitida `load_too_heavy`. Dolor, recuperación insuficiente de sesión, descarga, tiempo, equipo, enfermedad o motivación nunca activan la reducción.
- **Recuperación insuficiente repetida:** al aprobar `review_and_reduce_training_stress` sin ejercicios concretos, se sube el RIR objetivo +1 (máx. 5) en las próximas tres sesiones, en vez de quedar sin efecto.
- **Reversión de recortes:** la reducción guarda la dosis original (`adaptationOriginalSets`). Tras tres exposiciones completadas sin señales de fatiga se propone `restore_volume` (+1 serie, sin superar la original).
- **Descarga reactiva (`propose_deload`):** solo propuesta, si las tres últimas sesiones muestran estrés y al menos dos tipos de señal (recuperación insuficiente, RPE ≥ 9, <80 % de series). Nunca se aplica sola.
- **Orden del historial:** las tres últimas sesiones se ordenan por fecha programada, no por fecha de edición.
- Migración idempotente V9 amplía el CHECK de acciones de propuesta. Los clientes antiguos ven acciones nuevas que no conocen: deben tratarlas como informativas.

## 9. Frontend (Angular)
- Los modelos y todos los puntos donde se copian ejercicios conservan ahora `warmupSets`, `warmupNote`, `targetLoadKg`, `loadBasis` y `adaptationOriginalSets` (antes se habrían perdido al guardar).
- Editor semanal: campo «Carga (kg)» editable y avisos de motivo de la carga y de aproximación. Vista previa: carga y series de aproximación.
- Generación: campo opcional «Rotación de ejercicios»; vacío = rotación automática por número de mesociclo.
- Macrociclo: «Generar propuesta» usa ahora el borrador del servidor; las descargas solo se incluyen si el profesional marca la casilla (antes siempre se añadía una fase de descarga final).
- Etiquetas para `restore_volume` y `propose_deload`. El texto de prescripción heredado incluye carga y aproximación.
- Splits: cuerpo completo 2–4 días y PPL 3, 5 o 6 en toda la validación.

## 10. Imágenes en nuestro backend e intercambio de ejercicio
**Imágenes.** Las apps ya no contactan con terceros. `exercise_images` (PostgreSQL, bytes) guarda una fila por (ejercicio, posición): 0 = inicio/principal, 1 = final. Fuentes: `repdb-free` (copia diferida, de una en una, la primera vez que un ejercicio prescrito la necesita, desde el host fijo de RepDB usando la ruta del catálogo; valida tipo por bytes, máx. 2 MB; sin copia masiva ni endpoint de listado/exportación) y `custom` (subida por el propietario de una aportación profesional o superadmin; WebP/PNG/JPEG ≤ 2 MB), que permite imágenes de ejercicios que no son de RepDB.
- Paciente: `GET api/portal/training/sessions/{id}/exercise-images` (posiciones por ejercicio + atribución) y `GET api/portal/training/exercise-images/{exerciseId}/{slot}` (solo ejercicios prescritos a ese paciente).
- Profesional: `GET api/exercise-catalog/{id}/images[/{slot}]`, `PUT`/`DELETE api/exercise-catalog/{id}/images/{slot}` (propietario o superadmin). Angular usa ya este endpoint.
- Licencia RepDB Free Tier (leída en LICENSE-DATA.md): uso dentro de aplicaciones con atribución visible y sin redistribución como dataset o API. Servirlas solo a usuarios autenticados, una a una y con atribución es nuestra interpretación de «uso en la app»; si se quiere certeza, consultar con support@repdb.co.

**Intercambio de ejercicio (solo esa sesión).**
- `GET api/portal/training/sessions/{sessionId}/exercises/{exerciseId}/alternatives[?equipment=a,b]`: mismo patrón primero (ordenado por solapamiento muscular y prioridad; marca los del pool de rotación); si no hay, ejercicios que entrenan los mismos músculos principales; si tampoco, mensaje explícito de que no existe alternativa revisada. Respeta objetivo, nivel mínimo y material (si se indica); solo ejercicios aprobados.
- `POST .../swap` con `{replacementExerciseId}`: exige que sea una alternativa válida, solo en sesiones próximas sin registro, mantiene series/repeticiones/RIR/descanso, elimina carga y calentamiento del ejercicio anterior y guarda el origen en `swappedFromExerciseId` y una nota en las instrucciones de la sesión para que el profesional lo vea.

## 11. Imágenes: carga al arrancar, semilla de despliegue y huecos
- **Arranque:** tras el upsert del catálogo (RepDB + ejercicios añadidos + correcciones), el servidor importa la semilla de imágenes si existe (`ExerciseImages:SeedPath`, por defecto `Anguloso.Server/seed/exercise-images-seed.jsonl.gz`) y, ya en segundo plano (`ApplicationStarted`), copia una a una las imágenes de RepDB que falten (100 ms entre peticiones, se detiene tras 10 fallos seguidos). No bloquea ni hace fallar el arranque.
- **Semilla para otros servidores:** un superadmin descarga `GET api/exercise-catalog/images/seed` (gzip JSON-lines con checksum SHA-256 y bytes) y la deja en la ruta de semilla del servidor destino. Al importar se valida tamaño, tipo por bytes y checksum, y nunca se sobrescriben filas existentes. El fichero está en `.gitignore` y **no se versiona** (licencia RepDB: uso en la app, sin redistribuir como dataset).
- **Huecos:** `GET api/exercise-catalog/images/missing` (superadmin) lista los ejercicios aprobados sin ninguna imagen (hoy, los ejercicios añadidos por DietoExpress, que no vienen de RepDB); se completan con `PUT api/exercise-catalog/{id}/images/{slot}`.
- **Imágenes generadas con IA «con la estética de RepDB»:** no se hacen. La licencia (término 5) prohíbe usar sus imágenes como referencia o condicionamiento para modelos generativos y considera esos resultados derivados que no pueden usarse en aplicaciones.

## 12. Reajuste automático de sesiones futuras (sin profesional)
Decisión de producto: el sistema aplica por sí mismo los ajustes pequeños, acotados y reversibles tras cada registro (`TrainingAutoAdaptationPolicy` + `TrainingAutoAdaptationApplier`). Solo cambian sesiones **futuras, planificadas y sin registro** de un plan activo; el historial nunca se toca.
- **Se aplica solo:** −1 serie en los ejercicios con fatiga repetida (3 sesiones por ejercicio); RIR +1 en las próximas 3 sesiones si la baja recuperación se repite sin culpable claro; recuperar 1 serie quitada antes (sin superar la dosis original); descarga conservadora de los próximos 7 días con estrés combinado (mitad de series, RIR +2, sin progresión; una vez cada 28 días); mantener sin progresión las próximas sesiones; y **escribir la carga** (bajada o subida) del motor de progresión en cada sesión próxima.
- **Nunca se aplica solo:** dolor/seguridad (pausa la progresión y espera revisión) y reorganización de sesiones.
- **Salvaguardas:** enfriamiento por tipo de ajuste (14 días; descarga 28; mantener 7), una descarga en curso absorbe otras reducciones, todo en una transacción y un fallo deja la propuesta pendiente sin afectar al registro del paciente.
- **Trazabilidad:** cada ajuste queda como propuesta `approved` sin revisor y con nota `auto:<tipo>`, y una nota en las instrucciones de la sesión; el profesional puede revisarla o deshacerla. La aprobación manual sigue disponible para lo que queda pendiente.

## 13. Valoración tras cada ejercicio (aplicada por el sistema)
Tras cada ejercicio con series hechas, la app pregunta (opcional, 3 toques): estímulo (poco/adecuado/mucho), molestias (ninguna/leves/fuertes) y cómo estaba el músculo al empezar (recuperado/aún cargado). Se guardan en el feedback del ejercicio (`stimulus`, `discomfort`, `preFatigue`) y `TrainingMuscleFeedbackAdapter` decide **por músculo**:
- Molestias fuertes en la última exposición → pausa la progresión del ejercicio y queda pendiente de revisión (no se automatiza).
- Molestias leves en 2 de las últimas 3 → sugerencia de cambiar de ejercicio (informativa).
- Poco estímulo en las 2 últimas sesiones del músculo, siempre recuperado, sin molestias y con volumen semanal por debajo del techo (objetivo + tolerancia del generador) → **+1 serie** (al ejercicio con menos series, máx. 5), aplicada sola en una sesión de la próxima semana.
- Estímulo alto y «aún cargado» en ≥2 de las últimas 3 → **−1 serie** (mínimo 1), aplicada sola.
- Respuestas ausentes nunca cuentan como evidencia y una sola sesión nunca decide; enfriamiento de 14 días por tipo de cambio. Todo es umbral interno de producto, no de guía.
- Migración idempotente V10 amplía las acciones de propuesta (`add_muscle_set`, `reduce_muscle_set`, `suggest_exercise_swap`).
