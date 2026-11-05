# P1 — algoritmo de progresión por ejercicio

## Objetivo y alcance

La progresión se calcula por **ID estable de ejercicio**, dentro del mismo plan y a partir de series efectivamente ejecutadas. La salida es una recomendación para la siguiente exposición; nunca modifica una prescripción ni reescribe sesiones completadas.

La progresión doble (primero repeticiones dentro de un rango y después carga) se aplica solo cuando la prescripción declara una regla compatible. Las sesiones antiguas sin RIR o sin series estructuradas no se reinterpretan como éxitos.

## Evidencia y límites

Referencia principal:

- Currier BS et al. (2026), *Resistance Training Prescription for Muscle Function, Hypertrophy, and Physical Performance in Healthy Adults: An Overview of Reviews*, ACSM Position Stand, MSSE 58(4):851–872. DOI: https://doi.org/10.1249/MSS.0000000000003897.
- La revisión de ACSM sintetiza 137 revisiones sistemáticas y más de 30.000 participantes. Apoya el entrenamiento progresivo y reconoce que el esfuerzo próximo al fallo (por ejemplo, 2–3 RIR) puede proporcionar estímulo suficiente; no establece un umbral universal de RIR ni una fórmula universal para los saltos de carga.
- La propia revisión advierte que las relaciones dosis-respuesta y los detalles de protocolos no pueden deducirse de resúmenes de variables aisladas. Por ello, los umbrales operativos que siguen son reglas conservadoras de ingeniería, no leyes fisiológicas demostradas.

## Reglas implementadas

1. **Identidad:** solo se combina el historial del mismo `plan_id` y `exercise_id`. No se mezclan ejercicios con nombres parecidos, otros planes ni versiones de prescripción ajenas.
2. **Series válidas:** solo cuentan series `working` completadas. Las series de calentamiento no cuentan. Cada serie efectiva debe tener carga positiva, repeticiones y estado de finalización válidos.
3. **Rango:** si las series quedan dentro del rango pero aún no alcanzan el máximo, se mantiene la carga y se propone como máximo una repetición adicional por serie, sin superar el límite prescrito.
4. **RIR:** si existe un RIR objetivo, todas las series efectivas deben tener RIR registrado y no quedar por debajo de ese objetivo. RIR desconocido no equivale a RIR suficiente.
5. **Confirmación:** alcanzar el máximo del rango una sola vez no dispara una subida. Se requieren dos exposiciones consecutivas completadas en el máximo del rango, con el RIR objetivo, misma carga, recuperación no marcada como insuficiente y sin señal de dolor.
6. **Carga:** la prescripción admite un incremento mínimo de material (`SmallestLoadIncrementKg`) y un techo por ejercicio (`MaximumLoadIncreasePercent`, opcional, máximo configurable del 10 %). Si no se configura un techo, se aplica un 5 % operativo conservador. Solo se recomienda el incremento mínimo si no supera el techo de ese ejercicio. Si se desconoce el incremento del material, no se inventa un peso exacto: se indica usar el menor salto disponible que no supere el techo. El 5 % por defecto es una salvaguarda de ingeniería, no un umbral clínico ni una recomendación de ACSM.
7. **Seguridad/contexto:** dolor reciente, recuperación insuficiente, sesión no completada, series incompletas o rendimiento bajo bloquean la progresión automática. No se reduce carga automáticamente: se mantiene y se recomienda revisar técnica, descanso, fatiga y contexto con el profesional.
8. **Cargas distintas:** no se promedian series con cargas distintas. Back-off sets, dropsets y prescripciones con carga variable requieren un modelo explícito posterior; de momento se devuelve dato no válido para una progresión única.

## Por qué se requieren dos exposiciones en el máximo

La evidencia no determina un número universal de sesiones para confirmar una subida. Se eligen dos exposiciones consecutivas como salvaguarda contra fluctuaciones diarias, estimaciones imprecisas de RIR y ruido de registro. El coste es retrasar ligeramente algunas subidas; el beneficio es evitar saltos basados en una sola sesión atípica.

## Datos que faltan para mayor personalización

- Incremento mínimo de carga específico de la máquina/barra/mancuerna disponible en cada ejercicio. El contrato admite `smallestLoadIncrementKg` y `maximumLoadIncreasePercent`, pero los generadores/editores aún no solicitan esos valores; mientras no estén disponibles, el motor no inventa un peso y usa el techo predeterminado del 5 %.
- Historial de molestias vinculado al ejercicio concreto, no solo al contexto de sesión.
- Variantes de prescripción con cargas distintas por serie, top sets/back-off y progresiones por RIR o velocidad.
- Mejor calibración individual del RIR y del incremento tolerable. No se debe inferir un 1RM exacto a partir de una sola serie.

## Versionado y trazabilidad

El motor es una función pura y determinista. Sus acciones y explicaciones se devuelven por API para que Android pueda mostrarlas sin recalcular reglas. Cambios futuros en umbrales o semántica deben actualizar tests y este documento; si alteran recomendaciones históricas, se debe versionar el motor y no reinterpretar silenciosamente los registros pasados.
