import { describe, expect, it, beforeEach } from "vitest";
import { TestBed } from '@angular/core/testing';

import { es } from './es';
import { en } from './en';
import { ca } from './ca';
import { I18nService, detectLanguage, formatMessage, isSupportedLanguage, loadDictionary } from './i18n.service';

const placeholders = (text: string) => (text.match(/\{\w+\}/g) ?? []).sort().join(',');

describe('i18n dictionaries', () => {
    const keys = Object.keys(es);

    it('define the same keys in every language (a missing translation must never reach the screen)', () => {
        expect(Object.keys(en).sort()).toEqual([...keys].sort());
        expect(Object.keys(ca).sort()).toEqual([...keys].sort());
    });

    it('never leave a translation empty', () => {
        for (const [lang, dictionary] of Object.entries({ es, en, ca })) {
            for (const [key, value] of Object.entries(dictionary)) {
                expect(value.trim().length, `${lang}:${key}`).toBeGreaterThan(0);
            }
        }
    });

    it('keep the same {placeholders} in every language', () => {
        for (const key of keys as (keyof typeof es)[]) {
            expect(placeholders(en[key]), `en:${key}`).toBe(placeholders(es[key]));
            expect(placeholders(ca[key]), `ca:${key}`).toBe(placeholders(es[key]));
        }
    });

    it('actually translate the text instead of copying the Spanish source', () => {
        const identicalAllowed = new Set(['patient.header.notifications', 'common.language', 'papp.minutes']);
        const untranslated = (keys as (keyof typeof es)[]).filter(key => !identicalAllowed.has(key) && en[key] === es[key]);
        expect(untranslated).toEqual([]);
    });

    it('lazy-load every non-default dictionary', async () => {
        expect(await loadDictionary('es')).toBe(es);
        expect(await loadDictionary('en')).toBe(en);
        expect(await loadDictionary('ca')).toBe(ca);
    });
});

describe('detectLanguage', () => {
    it('prefers the language the user chose', () => {
        expect(detectLanguage('ca', ['en-US'], true)).toBe('ca');
        expect(detectLanguage('en', [], false)).toBe('en');
    });

    it('ignores unknown stored values', () => {
        expect(detectLanguage('fr', ['es-ES'], true)).toBe('es');
        expect(detectLanguage('<script>', undefined, true)).toBe('es');
    });

    it('uses the browser language only when automatic detection is on', () => {
        expect(detectLanguage(null, ['ca-ES', 'es'], true)).toBe('ca');
        expect(detectLanguage(null, ['en-GB'], true)).toBe('en');
        expect(detectLanguage(null, ['fr-FR'], true)).toBe('es');
        expect(detectLanguage(null, ['en-GB'], false)).toBe('es');
    });

    it('does not auto-detect yet while the translation covers only part of the app', () => {
        // Cuando la traducción esté completa se activa AUTO_DETECT_BROWSER_LANGUAGE y este test debe actualizarse.
        expect(detectLanguage(null, ['en-US'])).toBe('es');
    });

    it('recognizes only the supported languages', () => {
        expect(isSupportedLanguage('es')).toBe(true);
        expect(isSupportedLanguage('en')).toBe(true);
        expect(isSupportedLanguage('ca')).toBe(true);
        expect(isSupportedLanguage('fr')).toBe(false);
        expect(isSupportedLanguage(undefined)).toBe(false);
    });
});

describe('formatMessage', () => {
    it('replaces named parameters and leaves unknown ones visible', () => {
        expect(formatMessage('Hola {name}', { name: 'Ana' })).toBe('Hola Ana');
        expect(formatMessage('Hola {name}', {})).toBe('Hola {name}');
        expect(formatMessage('Sin parámetros')).toBe('Sin parámetros');
    });

    it('picks singular or plural from a "one|other" text when count is given', () => {
        expect(formatMessage('{count} día|{count} días', { count: 1 })).toBe('1 día');
        expect(formatMessage('{count} día|{count} días', { count: 3 })).toBe('3 días');
        expect(formatMessage('{count} día|{count} días', { count: 0 })).toBe('0 días');
    });

    it('does not interpret HTML or special regexp characters in values', () => {
        expect(formatMessage('Hola {name}', { name: '<b>$&</b>' })).toBe('Hola <b>$&</b>');
    });
});

describe('I18nService', () => {
    let service: I18nService;

    beforeEach(() => {
        TestBed.configureTestingModule({});
        service = TestBed.inject(I18nService);
    });

    it('translates into Spanish by default, with parameters', () => {
        expect(service.t('login.title')).toBe('Inicia sesión');
        expect(service.t('login.welcome', { name: 'Ana' })).toBe('Bienvenido Ana');
        expect(service.lang).toBe('es');
    });

    it('stores the chosen language without reloading when asked not to', () => {
        service.setLanguage('en', false);
        expect(localStorage.getItem('dietoexpress_lang')).toBe('en');
        localStorage.removeItem('dietoexpress_lang');
    });

    it('rejects unsupported languages', () => {
        service.setLanguage('fr' as never, false);
        expect(localStorage.getItem('dietoexpress_lang')).toBeNull();
    });
});
