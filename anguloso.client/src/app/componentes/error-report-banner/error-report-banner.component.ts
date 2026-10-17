import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { AuthService } from '../../servicios/auth.service';
import { SupportService } from '../../servicios/support.service';
import { ErrorReportingService, ReportableApplicationError } from '../../servicios/error-reporting.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-error-report-banner',
  standalone: true,
  imports: [CommonModule],
  template: `
    <section *ngIf="!auth.isSuperAdmin() && errors.currentError() as error" class="error-report" role="alert" aria-live="assertive">
      <div class="error-report-copy">
        <strong>{{ error.message }}</strong>
        <span>Referencia {{ error.id }} · {{ error.status ? 'HTTP ' + error.status : 'Error del cliente' }}</span>
        <span *ngIf="failure()">{{ failure() }}</span>
      </div>
      <div class="error-report-actions">
        <button type="button" (click)="createTicket(error)" [disabled]="sending()">
          {{ sending() ? 'Creando…' : 'Informar a soporte' }}
        </button>
        <button type="button" class="secondary" (click)="errors.dismiss()" [disabled]="sending()">Cerrar</button>
      </div>
    </section>
  `,
  styles: [`
    .error-report { position: fixed; z-index: 11000; right: 16px; bottom: 16px; display: flex; flex-wrap: wrap; align-items: center; gap: 14px; max-width: min(680px, calc(100vw - 32px)); padding: 14px 16px; border: 1px solid #b91c1c; border-radius: 12px; background: #fff; color: #7f1d1d; box-shadow: 0 6px 24px #0003; font: 14px/1.4 system-ui, sans-serif; }
    .error-report-copy { display: flex; flex: 1 1 240px; flex-direction: column; gap: 3px; min-width: 0; overflow-wrap: anywhere; }
    .error-report-copy span { font-size: 12px; }
    .error-report-actions { display: flex; flex-wrap: wrap; gap: 8px; }
    .error-report-actions button { padding: 8px 11px; border: 1px solid #991b1b; border-radius: 7px; background: #991b1b; color: white; cursor: pointer; }
    .error-report-actions button.secondary { background: white; color: #7f1d1d; }
    .error-report-actions button:disabled { opacity: .65; cursor: wait; }
    @media (max-width: 520px) { .error-report { right: 8px; bottom: 8px; max-width: calc(100vw - 16px); } }
  `]
})
export class ErrorReportBannerComponent {
  readonly errors = inject(ErrorReportingService);
  readonly auth = inject(AuthService);
  private readonly support = inject(SupportService);
  readonly sending = signal(false);
  readonly failure = signal('');

  createTicket(error: ReportableApplicationError): void {
    if (this.auth.isSuperAdmin() || this.sending()) return;
    this.sending.set(true);
    this.failure.set('');
    const body = [
      'Se ha detectado un error desde la aplicación y el usuario ha solicitado ayuda.',
      '',
      'Identificador: ' + error.id,
      'Código: ' + error.code,
      'Fecha UTC: ' + error.occurredAt,
      'Módulo/ruta: ' + error.path,
      'Estado: ' + (error.status ? 'HTTP ' + error.status : 'error del cliente'),
      error.correlationId ? 'Identificador de correlación: ' + error.correlationId : 'Identificador de correlación: no disponible',
      '',
      'No se han adjuntado cuerpos de petición/respuesta ni datos de formularios.'
    ].join('\n');
    this.support.createTicket({
      subject: 'Error de aplicación ' + error.id,
      category: 'problem',
      priority: error.status >= 500 ? 'high' : 'normal',
      body
    }).subscribe({
      next: result => {
        this.sending.set(false);
        this.errors.dismiss();
        alert('Ticket #' + result.id + ' creado correctamente.');
      },
      error: () => {
        this.sending.set(false);
        this.failure.set('No se ha podido crear el ticket. Comprueba la conexión y vuelve a intentarlo.');
      }
    });
  }
}
