import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { I18nService, SUPPORTED_LANGUAGES, isSupportedLanguage } from './i18n.service';
import { TranslatePipe } from './translate.pipe';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-language-switcher',
  standalone: true,
  imports: [TranslatePipe],
  template: `
    <label class="language-switcher">
      <span class="visually-hidden">{{ 'common.language' | t }}</span>
      <select [attr.aria-label]="'common.language' | t" (change)="change($any($event.target).value)">
        @for (language of languages; track language.code) {
          <option [value]="language.code" [selected]="language.code === i18n.lang" [attr.lang]="language.code">{{ language.label }}</option>
        }
      </select>
    </label>
  `,
  styles: [`
    .language-switcher select { font: inherit; font-size: 13px; padding: 6px 28px 6px 10px; border: 1px solid #cbd5e1; border-radius: 10px; background: #fff; color: #0f172a; cursor: pointer; }
    .language-switcher select:focus-visible { outline: 2px solid #0d9488; outline-offset: 2px; }
    .visually-hidden { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
  `]
})
// Selector de idioma accesible (un <select> nativo). Cambiar de idioma recarga la página.
export class LanguageSwitcherComponent {
  protected readonly i18n = inject(I18nService);
  protected readonly languages = SUPPORTED_LANGUAGES;

  protected change(value: string): void {
    if (isSupportedLanguage(value) && value !== this.i18n.lang) this.i18n.setLanguage(value);
  }
}
