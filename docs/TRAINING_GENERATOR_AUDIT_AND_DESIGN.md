# Auditoría y diseño del generador adaptativo de entrenamiento

Estado: especificación inicial; todavía no equivale a un motor implementado ni validado fisiológicamente.
Issue de seguimiento: https://github.com/jl-hita/DietoExpress/issues/415

## 1. Hallazgos del código existente

### Planificación profesional
- `TrainingPeriodizationController` administra perfiles, planes, mesociclos, microciclos, sesiones y plantillas.
- `training_sessions.exercise_prescription` y `training_session_templates.exercise_prescription` son texto libre. Es útil para mostrar planes heredados, pero no permite validar con fiabilidad series efectivas por músculo, progresión por ejercicio ni sustituciones estructuradas.
- La planificación actual está orientada a que el profesional gestione el calendario; no se ha encontrado aún un motor que genere y optimice un ciclo entero desde el perfil del cliente.
- Ya existe una prueba de regresión estática en `Tests/DietoExpress.Security.Tests/TrainingPeriodizationRegressionTests.cs`, que protege parte del contrato actual.

### Biblioteca de ejercicios: discrepancia detectada
- La UI de planificación declara un modelo `RepDbExercise` con nombre, material, región corporal, categoría, objetivos, músculos principales, instrucciones e imágenes.
- La trazabilidad del código muestra que `loadExpandedExerciseCatalog()` descarga el catálogo desde `https://exercise-dataset.com/exercises.json` directamente en el navegador. Si falla, mantiene el catálogo curado básico definido en la propia UI. La atribución visible menciona RepDB.
- **En el flujo revisado no se observa que ese catálogo ampliado se importe o persista en PostgreSQL.** Por tanto, no debemos asumir que el servidor tiene la misma biblioteca que ve la UI. Puede existir otra tabla o proceso de importación independiente, pero no aparece en el flujo examinado.
- La clasificación actual de músculos, objetivos y material incluye heurísticas derivadas de texto; sirve para filtros de interfaz, pero no basta por sí sola como metadato de confianza para un generador de entrenamiento.
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
- **Semana de descarga**: basarla en señales y en el diseño del ciclo; no insertarla mecánicamente cada cuatro semanas para todo el mundo.

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
Crear una matriz reproducible de perfiles sintéticos y reportar por versión: errores de validación, planes rechazados, violaciones de invariantes, distribución de volumen, cobertura, duración estimada y decisiones de adaptación. La generación masiva sirve para detectar fallos lógicos, no para demostrar eficacia clínica.

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

### 9.1 Primer contrato estructurado implementado
`TrainingProgramPrescription` representa el plan con versión del motor, objetivo, fecha de inicio, duración y sesiones. Cada sesión tiene semana/día y duración estimada; cada ejercicio conserva ID estable, versión del catálogo y nombre snapshot, orden, series, rango de repeticiones, RIR objetivo, descanso, regla de progresión y motivo de selección.

`TrainingPrescriptionValidator` rechaza contratos incompletos o inconsistentes antes de persistirlos o mostrarlos. Sus límites son guardas estructurales de software, no una afirmación de que una dosis concreta sea apropiada para todas las personas. El siguiente paso es un generador puro que consuma un catálogo explícito de ejercicios con metadatos fiables y produzca planes reproducibles que pasen este validador.

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
