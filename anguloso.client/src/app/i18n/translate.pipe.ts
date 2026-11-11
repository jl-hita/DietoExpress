import { Pipe, PipeTransform, inject } from '@angular/core';
import { I18nService } from './i18n.service';
import type { TranslationKey } from './es';

/**
 * `{{ 'login.title' | t }}` o `{{ 'login.welcome' | t: { name: user } }}`.
 * La clave está tipada: con `strictTemplates`, una clave que no existe en es.ts no compila.
 * Es pura porque el idioma no cambia durante la vida de la página (cambiarlo recarga).
 */
@Pipe({ name: 't', standalone: true })
export class TranslatePipe implements PipeTransform {
  private readonly i18n = inject(I18nService);

  transform(key: TranslationKey, params?: Record<string, string | number>): string {
    return this.i18n.t(key, params);
  }
}
