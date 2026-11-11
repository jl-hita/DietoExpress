import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { catchError, forkJoin, of } from 'rxjs';
import { I18nService } from '../../i18n/i18n.service';
import { TranslatePipe } from '../../i18n/translate.pipe';
import type { TranslationKey } from '../../i18n/es';
import { PatientAppService } from './patient-app.service';
import type { PatientAppointment, TrainingSession } from './patient-app.models';
import { firstName, formatDateLabel, formatTimeLabel, isToday, pickNextAppointment, pickNextTraining } from './patient-app.logic';

interface Shortcut { path: string; icon: string; title: TranslationKey; hint: TranslationKey; }

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-patient-home',
  standalone: true,
  imports: [RouterLink, MatIconModule, TranslatePipe],
  template: `
    <h1 class="home-title">
      @if (name()) { {{ 'papp.home.greeting' | t: { name: name() } }} } @else { {{ 'papp.home.greetingAnonymous' | t }} }
    </h1>

    @if (loading()) {
      <p class="muted">{{ 'papp.loading' | t }}</p>
    }

    @if (training(); as session) {
      <a class="card training" [routerLink]="['/portal', 'training']" [attr.aria-label]="session.title">
        <span class="badge" aria-hidden="true"><mat-icon>fitness_center</mat-icon></span>
        <span class="info">
          <small>{{ (trainingIsToday() ? 'papp.home.trainingToday' : 'papp.home.trainingNext') | t }}</small>
          <strong>{{ session.title }}</strong>
          <span>{{ dateLabel(session.scheduledDate) }} · {{ session.durationMinutes ? ('papp.minutes' | t: { count: session.durationMinutes }) : session.sessionType }}</span>
        </span>
        <span class="go">{{ (trainingIsToday() ? 'papp.home.start' : 'papp.home.view') | t }}</span>
      </a>
    }

    @if (appointment(); as next) {
      <a class="card" [routerLink]="['/portal', 'appointments']">
        <span class="badge light" aria-hidden="true"><mat-icon>event</mat-icon></span>
        <span class="info">
          <small class="muted">{{ 'papp.home.nextAppointment' | t }}</small>
          <strong>{{ dateLabel(next.startsAt) }}</strong>
          <span class="muted">{{ timeLabel(next.startsAt) }} · {{ modality(next) || ('papp.home.consultation' | t) }}</span>
        </span>
        <span class="go dark">{{ 'papp.home.view' | t }}</span>
      </a>
    }

    <h2 class="section-title">{{ 'papp.home.quickAccess' | t }}</h2>
    <div class="shortcuts">
      @for (shortcut of shortcuts; track shortcut.path) {
        <a class="card shortcut" [class.wide]="$last && shortcuts.length % 2 === 1" [routerLink]="['/portal', shortcut.path]">
          <span class="badge light" aria-hidden="true"><mat-icon>{{ shortcut.icon }}</mat-icon></span>
          <strong>{{ shortcut.title | t }}</strong>
          <small class="muted">{{ shortcut.hint | t }}</small>
        </a>
      }
    </div>
  `,
  styles: [`
    :host { display: block; }
    .home-title { margin: 4px 0 18px; font-size: 28px; line-height: 1.15; letter-spacing: -.5px; color: var(--pa-ink); }
    .section-title { margin: 26px 0 12px; font-size: 20px; color: var(--pa-ink); }
    .muted { color: var(--pa-muted); }
    .card { display: flex; align-items: center; gap: 12px; padding: 18px; margin-bottom: 14px; background: #fff; border: 1px solid var(--pa-line); border-radius: 22px; text-decoration: none; color: var(--pa-ink); }
    .card:hover { border-color: #cfe0d6; }
    .card:focus-visible { outline: 2px solid var(--pa-forest); outline-offset: 2px; }
    .card.training { background: var(--pa-forest); border-color: var(--pa-forest); color: #fff; }
    .badge { flex: none; width: 48px; height: 48px; border-radius: 15px; display: grid; place-items: center; background: #2C8066; color: #fff; }
    .badge.light { background: var(--pa-sage); color: var(--pa-forest); }
    .info { flex: 1; display: flex; flex-direction: column; gap: 3px; min-width: 0; }
    .info small { font-size: 12px; }
    .info strong { font-size: 17px; }
    .card.training .info small { color: #C8E4D4; }
    .card.training .info span { color: #E2F0E8; font-size: 14px; }
    .go { font-weight: 700; white-space: nowrap; }
    .go.dark { color: var(--pa-forest); }
    .shortcuts { display: grid; grid-template-columns: repeat(2, 1fr); gap: 12px; }
    .shortcut { flex-direction: column; align-items: flex-start; gap: 6px; margin: 0; padding: 16px; }
    .shortcut.wide { grid-column: 1 / -1; }
    .shortcut strong { font-size: 16px; }
    .shortcut small { font-size: 12.5px; line-height: 1.35; }
  `]
})
// Inicio: saludo, próximo entrenamiento, próxima cita y accesos rápidos, igual que la pantalla de inicio de la app Android.
export class PatientHomeComponent implements OnInit {
  private readonly app = inject(PatientAppService);
  private readonly i18n = inject(I18nService);

  readonly loading = signal(true);
  readonly appointment = signal<PatientAppointment | null>(null);
  readonly training = signal<TrainingSession | null>(null);
  readonly name = computed(() => firstName(this.app.profile()?.fullName));
  readonly trainingIsToday = computed(() => { const session = this.training(); return !!session && isToday(session.scheduledDate); });

  readonly shortcuts: readonly Shortcut[] = [
    { path: 'appointments', icon: 'event', title: 'papp.home.appointments', hint: 'papp.home.appointmentsHint' },
    { path: 'documents', icon: 'description', title: 'papp.home.documents', hint: 'papp.home.documentsHint' },
    { path: 'shopping', icon: 'shopping_cart', title: 'papp.home.shopping', hint: 'papp.home.shoppingHint' },
    { path: 'messages', icon: 'chat', title: 'papp.home.messages', hint: 'papp.home.messagesHint' },
    { path: 'profile', icon: 'person', title: 'papp.home.profile', hint: 'papp.home.profileHint' }
  ];

  ngOnInit(): void {
    // Un fallo en una de las dos consultas no debe ocultar la otra.
    forkJoin({
      appointments: this.app.appointments().pipe(catchError(() => of(null))),
      training: this.app.trainingSessions().pipe(catchError(() => of(null)))
    }).subscribe(({ appointments, training }) => {
      this.appointment.set(pickNextAppointment(appointments));
      this.training.set(pickNextTraining(training));
      this.loading.set(false);
    });
  }

  dateLabel(value: string): string { return formatDateLabel(value, this.i18n.lang); }
  timeLabel(value: string): string { return formatTimeLabel(value, this.i18n.lang); }
  modality(appointment: PatientAppointment): string {
    const value = appointment.modality?.trim();
    return value ? value.charAt(0).toUpperCase() + value.slice(1) : '';
  }
}
