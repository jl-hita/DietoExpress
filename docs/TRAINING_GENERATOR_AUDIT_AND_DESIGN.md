# Auditoría y diseño del generador adaptativo de entrenamiento

Estado: especificación inicial; todavía no equivale a un motor implementado ni validado fisiológicamente.
Issue de seguimiento: https://github.com/jl-hita/DietoExpress/issues/415

## 1. Hallazgos del código existente

### Planificación profesional
- `TrainingPeriodizationController` administra perfiles, planes, mesociclos, microciclos, sesiones y plantillas.
- `training_sessions.exercise_prescription` y `training_session_templates.exercise_prescription` conservan el texto libre por compatibilidad. Desde la migración `training-periodization-v6-structured-prescriptions`, las sesiones generadas también guardan `training_sessions.exercise_prescription_data` como JSONB: versión del motor, objetivo, semana/día, duración y ejercicios con ID/versión del catálogo, series, rango de repeticiones, RIR, descanso, progresión y grupos musculares. Las ediciones manuales que cambian el texto invalidan el JSON estructurado para evitar datos obsoletos; los cambios de fecha/título conservan la prescripción estructurada. Las plantillas de plan y las plantillas individuales de sesión conservan el JSON estructurado al guardarse/aplicarse; si el profesional edita el texto de la prescripción, el borrador descarta la estructura para evitar inconsistencias.
- La planificación actual está orientada a que el profesional gestione el calendario; no se ha encontrado aún un motor que genere y optimice un ciclo entero desde el perfil del cliente.
- Ya existe una prueba de regresión estática en `Tests/DietoExpress.Security.Tests/TrainingPeriodizationRegressionTests.cs`, que protege parte del contrato actual.

### Biblioteca de ejercicios: discrepancia detectada
- La UI de planificación declara un modelo `RepDbExercise` con `primary_muscles`, material, región corporal, categoría, objetivos, instrucciones e imágenes. Por tanto, **sí existen grupos musculares en el JSON externo de la biblioteca**, al menos como músculos principales.
- `loadExpandedExerciseCatalog()` descarga `https://exercise-dataset.com/exercises.json` directamente en el navegador. El tipo actual no declara `secondary_muscles`; cuando falta `primary_muscles`, la UI usa `body_part` como alternativa. La atribución visible menciona RepDB.
- **En el flujo revisado no se observa que ese catálogo ampliado se importe o persista en PostgreSQL.** El generador del servidor recibe un catálogo de candidatos suministrado explícitamente; no lee automáticamente el JSON que consume Angular.
- La clasificación actual de músculos, objetivos y material incluye heurísticas derivadas de texto. Es útil para filtros de interfaz, pero no basta por sí sola como metadato de confianza para un generador de entrenamiento. No se deben convertir los patrones de movimiento en datos de ejercicios reales sin revisión.
- Antes de la selección automática, debemos decidir si se importa/sincroniza un catálogo curado al backend o si se crea un contrato de catálogo compartido. No copiar masivamente el catálogo externo sin comprobar sus términos, atribución y condiciones de redistribución.
- El catálogo que consuma el motor necesita identificadores estables, procedencia, versión, material normalizado, músculos principales/secundarios revisados, patrones de movimiento, dificultad técnica y alternativas compatibles. Los metadatos ausentes deben permanecer desconocidos, no inferirse como hechos.

### Seguimiento del paciente y Android
- `GET /api/portal/training/sessions` ya expone sesiones al paciente autenticado.
- `POST /api/portal/training/sessions/{sessionId}/record` guarda duración, RPE y notas, con upsert por sesión/paciente. No registra explícitamente si la sesión fue completa, parcial, omitida por tiempo, limitada por fatiga o detenida por molestias.
- La app Android ya incluye consulta de entrenamientos y el documento `DietExpress.Android/docs/OFFLINE_ENDPOINT_AUDIT.md` trata las sesiones como datos de lectura cacheada. No se debe añadir un envío offline a una ruta nueva hasta diseñar idempotencia, conflictos, fecha de la sesión y comportamiento en cambio de cuenta.
- Los check-ins generales del portal no sustituyen un registro específico por sesión: la adaptación necesita relacionar la señal con una sesión, ejercicios y semana concretos.

## 2. Arquitectura propuesta

Separar cinco responsabilidades, con contratos versionados:

1. **Catálogo normalizado**: ejercicio y metadatos revisados; procedencia y nivel de confianza.
2. **Perfil de programación**: objetivo, experiencia, días disponibles, duración máxima, equipamiento, preferencias, restricciones y disponibilidad semanal.
3. **Generador determinista**: produce un plan completo con sesiones, ejercicios, series, repeticiones, descansos, objetivo de esfuerzo y progresión por semana.
4. **Registro de ejecución**: preserva lo programado y guarda lo realizado y el feedback del cliente.
5. **Adaptador**: calcula cambios únicamente en sesiones futuras y explica cada cambio. El historial completado nunca se reescribe silenciosamente.

La misma versión del motor, con los mismos datos y opciones, debe producir el mismo plan. La IA generativa puede explicar decisiones o sugerir candidatos, pero no debe eludir restricciones ni inventar ejercicios/metadatos.

## 3. Contrato de entrada mínimo

- Objetivo primario y secundarios (p. ej., hipertrofia, fuerza general, salud).
- Nivel de experiencia, con reglas explícitas para clasificarlo.
- Días semanales disponibles y restricciones de calendario.
- Duración máxima por sesión.
- Equipamiento realmente disponible.
- Preferencias y ejercicios excluidos por el usuario/profesional.
- Limitaciones relevantes declaradas y estado de revisión profesional cuando sea necesario.
- Duración del ciclo y fecha de inicio.
- Historial de entrenamiento disponible; cuando no exista, iniciar con supuestos conservadores y declararlos.

Si faltan datos esenciales, la API debe pedirlos o devolver una validación explicable; no rellenarlos silenciosamente.

## 4. Representación estructurada de una prescripción

Cada ejercicio programado necesita al menos:
- ID estable del ejercicio del catálogo y versión/metadatos usados.
- Grupos musculares principales y secundarios con vocabulario controlado y procedencia/revisión explícitas. No asumir que los secundarios existen en el JSON externo.
- Orden dentro de la sesión.
- Series objetivo y rango de repeticiones (o duración/distancia para modalidades apropiadas).
- Objetivo de esfuerzo (RIR/RPE) y descansos.
- Regla de progresión y límites de carga/repeticiones.
- Sustituciones compatibles previamente validadas.
- Notas e indicaciones de ejecución.
- Razón breve de selección para auditoría.

La cadena de texto heredada debe seguir mostrándose para planes existentes, pero no debe ser la fuente de verdad del nuevo motor.

## 5. Feedback de la app cliente

La interfaz debe ser breve y accesible, permitiendo registrar:

- Resultado: completa, parcial, no realizada.
- Motivo si no se completa: falta de tiempo, fatiga/esfuerzo excesivo, material/instalación, falta de motivación, enfermedad u otro.
- Esfuerzo global (RPE) y, cuando tenga sentido, RIR/rendimiento por ejercicio.
- Recuperación antes de la siguiente sesión: buena, parcial, insuficiente.
- Fatiga muscular/general y sueño opcional.
- Molestias/dolor y zona afectada, con opción de parar y avisar al profesional.
- Comentario opcional.

No se debe equiparar falta de tiempo a mala recuperación. Dolor nuevo, agudo o creciente activa una regla de seguridad: no recomendar continuar simplemente reduciendo la carga; detener o sustituir la actividad implicada y recomendar valoración profesional según el caso.

## 6. Política de adaptación inicial

- **Una sesión omitida por tiempo**: no reducir automáticamente la capacidad ni aumentar el volumen de las sesiones siguientes para “recuperarla”. Reorganizar únicamente si quedan días disponibles y los descansos siguen siendo adecuados.
- **Sesión parcial por fatiga o esfuerzo inesperado**: registrar qué se completó; comprobar tendencia y contexto antes de cambiar el plan.
- **Recuperación insuficiente repetida**: reducir temporalmente el estímulo pertinente (volumen/intensidad/frecuencia) de forma acotada y explicable.
- **Rendimiento estable y objetivo de esfuerzo cumplido**: progresar según la regla prescrita, por ejemplo aumentar repeticiones dentro del rango antes de subir la carga.
- **Estancamiento**: no inferirlo por una sola sesión; definir una ventana mínima y comprobar adherencia, técnica, descanso y cambios de ejercicio.
- **Molestias/dolor**: aplicar la regla de seguridad, no una optimización de rendimiento.
- **Semana de descarga**: basarla en señales y en el diseño del ciclo; no insertarla mecánicamente cada cuatro semanas para todo el mundo. La primera implementación técnica descrita en §9.1 usa, solo como heurística provisional del borrador, un patrón configurable pendiente de revisión (base, construcción, construcción, descarga). No debe activarse como prescripción universal ni para pacientes sin revisión profesional.

Los umbrales numéricos exactos se deben declarar como reglas iniciales configurables, justificar y validar; no presentarlos como límites biológicos universales.

## 7. Batería de pruebas exigida

### Unitarias y propiedades
- Validación de entrada y restricciones imposibles.
- Reproducibilidad del generador.
- Calendario: número de sesiones, días disponibles, duración máxima y separación entre sesiones.
- Cobertura muscular/patrones y ausencia de duplicados no deseados.
- Material y ejercicios excluidos.
- Límites de series/repeticiones/RIR, descansos y progresión.
- Ninguna progresión automática si faltan datos de rendimiento.
- Nunca reescribir sesiones pasadas al adaptar el futuro.
- Distinguir sesión omitida por tiempo de fatiga, baja recuperación o dolor.
- Dolor: bloquear recomendaciones de “continuar con menos peso” como respuesta automática genérica.
- Adaptaciones idempotentes: repetir el mismo evento no acumula reducciones ni genera versiones duplicadas.

### Escenarios integrales
- Principiante con 2 días y poco material.
- Persona intermedia con 3 días.
- 4 días con prioridades de tren superior/inferior.
- 5–6 días, controlando redundancia y recuperación.
- Sesiones incompletas, semanas perdidas y retorno tras interrupción.
- Feedback contradictorio o incompleto.
- Cambios de disponibilidad/equipamiento a mitad del ciclo.
- Sin ejercicios compatibles en el catálogo.
- Cambio de cuenta, reintento tras timeout y sincronización offline.

### Revisión de resultados
Crear una matriz reproducible de perfiles sintéticos y reportar por versión: errores de validación, planes rechazados, violaciones de invariantes, distribución de volumen, cobertura, duración estimada y decisiones de adaptación. La generación masiva sirve para detectar fallos lógicos, no para demostrar eficacia clínica. El informe JSON de evaluación calcula series principales por músculo y por semana, además de exposiciones secundarias; estas últimas no se equiparan a series efectivas. La matriz actual usa un catálogo sintético con mapeos genéricos por patrón para probar el cálculo, no un catálogo clínico real.

## 8. Validación científica y límites

Las reglas deben enlazar a evidencia identificable, registrar la fecha de revisión, distinguir evidencia sólida de decisiones pragmáticas y describir sus límites. Las guías/revisiones científicas ayudan a fijar buenos puntos de partida, pero no establecen un único programa óptimo para todos.

La calidad del software se evalúa mediante CI, invariantes, pruebas de integración y revisión de programas de referencia. La eficacia fisiológica requiere además revisión por un profesional cualificado y, con el tiempo, resultados reales. No se debe afirmar equivalencia con un entrenador experto solo porque el CI esté verde.

## 9. Secuencia de implementación

1. Terminar el rastreo de origen y metadatos de la biblioteca actual.
2. Acordar el modelo de prescripción estructurada y su migración compatible. **En curso:** existe un contrato C# tipado y un validador puro para invariantes estructurales; todavía no hay persistencia ni endpoint asociado.
3. Implementar motor puro y determinista, separado de controladores y persistencia.
4. Construir la batería de tests antes de activar generación a pacientes. **Añadidos en esta iteración:** tests del contrato estructurado, límites de dosis, identidad/versionado del catálogo y calendario.
5. Añadir persistencia de feedback y adaptaciones con auditoría e idempotencia.
6. Revisar el contrato y el flujo Android en el mismo esfuerzo coordinado.
7. Habilitar primero como borrador para profesionales; no autoaplicar cambios sensibles sin controles.
8. Someter programas de referencia a revisión profesional antes de publicitarlo como generador validado.

### 9.1 Contrato estructurado, validador y primera versión del generador
`TrainingProgramPrescription` representa el plan con versión del motor, objetivo, fecha de inicio, duración y sesiones. Cada sesión tiene semana/día y duración estimada; cada ejercicio conserva ID estable, versión del catálogo y nombre snapshot, orden, series, rango de repeticiones, RIR objetivo, descanso, regla de progresión y motivo de selección.

`TrainingPrescriptionValidator` rechaza contratos incompletos o inconsistentes antes de persistirlos o mostrarlos. `TrainingProgramGenerator` es una primera implementación pura y determinista (motor 0.5.0): recibe un catálogo explícito de candidatos con metadatos revisados, objetivo compatible, patrón de movimiento, nivel mínimo, material, prioridad curada y grupos musculares principales/secundarios; copia estos grupos a cada prescripción para conservar una instantánea auditable. Valida que haya al menos un músculo principal y que las listas no tengan valores inválidos, duplicados ni solapamientos. Genera semanas completas para 2–4 días semanales y devuelve errores explicables si falta un ejercicio compatible. No infiere atributos desde el nombre y no modifica la base de datos ni publica planes automáticamente.

Las prescripciones iniciales (series, repeticiones, RIR y descanso) son parámetros provisionales de un borrador de software, no recomendaciones individualizadas validadas ni prueba de eficacia. La primera periodización técnica repite un microciclo de cuatro semanas: semana base, dos semanas de construcción con una serie adicional (máximo cuatro), y semana de descarga con una serie menos y un RIR más; solo se aplica la descarga cuando se alcanza esa cuarta semana. Es una regla de ejemplo para demostrar cambios deterministas por semana, no una estrategia universal. Antes de activarlo para pacientes se debe hacer configurable por el profesional y contrastarlo con objetivo, experiencia, tolerancia, feedback y evidencia. Esta versión no cubre aún todos los perfiles, periodizaciones ni 5–6 días, y el tiempo estimado es una aproximación que deberá contrastarse. Las siguientes iteraciones deben añadir escenarios de cobertura y distribución de volumen, reglas por objetivo/nivel con evidencia documentada, control de fatiga y adaptación futura sin reescribir el historial. Los tests comprueban determinismo, días/semanas, exclusiones, material/objetivo, metadatos revisados, ciclo semanal y validación del plan.

### 9.2 Registro estructurado de feedback y recomendación conservadora

La migración idempotente V5 añade a `training_session_records` el resultado de la sesión, motivo de no finalización, recuperación declarada, señal de dolor y cumplimiento del objetivo de progresión. Los registros anteriores conservan esos campos a NULL; no se inventa feedback histórico. Las restricciones de PostgreSQL y las validaciones HTTP limitan los valores aceptados.

El portal web del paciente ya permite enviar ese check-in y muestra la recomendación calculada con los tres registros más recientes del plan activo. Una sesión no realizada por falta de tiempo no se contabiliza como entrenamiento completado ni recibe duración/RPE inventados. La consulta de feedback se ordena por última actualización para que una sesión omitida, sin `completed_at`, no desaparezca del historial reciente.

La política solo devuelve una recomendación explicada: no modifica automáticamente sesiones, cargas, series ni el plan profesional. Los cambios futuros requieren revisión y aplicación explícita por un profesional. El endpoint existente mantiene compatibilidad: los campos nuevos del cuerpo son opcionales y los clientes antiguos que no los envían no borran el feedback estructurado guardado. La respuesta GET añade campos, sin cambiar ni eliminar los anteriores.

**Compatibilidad Android revisada:** el endpoint sigue siendo `GET /api/portal/training/sessions` y `POST /api/portal/training/sessions/{sessionId}/record`; no se cambian rutas, autenticación ni campos antiguos requeridos. Android ya permitía registrar duración/RPE/notas online; la PR `jl-hita/DietoExpress.Android#41` añade también el check-in estructurado y muestra la recomendación adaptativa. Los campos nuevos del request son opcionales y la respuesta solo añade campos, por lo que los clientes antiguos siguen siendo compatibles. No se habilita escritura offline: la app envía estos registros online hasta contar con pruebas de idempotencia, reintentos tras timeout y resolución de conflictos.

## 10. Condición de compatibilidad Android

Todo PR que modifique rutas, DTO, autenticación, permisos, fechas o reglas de negocio debe declarar impacto en `jl-hita/DietoExpress.Android`, conforme a `docs/API_COMPATIBILITY.md`. No encolar feedback offline hasta que la semántica de fecha, idempotencia y conflictos esté definida y probada.


## 11. Backlog posterior de catálogo y contenido multimedia (prioridad baja)

Estos trabajos son importantes para enriquecer la biblioteca, pero **no deben retrasar** el motor de planificación, su batería de pruebas, la progresión ni el registro/adaptación del feedback del cliente.

### 11.1 Catálogo de ejercicios administrable en PostgreSQL
- Sustituir la dependencia de ejecución del catálogo JSON descargado en el navegador por un catálogo de ejercicios persistido en la base de datos y servido por la API.
- Crear un proceso de importación inicial controlado, con identificadores estables, procedencia, atribución/licencia, versión de origen y detección de duplicados.
- Permitir ampliar la biblioteca con ejercicios nuevos mediante una interfaz de administración para profesionales autorizados (y, si se decide, importación validada). No permitir que un usuario sin permisos altere el catálogo global.
- Definir un flujo de revisión antes de publicar ejercicios nuevos o modificados y preservar compatibilidad con las prescripciones históricas.
- No importar ni redistribuir datos del catálogo externo hasta verificar sus términos de uso, licencia y requisitos de atribución.

### 11.2 Imágenes y otros medios
- No almacenar por defecto imágenes o vídeos binarios como grandes blobs en PostgreSQL. Valorar almacenamiento de objetos/archivos con URL estable, metadatos en PostgreSQL y política de copias de seguridad.
- Comparar alternativas compatibles con el despliegue actual: almacenamiento local persistente con backups y entrega por nginx, o almacenamiento de objetos compatible con S3. Decidir en función de coste, simplicidad operativa, permisos, caché y tamaño.
- Guardar en la base de datos la referencia al medio, tipo, texto alternativo, autor/procedencia, licencia, fecha de revisión y estado de publicación. Definir qué ocurre si un recurso externo deja de estar disponible.
- La solución elegida debe evitar rutas absolutas locales expuestas al cliente, medios huérfanos y enlaces que revelen credenciales.

### 11.3 Vídeos tutoriales de técnica
- Valorar vídeos breves que muestren ejecución correcta, recorrido, respiración, errores comunes y variantes/regresiones.
- Priorizar vídeos propios o con licencia explícita para reutilización; no descargar ni redistribuir material ajeno sin permiso.
- Para una primera iteración, puede bastar enlazar a un recurso fiable aprobado, guardando proveedor, URL, idioma, licencia/procedencia y fecha de verificación. La reproducción embebida debe respetar privacidad, disponibilidad y condiciones del proveedor.
- La selección del vídeo no sustituye instrucciones escritas ni revisión profesional de los ejercicios.

### Criterio de orden
Completar primero el motor determinista y sus tests, el modelo estructurado de prescripción, el feedback del cliente y las adaptaciones futuras seguras. Abordar catálogo administrable, almacenamiento de imágenes y vídeos tutoriales después, como trabajo de prioridad inferior.


### Catálogo persistente de ejercicios (RepDB)

- El catálogo de RepDB se sincroniza a PostgreSQL en `exercise_catalog` al arrancar por primera vez si la tabla está vacía. La operación de superadmin `POST /api/exercise-catalog/sync` permite actualizarlo manualmente; las búsquedas de la aplicación usan `GET /api/exercise-catalog`.
- Cada registro conserva el objeto de origen completo en `source_payload JSONB`, además de columnas consultables para nombres, descripciones, instrucciones y consejos por idioma, categoría, tipo de fuerza, mecánica, dificultad, material, región corporal, músculos primarios/secundarios, objetivos, etiquetas, MET, unilateralidad, peso corporal e imágenes. El upsert mantiene las anotaciones de revisión ya existentes.
- La UI ya no descarga el JSON directamente desde el navegador. La atribución de RepDB sigue visible en la biblioteca. El endpoint de consulta no devuelve el payload bruto ni permite solicitar más de 100 resultados por petición.
- Cada ejercicio tiene `author` (por defecto `superadmin` para el catálogo preexistente) y `owner_user_id` para aportaciones profesionales. El catálogo normal devuelve los ejercicios de superadmin, los aprobados globalmente y los pendientes del profesional autenticado; los pendientes de otros profesionales y los rechazados no se exponen para uso manual. La cola de revisión de superadmin puede consultar todos los autores.
- Los metadatos originales de músculos se conservan sin alteración. Una selección explícita de ejercicios tiene además anotaciones musculares y de patrón revisadas por DietoExpress, separadas de los datos originales; solo esa selección puede considerarse candidata para el generador. La lista inicial cubre sentadilla, bisagra de cadera, empuje y tracción horizontal/vertical, trabajo unilateral y core. Los IDs que no existan en una revisión concreta del catálogo simplemente no recibirán anotación.
- La evaluación estructural de 27 escenarios usa ahora ejercicios reales de una selección curada, no ejercicios sintéticos que asignaban músculos por patrón. El informe no acredita por sí mismo la idoneidad clínica ni demuestra que el volumen sea óptimo; todavía hay que validar los resultados y ampliar la revisión de la biblioteca antes de tratar el catálogo completo como apto para generación automática.
- Licencia y atribución: RepDB Free Tier permite uso dentro de la aplicación con atribución visible, pero no redistribuir el dataset como dataset o API independiente. No se copian los 609+ registros ni sus imágenes al repositorio. Fuente: [catálogo público](https://exercise-dataset.com/) y [licencia de datos](https://github.com/RepDB/exercise-dataset/blob/main/LICENSE-DATA.md).
