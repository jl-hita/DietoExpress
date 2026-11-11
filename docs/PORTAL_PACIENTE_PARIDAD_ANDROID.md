# Portal web del paciente: paridad con la app Android

Objetivo: que el portal web del paciente sea una «copia» de la app Android (misma estructura, mismas pantallas, mismo
comportamiento), porque la app va mucho más avanzada. Este documento recoge el inventario, las brechas y el plan por fases.

**Alcance de la comparación.** Se ha hecho sobre el código y la documentación de la app (`MainActivity`, `PortalViewModel`,
`PortalApi`, `docs/`) y sobre el portal web actual. No he usado la app en un dispositivo; lo que dependa del comportamiento real
(offline, notificaciones, vídeo) se marca como «por validar».

## Contratos: la API ya es la misma

La app consume `/api/portal/*`, `/api/appointments/*` y `/api/messages/patient`. Son endpoints que ya existen en el backend y
sirven igual a la web, así que **la paridad es casi toda de frontend**. Hay que mantener la compatibilidad con la app al tocar
cualquiera de ellos (`DietoExpress.Android/docs/API_COMPATIBILITY.md`).

## Estructura de la app Android que se replica

| Elemento | Android | Web actual |
|---|---|---|
| Navegación | Barra inferior de 5 accesos: Inicio, Mi dieta, Entrenamiento, Progreso, Citas | Pestañas distintas (Hoy, Citas, Compra, Mensajes, Documentos, Entrenamiento, Progreso) |
| Inicio | Saludo, próximo entrenamiento, próxima cita, accesos rápidos y panel «Tu seguimiento» | «Hoy» centrado en la dieta |
| Secundarias | Mensajes, Documentos, Lista de compra y Perfil desde Inicio | Pestañas al mismo nivel |
| Diseño | Paleta verde (`#176B52`), tarjetas de 22 px, tipografía Outfit | Estilo propio del portal |

## Matriz de funcionalidades

| Área | Android | Web actual | Brecha |
|---|---|---|---|
| Acceso con enlace de email y PIN | Sí | Sí | Reutilizar |
| Acceso con huella/rostro | Sí (clave en Keystore) | No | **WebAuthn (passkeys)**: el backend de dispositivos biométricos se puede extender |
| Inicio | Sí, con panel de seguimiento | Parcial | Nueva pantalla (**hecha la base**) |
| Dieta | Plan por días, macros por comida, PDF | Sí | Rediseñar al estilo de la app |
| Lista de compra | Sí, con avisos de alérgenos | Sí | Rediseñar |
| Entrenamiento | Modo activo: registro por serie (calentamiento/trabajo, carga, repeticiones, RIR), descansos, imágenes, cambio de ejercicio con alternativas, valoración, borrador local, petición de revisión | Registro básico, recomendación y petición de revisión | **La brecha mayor** |
| Progreso | Gráficas de peso, altura, grasa, masa muscular, grasa visceral, cintura y cadera; revisión semanal | Gráfica básica y check-in | Ampliar |
| Citas | Lista, disponibilidad, reserva, cancelación, historial | Sí | Rediseñar |
| Videoconsulta | LiveKit, ventana de acceso | Sí | Reutilizar la existente |
| Documentos | Lista, PDF, aceptación tras abrir | Sí | Rediseñar |
| Mensajes | Chat con borrador y reintento seguro | Sí | Rediseñar |
| Perfil | Datos, cambio/baja de profesional, sincronización | Parcial | Ampliar |
| Notificaciones | FCM | Web Push parcial | Reutilizar |
| Directorio público y reserva | Sí | Sí (página pública) | Enlazar |
| Offline | Caché cifrada (Room) y cola de envíos | No | PWA con caché de lectura (decisión abajo) |
| Idiomas | Español | Español, inglés y catalán (infraestructura) | Las pantallas nuevas nacen con las tres lenguas |

## Decisiones de arquitectura

1. **Interfaz nueva en `/portal`, convivencia con `/patient`.** Los pacientes entran hoy por enlaces de email y notificaciones que
   apuntan a `/patient`; cambiarlo de golpe rompería esos enlaces y es arriesgado sin pruebas de pantalla completas. `/portal` se
   construye en paralelo, sin enlazarlo, y cuando tenga todas las secciones `/patient` pasa a redirigir allí.
2. **Cada sección se migra por separado** y mientras no esté hecha, su ruta muestra un aviso con enlace al portal actual
   (`PatientAppPendingComponent`), de modo que nunca hay un callejón sin salida.
3. **i18n desde el principio**: todas las pantallas nuevas usan claves (`papp.*`) en español, inglés y catalán. Los diccionarios
   están tipados: una traducción olvidada no compila.
4. **Reglas compartidas con la app**: «próxima cita» y «próximo entrenamiento» usan la misma lógica que `HomeContent` de Android
   (`patient-app.logic.ts`, con tests).
5. **Diseño**: tokens CSS (`--pa-*`) con los mismos colores que el tema de Android. En pantallas anchas la barra inferior pasa a un
   lateral.

## Plan por fases

| Fase | Contenido | Estado |
|---|---|---|
| 1a | Estructura: ruta `/portal`, cabecera, navegación de 5 accesos, Inicio (saludo, próximo entrenamiento, próxima cita, accesos rápidos), i18n | **Hecha** |
| 1b | Inicio: panel «Tu seguimiento» (peso y tendencia) | Pendiente |
| 2 | Dieta y lista de compra | Pendiente |
| 3 | Entrenamiento: lista de sesiones, modo activo con series, descansos, imágenes y cambio de ejercicio | Pendiente (la mayor) |
| 4 | Progreso (gráficas y revisión semanal) y Citas (reserva, cancelación, videoconsulta) | Pendiente |
| 5 | Documentos, Mensajes y Perfil | Pendiente |
| 6 | Acceso propio en `/portal` (enlace, PIN, passkeys), PWA instalable y caché de lectura, y paso de `/patient` a `/portal` | Pendiente |

## Decisiones que necesito

- **PWA/offline**: la app Android guarda datos cifrados y una cola de envíos. En la web propongo solo caché de lectura (ver la
  dieta y el entrenamiento sin conexión) y **no** cola de envíos al principio, por los riesgos de duplicados que ya se anotaron
  en la propia app. ¿Te parece bien?
- **Passkeys (WebAuthn)** para el acceso biométrico en la web: requiere ampliar el backend. ¿Entra en el alcance?
- **Fecha de corte**: cuándo se pasa `/patient` a `/portal` (propongo cuando estén las fases 1–5 y se haya probado con pacientes reales).
