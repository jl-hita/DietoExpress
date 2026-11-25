import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, Input, OnChanges, SimpleChanges, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { AndroidTesterStatus, AndroidTestersService } from '../../servicios/android-testers.service';

/** Solicitud de alta del paciente en la prueba cerrada de la app Android. */
@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-android-tester-card',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule],
  template: `
    <div class="card">
      <div class="title"><mat-icon>android</mat-icon><h3>Prueba de la app Android</h3></div>
      <p class="help">
        Solicita el alta del paciente en la prueba de la app. Indica el correo de la cuenta de Google que usa en Google Play
        (normalmente Gmail). Cuando esté dado de alta recibirá un email con el enlace para participar.
      </p>

      @if (request(); as current) {
        <p class="status">
          @if (current.status === 'invited') {
            ✓ Invitación enviada a <strong>{{ current.googleEmail }}</strong> el {{ current.invitedAt | date:'dd/MM/yyyy' }}.
          } @else {
            Solicitud pendiente para <strong>{{ current.googleEmail }}</strong> desde el {{ current.requestedAt | date:'dd/MM/yyyy' }}.
          }
        </p>
      }

      <div class="fields">
        <mat-form-field appearance="outline" class="email">
          <mat-label>Cuenta de Google del paciente</mat-label>
          <input matInput type="email" maxlength="254" [ngModel]="email()" (ngModelChange)="email.set($event)">
        </mat-form-field>
        <mat-checkbox [ngModel]="consent()" (ngModelChange)="consent.set($event)">
          El paciente consiente que su correo se comparta con DietoExpress y Google para participar en la prueba.
        </mat-checkbox>
        <button mat-flat-button color="primary" type="button" (click)="submit()"
                [disabled]="saving() || !consent() || !email().trim() || email().trim().toLowerCase() === request()?.googleEmail">
          <mat-icon>send</mat-icon> {{ request() ? 'Solicitar con otro correo' : 'Solicitar acceso' }}
        </button>
      </div>
      @if (message()) { <p class="help" role="status">{{ message() }}</p> }
    </div>
  `,
  styles: [`
    .card { background:#f8fafc; border:1px solid #e2e8f0; border-radius:12px; padding:20px; }
    .title { display:flex; align-items:center; gap:10px; margin-bottom:8px; }
    .title mat-icon { color:#0d9488; }
    h3 { margin:0; font-size:16px; font-weight:700; color:#0f172a; }
    .help { margin:0 0 16px; font-size:13px; color:#64748b; line-height:1.5; }
    .status { margin:0 0 16px; font-size:13px; color:#0f172a; }
    .fields { display:flex; flex-direction:column; align-items:flex-start; gap:8px; }
    .email { width:100%; max-width:380px; }
  `]
})
export class AndroidTesterCardComponent implements OnChanges {
  @Input({ required: true }) clientId!: number;
  @Input() clientEmail = '';

  private readonly testers = inject(AndroidTestersService);
  readonly request = signal<AndroidTesterStatus | null>(null);
  readonly email = signal('');
  readonly consent = signal(false);
  readonly saving = signal(false);
  readonly message = signal('');

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['clientId']) {
      const clientId = this.clientId;
      this.request.set(null);
      this.email.set(this.clientEmail);
      this.consent.set(false);
      this.message.set('');
      this.testers.getForClient(clientId).subscribe({
        next: ({ request }) => {
          if (clientId !== this.clientId) return;
          this.request.set(request);
          if (request) this.email.set(request.googleEmail);
        },
        error: () => this.message.set('No se ha podido consultar el estado de la prueba.')
      });
    } else if (changes['clientEmail'] && !this.request() && (!this.email() || this.email() === changes['clientEmail'].previousValue)) {
      // La ficha llega después que el identificador; no se pisa lo que ya se haya escrito.
      this.email.set(this.clientEmail);
    }
  }

  submit(): void {
    this.saving.set(true);
    this.message.set('');
    this.testers.requestForClient(this.clientId, this.email().trim(), this.consent()).subscribe({
      next: ({ request }) => {
        this.saving.set(false);
        this.request.set(request);
        this.email.set(request.googleEmail);
        this.consent.set(false);
        this.message.set('Solicitud enviada.');
      },
      error: err => {
        this.saving.set(false);
        this.message.set(err?.error?.message || 'No se ha podido enviar la solicitud.');
      }
    });
  }
}
