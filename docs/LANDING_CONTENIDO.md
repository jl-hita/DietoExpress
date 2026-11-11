# Cómo añadir vídeo, capturas y testimonios a la landing

La landing está preparada para tres bloques que todavía no tienen material. Mientras estén vacíos **no se muestra nada**: ni
secciones, ni títulos, ni enlaces en el menú. Basta con rellenar un único archivo y añadir los ficheros.

- Contenido: `anguloso.client/src/app/componentes/landing/landing.content.ts` (constante `LANDING_CONTENT`).
- Archivos (imágenes, vídeo, subtítulos): `anguloso.client/public/assets/landing/`. Se referencian como `/assets/landing/<archivo>`.

Todo se aloja en el propio servidor a propósito: no hay terceros (YouTube, CDN) que carguen cookies o scripts, y la política de
seguridad de contenido solo permite vídeo e imágenes propios. Una URL externa **no se mostrará** y el test de la landing falla.

## Reglas comunes

- **Datos ficticios siempre.** Ninguna captura puede contener datos de pacientes reales (nombres, fotos, mediciones, citas).
  Usa una cuenta de demostración con datos inventados.
- **Texto alternativo obligatorio** en cada imagen y título en el vídeo: lo leen los lectores de pantalla.
- Tras cambiar el contenido: `npm run build` y `npm test` en `anguloso.client`. El test `landing.content.spec.ts` falla si algo
  incumple estas reglas.

## Vídeo

```ts
video: {
  src: '/assets/landing/demo.mp4',          // MP4 (H.264 + AAC)
  title: 'Así trabajas con DietoExpress en cinco minutos',
  poster: '/assets/landing/demo-poster.jpg',
  captionsSrc: '/assets/landing/demo.es.vtt', // subtítulos WebVTT
  captionsLang: 'es',
  caption: 'Del alta del paciente a la dieta en pocos pasos.'
}
```

- Formato 16:9, 1280×720 o 1920×1080, idealmente de 60 a 120 segundos y por debajo de 15 MB (se carga solo al pulsar play).
- Aporta **portada** (`poster`) y **subtítulos**: mucha gente lo ve sin sonido y mejora la accesibilidad.
- Aparece como sección «Míralo en acción» justo antes de las funciones y añade «Vídeo» al menú.

## Capturas

```ts
screenshots: [
  { src: '/assets/landing/agenda.webp', alt: 'Agenda semanal con citas y videoconsultas',
    caption: 'Agenda con citas y videoconsultas', device: 'desktop', width: 1600, height: 1000 },
  { src: '/assets/landing/app-dieta.webp', alt: 'Pantalla de dieta en la app del paciente',
    caption: 'La app del paciente', device: 'mobile', width: 750, height: 1500 }
]
```

- Formato WebP (o PNG/JPG). Escritorio: unos 1600 px de ancho. Móvil: unos 750 px de ancho.
- Indica `width` y `height` reales: evitan que la página «salte» al cargar.
- `device: 'mobile'` las muestra verticales y más estrechas. Sin `device` se tratan como apaisadas.
- Se muestran en la sección «Así se ve DietoExpress» y añaden «Capturas» al menú.

## Testimonios

```ts
testimonials: [
  { quote: 'Preparo las dietas en la mitad de tiempo.', name: 'Laura Gómez',
    role: 'Dietista-nutricionista', place: 'Valencia',
    photo: '/assets/landing/laura.jpg', photoAlt: 'Laura Gómez', consentGiven: true }
]
```

- **`consentGiven: true` solo si la persona ha dado permiso por escrito** para publicar su nombre, su foto y sus palabras
  (RGPD). Conserva ese permiso. Un testimonio sin él no se muestra aunque esté en la lista, y el test lo señala.
- No inventes ni retoques las opiniones. Si el texto se acorta, que lo apruebe quien lo dijo.
- La foto es opcional (cuadrada, 96×96 px o más). Si la pones, `photoAlt` es obligatorio.
- Se muestran en «Lo que dicen los profesionales», antes de los planes, y añaden «Opiniones» al menú.

## Pendiente cuando haya material

- Enlace a la ficha de Google Play junto con las capturas de la app.
- Traducción de estos textos al catalán y al inglés junto con el resto de la landing.
