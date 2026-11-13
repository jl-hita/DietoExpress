# Landing: análisis y propuesta (2026-11-11)

Análisis de `anguloso.client/src/app/componentes/landing`. Se basa en leer la página y en verla en el navegador integrado
(escritorio y móvil). **No he medido rendimiento (Lighthouse) ni analítica de conversión**; las recomendaciones son de criterio
y conviene validarlas con datos reales de visitas y registros.

## Qué hay hoy

Diez bloques seguidos: hero con maqueta del panel, tira de funciones, «plataforma completa» (6 tarjetas), captación y agenda
(maqueta del paciente), consulta online (4 tarjetas), app Android (maqueta), captación de pacientes (3 tarjetas), clínicas
(3 tarjetas), nutrición especializada (3 tarjetas), entrenamiento (6 tarjetas), una banda de «planes» y confianza.

## Lo que funciona

- El hero comunica bien a quién se dirige (nutrición, entrenamiento y clínicas) y tiene una acción clara.
- Visual coherente (paleta verde, tipografía, maquetas del producto) y buen comportamiento en móvil.
- Los mensajes de producto son concretos y verificables (agenda, portal, videoconsulta, entrenamiento con adaptación).

## Problemas detectados

1. **No mostraba planes ni precios** (confirmado): la sección `#planes` solo tenía un texto y un botón. Es la objeción número uno
   de un profesional que compara con otro programa. **Corregido en esta rama** (ver abajo).
2. **Demasiado larga y repetitiva.** Unas 30 tarjetas con el mismo formato (icono + título + párrafo) en seis cuadrículas. El
   visitante no distingue qué es lo importante y el mensaje se diluye. Los bloques de «consulta online», «captación» y «agenda»
   se solapan.
3. **Todo es maqueta dibujada en HTML**, no capturas del producto real. Transmite «concepto» y no «esto es lo que usarás».
4. **Sin prueba social**: ni testimonios, ni cifras, ni casos, ni logos. Para datos de salud es lo que más tranquiliza.
5. **Sin preguntas frecuentes** (¿mis datos están seguros? ¿puedo migrar desde otro programa? ¿hay permanencia? ¿qué pasa al
   acabar la prueba?). Son objeciones que hoy solo se resuelven escribiendo a soporte.
6. **Llamadas a la acción inconsistentes**: «Empezar gratis», «Crear mi cuenta», «Conocer DietoExpress», «Probar DietoExpress»,
   «Gestionar mi consulta», «Crear cuenta gratis». Todas hacen lo mismo; varias palabras para una sola acción restan claridad.
7. **La app Android se anuncia sin enlace a la tienda** ni capturas.
8. **Solo en español** (el i18n llega al login y a piezas del portal, no a la landing).
9. **Afirmaciones difíciles de sostener**: «más visible», «siempre conectado», «Capta, atiende y fideliza». Mejor mostrar
   funciones concretas que prometer resultados comerciales.
10. **SEO básico**: el `<title>` de `index.html` y el de la ruta no coinciden y no hay datos estructurados (descripción, Open Graph,
    FAQ, organización). Conviene revisarlo con una herramienta de auditoría.

## Hecho en esta rama

- Sección **Planes y precios** real: lee `GET /api/public/plans`, que sale de la misma tabla que usa el cobro
  (`subscription_plans`), por lo que el precio de la web nunca puede diferir del que se cobra y se edita desde SuperAdmin.
- Selector mensual/anual (por defecto anual), precio mensual equivalente, importe anual y ahorro calculados (hoy 17 %).
- Solo se anuncian planes vendibles (con precio y precios de Stripe configurados). Los internos (`free`, `demo_nutri`,
  `trial_nutri`) no se muestran, y no se expone ningún identificador de Stripe.
- Límites (nutricionistas, pacientes) y funcionalidades según lo que el plan tiene realmente activado.
- Si el catálogo no carga, vuelve a verse la banda anterior con el botón de empezar (no queda un hueco).
- Endpoint con caché de 5 minutos y límite de peticiones.

## Propuesta por prioridad

**Primero (alto impacto, bajo esfuerzo)**
1. Unificar la llamada a la acción: «Probar 7 días gratis» (o el texto que se decida) en todas partes, y un botón fijo en móvil.
2. Añadir bloque de **preguntas frecuentes** con datos estructurados FAQ. Incluir seguridad (verificación en dos pasos, copias cifradas,
   aislamiento por centro), permanencia y qué ocurre al acabar la prueba.
3. Reducir la página a 5–6 bloques: hero → cómo funciona (3 pasos) → nutrición/entrenamiento (con pestañas en vez de dos cuadrículas)
   → pacientes y app → clínicas → planes → preguntas. Las tarjetas sobrantes pasan a una página de «Funciones».
4. Corregir el título y añadir descripción y Open Graph.

**Después (necesita material tuyo)**
5. Sustituir las maquetas por **capturas o un vídeo corto** del producto real, con datos ficticios.
6. **Prueba social**: dos o tres testimonios de profesionales piloto, número de pacientes gestionados, una cita con nombre y foto
   (con su permiso).
7. Enlace a **Google Play** y capturas de la app cuando esté publicada.
8. Bloque «**¿Vienes de otro programa?**» cuando exista la migración (punto 2 del plan): es un argumento fuerte frente a Dietowin.
   Evitar comparativas con su nombre sin revisión legal.

**Más adelante**
9. Traducir la landing (catalán e inglés) junto con el resto del i18n y activar la detección del idioma del navegador.
10. Medir: eventos de «clic en crear cuenta», profundidad de scroll y registros por origen, para decidir con datos qué bloques sobran.

## Decisiones que necesito de ti

- ~~Número de días de la prueba~~: resuelto, es de 30 días y la web lo lee del catálogo (no está escrito en el código).
- ~~Si el precio se anuncia con o sin IVA~~: resuelto, los precios incluyen IVA y la web lo indica.
- Si hay testimonios, capturas reales o un vídeo que podamos usar.

## Rediseño de la landing (2026-11-13)

- Estructura nueva: hero → franja del ciclo completo → cómo funciona (3 pasos) → dietas → entrenamiento → seguimiento → agenda y
  videoconsulta → app del paciente → automatizaciones → clínicas → herramientas → seguridad → planes → preguntas → cierre.
- Una sola llamada a la acción («Probar N días gratis», con los días leídos del catálogo de planes) y botón fijo en móvil.
- Preguntas frecuentes con datos estructurados FAQ y SoftwareApplication, título, descripción y Open Graph propios (el componente
  los pone al entrar y restaura la descripción al salir).
- Las maquetas son HTML/CSS con texto legible (≥ 11 px) y datos ficticios. **Siguen siendo maquetas**: sustituirlas por capturas
  reales con `landing.content.ts` (la sección «Capturas» ya existe y aparece sola cuando hay material).
- La app Android se anuncia «en prueba cerrada» sin enlace a la tienda; añadir el botón de Google Play cuando esté publicada.
- Pendiente: testimonios reales, bloque «¿Vienes de otro programa?» cuando exista la migración, traducción ES/EN/CA.
