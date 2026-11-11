/**
 * Contenido editable de la landing que todavía no existe: vídeo, capturas del producto y testimonios.
 *
 * Para publicarlo basta con rellenar `LANDING_CONTENT` (ver docs/LANDING_CONTENIDO.md). Cada sección solo aparece cuando tiene
 * contenido, así que mientras esté vacío la página no muestra huecos, ni enlaces en el menú, ni títulos sin nada debajo.
 *
 * Los archivos van en `public/assets/landing/` y se referencian como `/assets/landing/<archivo>`. Se alojan en el propio
 * servidor a propósito: no hay terceros que carguen cookies ni scripts, y la política de seguridad de contenido solo permite
 * vídeo e imágenes propios.
 */

export interface LandingVideo {
  /** Ruta del archivo MP4 (H.264), p. ej. `/assets/landing/demo.mp4`. */
  src: string;
  /** Título accesible del vídeo (se lee con lectores de pantalla). */
  title: string;
  /** Imagen de portada que se ve antes de reproducir. Muy recomendable: evita descargar el vídeo para mostrar algo. */
  poster?: string;
  /** Subtítulos en formato WebVTT. Muy recomendable para accesibilidad y para quien ve el vídeo sin sonido. */
  captionsSrc?: string;
  captionsLang?: string;
  /** Frase breve bajo el vídeo. */
  caption?: string;
}

export interface LandingScreenshot {
  /** Ruta de la imagen, p. ej. `/assets/landing/agenda.webp`. Debe usar datos ficticios, nunca de pacientes reales. */
  src: string;
  /** Descripción para lectores de pantalla. Obligatoria. */
  alt: string;
  /** Texto visible bajo la imagen. */
  caption?: string;
  /** `desktop` (apaisada) o `mobile` (vertical, de la app). */
  device?: 'desktop' | 'mobile';
  /** Dimensiones reales en píxeles: evitan que la página salte al cargar la imagen. */
  width?: number;
  height?: number;
}

export interface LandingTestimonial {
  quote: string;
  name: string;
  /** Profesión o cargo, p. ej. «Dietista-nutricionista». */
  role: string;
  /** Ciudad, clínica o similar. */
  place?: string;
  photo?: string;
  photoAlt?: string;
  /**
   * Debe ser `true` solo si la persona ha dado permiso por escrito para publicar su nombre, su foto y sus palabras (RGPD).
   * Un testimonio sin este permiso no se muestra aunque esté en la lista.
   */
  consentGiven: boolean;
}

export interface LandingContent {
  video: LandingVideo | null;
  screenshots: LandingScreenshot[];
  testimonials: LandingTestimonial[];
}

/** Vacío hasta que haya material real. Ver docs/LANDING_CONTENIDO.md. */
export const LANDING_CONTENT: LandingContent = {
  video: null,
  screenshots: [],
  testimonials: []
};

const ASSET_PREFIX = '/assets/landing/';
const isLocalAsset = (path: string | undefined): boolean =>
  !!path && path.startsWith(ASSET_PREFIX) && !path.includes('..') && !path.includes('//', 1);

/** Testimonios que se pueden publicar: con permiso expreso y con el texto imprescindible. */
export function publishableTestimonials(content: LandingContent): LandingTestimonial[] {
  return content.testimonials.filter(t => t.consentGiven === true && !!t.quote?.trim() && !!t.name?.trim() && !!t.role?.trim());
}

/** Capturas con descripción accesible y archivo propio. */
export function publishableScreenshots(content: LandingContent): LandingScreenshot[] {
  return content.screenshots.filter(s => isLocalAsset(s.src) && !!s.alt?.trim());
}

/** Vídeo con archivo propio y título accesible. */
export function publishableVideo(content: LandingContent): LandingVideo | null {
  const video = content.video;
  return video && isLocalAsset(video.src) && !!video.title?.trim() ? video : null;
}

/**
 * Devuelve los problemas del contenido que impedirían publicarlo bien. Un test comprueba que el contenido incluido en el
 * repositorio no tiene ninguno, de modo que no se puede subir una captura sin descripción o un testimonio sin permiso.
 */
export function validateLandingContent(content: LandingContent): string[] {
  const problems: string[] = [];

  if (content.video) {
    if (!isLocalAsset(content.video.src)) problems.push(`Vídeo: «src» debe empezar por ${ASSET_PREFIX} (no se permiten URL externas).`);
    if (!content.video.title?.trim()) problems.push('Vídeo: falta «title» (descripción accesible).');
    if (content.video.poster && !isLocalAsset(content.video.poster)) problems.push(`Vídeo: «poster» debe empezar por ${ASSET_PREFIX}.`);
    if (content.video.captionsSrc && !isLocalAsset(content.video.captionsSrc)) problems.push(`Vídeo: «captionsSrc» debe empezar por ${ASSET_PREFIX}.`);
  }

  content.screenshots.forEach((shot, i) => {
    if (!isLocalAsset(shot.src)) problems.push(`Captura ${i + 1}: «src» debe empezar por ${ASSET_PREFIX}.`);
    if (!shot.alt?.trim()) problems.push(`Captura ${i + 1}: falta «alt» (descripción accesible).`);
  });

  content.testimonials.forEach((t, i) => {
    if (t.consentGiven !== true) problems.push(`Testimonio ${i + 1} (${t.name || 'sin nombre'}): falta el permiso expreso («consentGiven: true»).`);
    if (!t.quote?.trim() || !t.name?.trim() || !t.role?.trim()) problems.push(`Testimonio ${i + 1}: faltan «quote», «name» o «role».`);
    if (t.photo && !isLocalAsset(t.photo)) problems.push(`Testimonio ${i + 1}: «photo» debe empezar por ${ASSET_PREFIX}.`);
    if (t.photo && !t.photoAlt?.trim()) problems.push(`Testimonio ${i + 1}: la foto necesita «photoAlt».`);
  });

  return problems;
}
