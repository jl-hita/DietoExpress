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
