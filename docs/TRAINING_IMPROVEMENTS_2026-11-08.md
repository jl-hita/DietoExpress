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
