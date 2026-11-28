# Política de descargas (deload) — DietoExpress

## Evidencia

La ACSM Position Stand 2026 (Currier et al., DOI 10.1249/MSS.0000000000003897) no establece frecuencia ni protocolo de descarga. No atribuirle una regla cada 2, 3 o 4 semanas.

La evidencia específica es limitada:
- Bell et al. 2026, *A Practical Approach to Deloading* (DOI 10.1519/SSC.0000000000000910), describe como práctica habitual planificar descargas cada 4–8 semanas según ciclo y recuperación, y contempla el enfoque reactivo. Es una revisión práctica, no una prueba de intervalo óptimo.
- El consenso Delphi de Bell et al. 2023 (DOI 10.1186/s40798-023-00633-0) describe 4–6 semanas y unos 7 días como práctica habitual, pero reconoce la escasez de evidencia experimental y recomienda individualizar.
- Coleman et al. 2024 (DOI 10.7717/peerj.16777) estudió una semana de cese completo: no encontró ventaja hipertrófica y el grupo continuo mejoró más en fuerza. Cese completo no equivale a descarga activa.
- Un ensayo cruzado de 2026 en 19 hombres jóvenes no entrenados (DOI 10.1038/s41598-026-40612-5) no encontró diferencias significativas en hipertrofia ni resistencia muscular al reducir volumen/frecuencia en semanas 4 y 8. La muestra y población limitan la generalización.

## Criterio de producto

No programar una descarga universal cada 2–3 semanas ni convertir 4–8 semanas en mandato fisiológico. El intervalo 4–8 puede ser una referencia editable para planificación profesional, no una regla automática.

La descarga es una reducción temporal del estrés de entrenamiento, distinta de una reducción aislada de una serie, una modificación permanente del volumen, una pausa por lesión/enfermedad o un taper competitivo.

## Comportamiento y primera implementación

1. El profesional puede marcar explícitamente una fase del macrociclo como «descarga». El generador propaga esa decisión al contrato de generación; no infiere fatiga por calendario.
2. El flujo de generación de un bloque manual también permite marcar una descarga activa, limitada a un bloque de una semana.
3. La prescripción inicial reduce las series de cada ejercicio aproximadamente entre un tercio y la mitad (redondeo hacia arriba, mínimo una serie) y aumenta el RIR objetivo en dos puntos, con máximo de 5. Conserva selección de ejercicios, rangos de repeticiones y patrones. Las exposiciones que ya tienen una sola serie mantienen esa serie; la descarga actúa en ellas mediante el mayor margen de RIR y el informe lo explicita.
4. La vista previa de una descarga planificada informa de que es una heurística conservadora y requiere revisión profesional. Los valores no se atribuyen a ACSM ni se presentan como óptimos.
5. La descarga planificada no se activa por defecto ni por el mero número de semana. Existe además una vía reactiva separada, descrita a continuación, que solo propone/aplica una descarga cuando convergen señales persistentes de varias sesiones.
6. No decidir por una única sesión mala, un RPE global aislado ni falta de registros.
7. Dolor nuevo, agudo o creciente activa pausa y revisión de seguridad del ejercicio afectado; no una descarga global.
8. Falta de tiempo, equipo, enfermedad o motivación no son evidencia de fatiga fisiológica.
9. La vía reactiva por feedback es independiente de la descarga planificada, pero ambas solo modifican sesiones futuras planificadas sin registro. El historial completado no se modifica.

## Vía reactiva implementada y límites

La política global emite una propuesta reactiva solo si los tres últimos registros de sesión muestran al menos una señal de estrés cada uno y, en conjunto, aparecen al menos dos tipos distintos:
- recuperación insuficiente;
- RPE global de sesión >= 9/10;
- menos del 80 % de series completadas, únicamente si el motivo es Fatigue o no se indicó un motivo externo.

Un registro afectado por enfermedad no cuenta como sesión estresada. Las series omitidas por falta de tiempo, material, motivación u otro motivo externo no cuentan como segundo tipo de señal. Dolor nuevo o creciente tiene prioridad y devuelve pausa/revisión del ejercicio afectado, no una descarga.

La política emite `ProposeDeload`; el aplicador la ejecuta automáticamente solo si no se aplicó otra descarga en los 28 días anteriores. Aplica la mitad de series (redondeo conservador, mínimo una), RIR +2 y desactiva la progresión durante los próximos siete días. Solo toca sesiones futuras, planificadas y sin registro dentro de esa ventana (máximo 14); si no cambia ninguna, la propuesta queda pendiente. La aplicación queda auditada como automática, visible y reversible por el profesional.

El cooldown de 28 días es un intervalo mínimo entre descargas automáticas, no una orden de descargar cada cuatro semanas ni un calendario. Mientras una descarga está activa se bloquean otros ajustes automáticos de carga/volumen para evitar que se contradigan.

Las pruebas cubren la convergencia de señales, una sesión sana, prioridad del dolor, motivos externos, enfermedad, cooldown, bloqueo de otros ajustes y transformación de series/RIR. Siguen pendientes la vigilancia de falsos positivos/negativos con datos reales y la validación de la dosis en distintos perfiles; los umbrales son heurísticas internas, no protocolos demostrados como óptimos.
