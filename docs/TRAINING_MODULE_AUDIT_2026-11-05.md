# Auditoría inicial del módulo de entrenamiento

Fecha: 2026-11-05  
Alcance: revisión estática de `main` en `jl-hita/DietoExpress` y `jl-hita/DietoExpress.Android`. No se ha ejecutado el backend contra PostgreSQL ni se ha accedido a una sesión autenticada del entorno; por tanto, las cifras actuales del catálogo no se inventan. Esta auditoría describe lo verificable en código y distingue los datos que requieren consulta dinámica.

## Resumen ejecutivo

- **Backend:** existe una base de periodización/generación bastante avanzada para planificación profesional, prescripciones estructuradas JSONB, análisis de calidad y feedback/adaptación conservadora. No existe todavía un registro de ejecución granular por serie en el portal del paciente.
- **Android:** existe una pantalla de planificación/seguimiento de sesiones, resúmenes semanales y feedback por sesión. No existe un modo de entrenamiento en vivo para registrar cada serie con carga, repeticiones y RIR. Las escrituras de entrenamiento siguen siendo online; la caché offline es de lectura.
- **Catálogo:** PostgreSQL dispone de catálogo, procedencia RepDB, estados pending/approved/rejected, metadatos revisados, herramientas de curación y estadísticas globales. El generador solo selecciona candidatos revisados o aportados por el profesional propietario. Sin acceso de solo lectura al PostgreSQL activo no es posible certificar el recuento vigente ni auditar el contenido de cada ejercicio.
- **Conclusión:** P0 no debe rehacer el generador ni la periodización. Debe construir el circuito de ejecución paciente ↔ backend ↔ Android, con registro por serie, persistencia y sincronización segura, manteniendo la prescripción histórica inmutable.

## 1. Backend

### Ya implementado

- `TrainingPeriodizationController`: gestión profesional de perfiles, planes, mesociclos, microciclos, sesiones y plantillas.
- `training_sessions.exercise_prescription_data` almacena prescripciones estructuradas JSONB (motor/objetivo/semana/día/ejercicios, ID y versión de catálogo, series, rango de repeticiones, RIR, descanso, progresión y grupos musculares).
- `TrainingProgramGenerator`: generador determinista que consume candidatos explícitos y selecciona por revisión/propiedad, patrón, objetivo, experiencia mínima, equipamiento y exclusiones. Mantiene selección estable entre semanas para facilitar seguimiento.
- `TrainingProgramQualityAnalyzer`: calcula series y frecuencia primaria/secundaria por grupo muscular y emite avisos no bloqueantes. El umbral de más de 20 series primarias es heurístico y el propio código lo presenta como diagnóstico, no como límite fisiológico universal.
- `TrainingPortalController`: GET de sesiones del paciente y POST de registro agregado por sesión. El registro actual guarda duración, RPE global, notas, outcome, motivo, recuperación, señal de dolor, objetivo de progresión y feedback agregado por ejercicio (series previstas/completadas), mediante upsert por paciente/sesión.
- `TrainingAdaptationPolicy`: política determinista conservadora. Señales de dolor bloquean progresión; baja finalización, recuperación insuficiente o esfuerzo alto mantienen/revisan la progresión; no reescribe el plan. Se generan propuestas para revisión profesional.
- Tests existentes: generador, validador de prescripciones, regresión de periodización, analizador, matriz/política adaptativa y flujo de revisión.

### Brechas P0

1. No hay entidad/contrato de serie ejecutada: faltan set number, tipo (warm-up/work), carga, repeticiones reales, RIR/RPE por serie, estado y marca temporal.
2. El feedback actual resume series por ejercicio; no permite reconstruir qué hizo el usuario ni calcular progresión por ejercicio/serie.
3. El POST de registro de sesión no expone un contrato explícito de idempotencia/versión para una secuencia de guardados offline. El upsert existente no basta para resolver escrituras concurrentes o una cola de snapshots obsoletos.
4. El historial de prescripción y la ejecución deben permanecer separados; el registro de ejecución no debe modificar `exercise_prescription_data`.
5. El endpoint actual devuelve sesiones y feedback, pero todavía no expone un historial detallado de series por ejercicio para el logger.

### Reutilizar

Mantener los controladores y modelos de periodización, el JSONB estructurado, la biblioteca existente, el analizador y la política adaptativa. Añadir la capa de ejecución como agregado independiente y versionado; no meter el logger dentro del generador ni sobrecargar la prescripción profesional.

## 2. Android

### Ya implementado

- Jetpack Compose y navegación por secciones dentro de `MainActivity.kt`.
- `TrainingFeedbackContent.kt` muestra sesiones programadas, navegación por periodos, resumen semanal estimado, prescripciones de ejercicios y formulario de feedback agregado por sesión.
- `PortalViewModel.loadTraining()` utiliza `OfflineRepository.readThrough` para cachear `GET /portal/training/sessions` por cuenta.
- `recordTrainingSession()` valida feedback y envía `POST /portal/training/sessions/{id}/record`; tras el éxito vuelve a cargar sesiones y actualiza caché.
- Room almacena caché cifrada y operaciones pendientes genéricas; WorkManager sincroniza tipos de operación que tienen handlers explícitos.
- Contratos/tests de visibilidad de sesiones y feedback existentes.

### Brechas P0

1. No hay pantalla de ejecución activa ni controles por serie; el usuario solo registra resultado agregado de la sesión.
2. No hay persistencia local de una sesión en curso ni de sus series; cerrar la app puede perder el progreso que aún no se ha enviado.
3. El POST de entrenamiento se hace online y no se encola en outbox. El documento `docs/OFFLINE_ENDPOINT_AUDIT.md` lo prohíbe correctamente hasta fijar idempotencia y conflictos.
4. `MainActivity.kt` concentra navegación y gran parte de UI (archivo grande, ~92 KB); P0 debe extraer una superficie de entrenamiento modular en archivos propios, sin reescribir las demás secciones.
5. El tema global es verde/forest. Se puede dar al entrenamiento una identidad visual diferenciada (por ejemplo, grafito + lima/ámbar) mediante tokens locales de Compose, sin alterar dieta ni el resto del portal.

### Reutilizar

Room, cifrado Keystore, repositorio offline, outbox, WorkManager, Retrofit, modelos de prescripción estructurada y navegación existente. No encolar el POST agregado actual sin cambiar primero su contrato.

## 3. Catálogo de ejercicios

### Ya implementado

- `ExerciseCatalogService` crea/sincroniza catálogo PostgreSQL con fuente `repdb-free` y contribuciones profesionales.
- Estados `pending`, `approved`, `rejected`; revisión humana y metadatos revisados (patrón, músculos principales/secundarios, experiencia, objetivos, equipamiento, prioridad y notas).
- Estadísticas globales con conteos por estado y fuente disponibles a través de `GET /api/exercise-catalog/review-stats` (solo superadmin).
- Curación automática RepDB v2 intenta enriquecer/autorizar metadatos para filas pendientes sin sobrescribir decisiones humanas. Este paso es una heurística de precuración y no equivale a evidencia científica ni revisión anatómica experta.
- El generador filtra por patrón, objetivo, experiencia, material y exclusiones y ordena por prioridad de selección.

### Riesgos y trabajo pendiente

1. El estado de producción no se puede certificar en esta auditoría estática. Hay que consultar `review-stats` y exportar la cola completa en una sesión superadmin, sin usar cifras antiguas.
2. Auditar la lista aprobada por utilidad real para hipertrofia, no solo por metadatos válidos. Revisar especialmente básicos y máquinas progresables (press banca, jalón, remos, prensa, sentadilla, bisagras, extensiones/curls, elevaciones laterales) y redundancias/ejercicios de poco valor.
3. Verificar licencia, atribución y permiso de redistribución de RepDB antes de empaquetar contenido multimedia o republicar el catálogo.
4. Revisar la calidad de patrones, músculos primarios/secundarios, equipamiento, dificultad y prioridad. Mantener desconocido lo no validado; no confiar exclusivamente en heurísticas por nombre/descripción.
5. Revisar por qué algunos filtros de patrón/material pueden dejar cero candidatos; el diagnóstico del generador ya informa el embudo, pero falta una evaluación de cobertura del catálogo por objetivo/experiencia/equipamiento.
6. El ranking actual usa `SelectionPriority` y orden alfabético como desempate. Falta un criterio de curación explícito y auditable basado en estímulo, progresabilidad, estabilidad, recorrido, fatiga, dificultad técnica y contexto.

### Datos dinámicos requeridos

Ejecutar como superadmin:

- `GET /api/exercise-catalog/review-stats`
- `GET /api/exercise-catalog/review-queue/export`

Guardar un snapshot fechado de total/pending/approved/rejected y revisar una exportación de aprobados. No publicar datos personales de contribuyentes ni asumir que el número aprobado equivale al número de ejercicios de calidad para hipertrofia.

## 4. Diseño P0 recomendado

- Nuevo agregado de ejecución independiente de `training_session_records` (o extensión versionada con semántica equivalente) que guarde una instantánea de series por sesión/ejercicio.
- Contrato de escritura con identificador de mutación idempotente y control de versión/orden para que reintentos y cola offline no sobrescriban datos más recientes.
- Leer ejecución junto a sesiones en un endpoint del paciente autenticado, validando que la sesión pertenece al paciente derivado de claims.
- Android: Room para sesión activa/series y operaciones pendientes cifradas; autosave local inmediato; WorkManager solo tras definir idempotencia y conflictos; cambio de cuenta no mezcla ni sincroniza datos ajenos.
- UI dedicada: resumen de sesión, ejercicio activo, series con controles grandes, peso/reps/RIR, warm-up vs trabajo, descanso, historial reciente, sustitución, añadir/quitar series, guardar y finalizar. Feedback de recuperación se mantiene al finalizar, no interrumpe cada serie.
- Tokens visuales propios del módulo de entrenamiento y componentes Compose separados de `MainActivity.kt`.

## 5. Criterios de aceptación P0

- Una sesión en curso y cada serie sobreviven a cierre forzado/reinicio de proceso.
- Registro sin conexión completo; reintentos no duplican ni revierten datos.
- Conflictos explícitos y sin pérdida silenciosa de datos.
- Las prescripciones originales e historial no se modifican.
- Carga/repeticiones/RIR se recuperan correctamente desde otro dispositivo tras sincronizar.
- UI usable con una mano, botones grandes, baja fricción, contraste/accesibilidad y pruebas de pantallas pequeñas.
- Backend tests de autorización, pertenencia de sesión, validación, idempotencia, concurrencia y compatibilidad.
- Android unit/instrumented tests para persistencia, migración, outbox, reintentos, cambio de cuenta y restauración de sesión.

## Limitaciones de esta auditoría

- Revisión estática de repositorios; no se ejecutaron tests ni builds en esta sesión.
- No se consultó la base PostgreSQL de producción ni el endpoint administrativo, por lo que los conteos reales y la calidad individual del catálogo siguen pendientes de consulta dinámica.
- No se validó en dispositivo físico la UX actual.

## Referencias de código revisadas

- Backend: `TrainingPortalController`, `TrainingPeriodizationController`, `TrainingProgramGenerator`, `TrainingPrescription`, `TrainingProgramQualityAnalyzer`, `TrainingAdaptationPolicy`, `ExerciseCatalogService`, `RepDbExerciseCuration`, `RepDbExerciseAutoReviewer` y tests asociados.
- Android: `MainActivity`, `TrainingFeedbackContent`, `PortalViewModel`, `PortalApi`, `OfflineDatabase`, `OfflineRepository`, `SyncPendingOperationsWorker`, `ANDROID_GAP_AUDIT.md` y `OFFLINE_ENDPOINT_AUDIT.md`.
