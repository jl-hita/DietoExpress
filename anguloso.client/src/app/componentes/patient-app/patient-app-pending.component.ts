import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslatePipe } from '../../i18n/translate.pipe';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-patient-app-pending',
  standalone: true,
  imports: [RouterLink, TranslatePipe],
  template: `
    <section class="pending">
      <h1>{{ 'papp.pending.title' | t }}</h1>
      <p>{{ 'papp.pending.text' | t }}</p>
      <a class="open" routerLink="/patient">{{ 'papp.pending.open' | t }}</a>
    </section>
  `,
  styles: [`
    .pending { padding: 28px 22px; background: #fff; border: 1px solid var(--pa-line); border-radius: 22px; }
    h1 { margin: 0 0 10px; font-size: 20px; color: var(--pa-ink); }
    p { margin: 0 0 18px; color: var(--pa-muted); line-height: 1.5; }
    .open { display: inline-block; padding: 12px 20px; border-radius: 14px; background: var(--pa-forest); color: #fff; font-weight: 700; text-decoration: none; }
    .open:focus-visible { outline: 2px solid var(--pa-forest-dark); outline-offset: 2px; }
  `]
})
// Marcador de las secciones que todavía no se han trasladado a la nueva interfaz: remite al portal actual para no dejar un callejón sin salida.
export class PatientAppPendingComponent { }
