import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { LanguageSwitcherComponent } from '../../i18n/language-switcher.component';
import { TranslatePipe } from '../../i18n/translate.pipe';
import type { TranslationKey } from '../../i18n/es';
import { PatientAppService } from './patient-app.service';

interface NavItem { path: string; icon: string; label: TranslationKey; exact: boolean; }

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-patient-app-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatIconModule, TranslatePipe, LanguageSwitcherComponent],
  template: `
    <div class="pa-shell">
      <header class="pa-top">
        <span class="pa-brand">DietoExpress</span>
        <div class="pa-top-actions">
          <app-language-switcher></app-language-switcher>
          <button type="button" class="pa-link" (click)="logout()">{{ 'papp.logout' | t }}</button>
        </div>
      </header>

      <main class="pa-content">
        @if (loadError()) {
          <div class="pa-banner" role="alert">
            <span>{{ 'papp.error' | t }}</span>
            <button type="button" class="pa-link" (click)="load()">{{ 'papp.retry' | t }}</button>
          </div>
        }
        <router-outlet></router-outlet>
      </main>

      <nav class="pa-nav" [attr.aria-label]="'papp.nav.label' | t">
        @for (item of items; track item.path) {
          <a [routerLink]="item.path ? ['/portal', item.path] : ['/portal']" routerLinkActive="active" [routerLinkActiveOptions]="{ exact: item.exact }" #link="routerLinkActive" [attr.aria-current]="link.isActive ? 'page' : null">
            <mat-icon aria-hidden="true">{{ item.icon }}</mat-icon>
            <span>{{ item.label | t }}</span>
          </a>
        }
      </nav>
    </div>
  `,
  styles: [`
    :host { display: block; --pa-forest: #176B52; --pa-forest-dark: #104B3B; --pa-sage: #E7F2EC; --pa-canvas: #F5F8F6; --pa-ink: #172923; --pa-muted: #687970; --pa-line: #E2EAE5; --pa-amber: #94651B; }
    .pa-shell { min-height: 100vh; background: var(--pa-canvas); color: var(--pa-ink); font-family: 'Outfit', 'Inter', system-ui, sans-serif; }
    .pa-top { position: sticky; top: 0; z-index: 5; display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 12px 20px; background: #fff; border-bottom: 1px solid var(--pa-line); }
    .pa-brand { font-weight: 800; letter-spacing: .3px; color: var(--pa-forest); }
    .pa-top-actions { display: flex; align-items: center; gap: 12px; }
    .pa-link { border: 0; background: transparent; color: var(--pa-muted); font: inherit; font-size: 14px; cursor: pointer; padding: 6px 4px; }
    .pa-link:hover { color: var(--pa-forest); }
    .pa-content { max-width: 720px; margin: 0 auto; padding: 20px 20px 104px; box-sizing: border-box; }
    .pa-banner { display: flex; justify-content: space-between; align-items: center; gap: 12px; margin-bottom: 16px; padding: 12px 16px; border-radius: 14px; background: #FFF4E0; color: var(--pa-amber); font-size: 14px; }
    .pa-nav { position: fixed; left: 0; right: 0; bottom: 0; z-index: 6; display: flex; background: #fff; border-top: 1px solid var(--pa-line); padding-bottom: env(safe-area-inset-bottom); }
    .pa-nav a { flex: 1; display: flex; flex-direction: column; align-items: center; gap: 2px; padding: 8px 4px 10px; color: var(--pa-muted); text-decoration: none; font-size: 11.5px; font-weight: 600; }
    .pa-nav a.active { color: var(--pa-forest); }
    .pa-nav a.active mat-icon { background: var(--pa-sage); }
    .pa-nav mat-icon { width: 52px; height: 28px; border-radius: 14px; display: grid; place-items: center; font-size: 22px; }
    .pa-nav a:focus-visible, .pa-link:focus-visible { outline: 2px solid var(--pa-forest); outline-offset: 2px; border-radius: 8px; }
    /* En pantallas anchas la navegación pasa a un lateral para no estirar la barra inferior. */
    @media (min-width: 900px) {
      .pa-content { margin-left: 220px; max-width: 760px; padding-bottom: 40px; }
      .pa-top { padding-left: 240px; }
      .pa-nav { top: 0; right: auto; width: 200px; flex-direction: column; border-top: 0; border-right: 1px solid var(--pa-line); padding: 72px 12px 0; gap: 4px; }
      .pa-nav a { flex: none; flex-direction: row; gap: 12px; padding: 10px 12px; border-radius: 14px; font-size: 14px; }
      .pa-nav a.active { background: var(--pa-sage); }
      .pa-nav mat-icon, .pa-nav a.active mat-icon { width: 24px; height: 24px; background: transparent; }
    }
  `]
})
// Estructura de la nueva interfaz del paciente: cabecera, contenido y la misma navegación inferior de cinco accesos que la app Android.
export class PatientAppShellComponent implements OnInit {
  private readonly app = inject(PatientAppService);
  private readonly router = inject(Router);

  readonly loadError = signal(false);

  readonly items: readonly NavItem[] = [
    { path: '', icon: 'home', label: 'papp.nav.home', exact: true },
    { path: 'diet', icon: 'restaurant_menu', label: 'papp.nav.diet', exact: false },
    { path: 'training', icon: 'fitness_center', label: 'papp.nav.training', exact: false },
    { path: 'progress', icon: 'trending_up', label: 'papp.nav.progress', exact: false },
    { path: 'appointments', icon: 'event', label: 'papp.nav.appointments', exact: false }
  ];

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loadError.set(false);
    this.app.loadProfile().subscribe({
      error: err => {
        // Sin sesión de paciente se entra por el portal actual (enlace del email o PIN); aquí no hay formulario de acceso todavía.
        if (err?.status === 401 || err?.status === 403) void this.router.navigate(['/patient']);
        else this.loadError.set(true);
      }
    });
  }

  logout(): void {
    this.app.logout().subscribe({ complete: () => void this.router.navigate(['/patient']), error: () => void this.router.navigate(['/patient']) });
  }
}
