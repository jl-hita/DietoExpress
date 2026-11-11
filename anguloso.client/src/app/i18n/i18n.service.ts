import { Injectable } from '@angular/core';
import { es, type Dictionary, type TranslationKey } from './es';

export type Lang = 'es' | 'en' | 'ca';

/** Idiomas disponibles, con su nombre en el propio idioma (así se reconocen aunque la interfaz esté en otro). */
export const SUPPORTED_LANGUAGES: ReadonlyArray<{ code: Lang; label: string }> = [
  { code: 'es', label: 'Español' },
  { code: 'en', label: 'English' },
  { code: 'ca', label: 'Català' }
];

export const DEFAULT_LANGUAGE: Lang = 'es';
const STORAGE_KEY = 'dietoexpress_lang';

export function isSupportedLanguage(value: unknown): value is Lang {
  return value === 'es' || value === 'en' || value === 'ca';
}

/**
 * Mientras la traducción no cubra toda la aplicación, el idioma del navegador NO se usa para elegir el idioma: un visitante
 * con el navegador en inglés vería el inicio de sesión traducido y el resto en español. Cuando todas las pantallas estén
 * traducidas basta con poner esta constante a true.
 */
export const AUTO_DETECT_BROWSER_LANGUAGE = false;

/**
 * Elige el idioma: primero la preferencia guardada por el usuario; después, si la detección automática está activada, el
 * idioma del navegador (`ca-ES` → `ca`); y si no, el español. Es una función pura para poder probarla sin navegador.
 */
export function detectLanguage(
  stored: string | null | undefined,
  browserLanguages: readonly string[] | undefined,
  autoDetect: boolean = AUTO_DETECT_BROWSER_LANGUAGE
): Lang {
  if (isSupportedLanguage(stored)) return stored;
  if (autoDetect) {
    for (const candidate of browserLanguages ?? []) {
      const primary = candidate?.toLowerCase().split('-')[0];
      if (isSupportedLanguage(primary)) return primary;
    }
  }
  return DEFAULT_LANGUAGE;
}

/** El español va incluido en el paquete principal; los demás idiomas se descargan solo cuando se necesitan. */
export async function loadDictionary(lang: Lang): Promise<Dictionary> {
  switch (lang) {
    case 'en': return (await import('./en')).en;
    case 'ca': return (await import('./ca')).ca;
    default: return es;
  }
}

export function readStoredLanguage(): string | null {
  try { return localStorage.getItem(STORAGE_KEY); } catch { return null; }
}

/** Sustituye `{param}` y elige singular/plural cuando el texto trae dos formas separadas por `|` y se pasa `count`. */
export function formatMessage(template: string, params?: Record<string, string | number>): string {
  let text = template;
  if (params && typeof params['count'] === 'number' && text.includes('|')) {
    const [singular, plural] = text.split('|');
    text = params['count'] === 1 ? singular : plural ?? singular;
  }
  if (!params) return text;
  return text.replace(/\{(\w+)\}/g, (match, name: string) => (name in params ? String(params[name]) : match));
}

@Injectable({ providedIn: 'root' })
// Servicio de traducción en tiempo de ejecución. El idioma se fija al arrancar y cambiarlo recarga la página: así los textos,
// las fechas y los números de toda la aplicación son siempre coherentes sin tener que refrescar cada componente.
export class I18nService {
  private dictionary: Dictionary = es;
  private currentLang: Lang = DEFAULT_LANGUAGE;

  get lang(): Lang { return this.currentLang; }

  /** Se ejecuta antes de pintar la aplicación (ver main.ts). */
  async init(): Promise<void> {
    this.currentLang = detectLanguage(readStoredLanguage(), typeof navigator === 'undefined' ? undefined : navigator.languages ?? [navigator.language]);
    try {
      this.dictionary = await loadDictionary(this.currentLang);
    } catch {
      // Sin red para el chunk del idioma, la aplicación sigue en español en lugar de quedarse en blanco.
      this.dictionary = es;
      this.currentLang = DEFAULT_LANGUAGE;
    }
    if (typeof document !== 'undefined') document.documentElement.lang = this.currentLang;
  }

  setLanguage(lang: Lang, reload = true): void {
    if (!isSupportedLanguage(lang)) return;
    try { localStorage.setItem(STORAGE_KEY, lang); } catch { /* Sin almacenamiento se usa el idioma del navegador. */ }
    if (reload && typeof location !== 'undefined') location.reload();
  }

  t(key: TranslationKey, params?: Record<string, string | number>): string {
    return formatMessage(this.dictionary[key] ?? es[key] ?? key, params);
  }
}
