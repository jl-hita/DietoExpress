import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, EventEmitter, Input, OnChanges, Output, SimpleChanges, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { catchError, of } from 'rxjs';

export interface TrainingReviewRequestItem {
  id: number; planId: number; planName: string; planVersion: number; note?: string | null; status: string; createdAt: string;
}

/**
 * Solicitudes de revisión manual de rutina de un cliente. Es autónomo (carga y resuelve por sí mismo) para que la
 * pantalla de planificación no tenga que conocer su estado; solo recibe el cliente y avisa de qué rutina abrir.
 * Resolver una solicitud nunca modifica sesiones ni plan: el profesional edita la rutina por separado.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-training-review-requests-panel',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="panel review-requests-panel" aria-labelledby="review-requests-title">
      <div class="section-heading">
        <div><h2 id="review-requests-title">Solicitudes de revisión manual</h2>
          <p>El cliente pide que revises su rutina. La solicitud no cambia el plan por sí sola y las sesiones pasadas nunca se modifican.</p></div>
        <span class="status">{{ items().length }} pendientes</span>
      </div>
      <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
      <p *ngIf="notice()" class="success" role="status">{{ notice() }}</p>
      <p *ngIf="!items().length" class="muted">No hay solicitudes pendientes.</p>
      <article *ngFor="let item of items()" class="template-row" [class.is-highlighted]="item.id === highlightRequestId">
        <div class="adaptation-proposal-content">
          <strong>{{ item.planName }}{{ item.planVersion > 1 ? ' · revisión ' + item.planVersion : '' }}</strong>
          <p>Solicitada el {{ item.createdAt | date:'dd/MM/yyyy HH:mm' }}</p>
          <p *ngIf="item.note">{{ item.note }}</p>
          <label>Nota de respuesta (opcional)
            <textarea [name]="'reviewRequestNotes' + item.id" [ngModel]="notes()[item.id] ?? ''" (ngModelChange)="setNote(item.id, $event)" rows="2" maxlength="2000"></textarea>
          </label>
        </div>
        <div class="cycle-actions">
          <button type="button" (click)="openPlan.emit(item.planId)" [attr.aria-label]="'Abrir la rutina ' + item.planName">Abrir rutina</button>
          <button type="button" (click)="resolve(item, 'attended')" [disabled]="busy()">Marcar como atendida</button>
          <button type="button" class="secondary" (click)="resolve(item, 'rejected')" [disabled]="busy()">Rechazar</button>
        </div>
      </article>
    </section>
  `
})
export class TrainingReviewRequestsPanelComponent implements OnChanges {
  private readonly http = inject(HttpClient);
  @Input({ required: true }) clientId = 0;
  @Input() highlightRequestId: number | null = null;
  @Output() openPlan = new EventEmitter<number>();
  @Output() resolved = new EventEmitter<void>();

  readonly items = signal<TrainingReviewRequestItem[]>([]);
  readonly notes = signal<Record<number, string>>({});
  readonly busy = signal(false);
  readonly error = signal('');
  readonly notice = signal('');

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['clientId'] && this.clientId > 0) this.load();
  }

  load(): void {
    this.http.get<TrainingReviewRequestItem[]>('/api/training/clients/' + this.clientId + '/review-requests')
      .pipe(catchError(() => { this.error.set('No se han podido cargar las solicitudes de revisión.'); return of([] as TrainingReviewRequestItem[]); }))
      .subscribe(items => this.items.set(Array.isArray(items) ? items.filter(item => !!item && Number.isInteger(item.id)) : []));
  }

  setNote(id: number, value: string): void {
    this.notes.update(current => ({ ...current, [id]: value }));
  }

  resolve(item: TrainingReviewRequestItem, decision: 'attended' | 'rejected'): void {
    if (!confirm('¿Quieres marcar esta solicitud como ' + (decision === 'attended' ? 'atendida' : 'rechazada') + '? No se modificará ninguna sesión.')) return;
    this.busy.set(true);
    this.error.set('');
    this.http.post<{ message?: string }>('/api/training/review-requests/' + item.id + '/resolve', {
      decision, notes: this.notes()[item.id] ?? ''
    }).subscribe({
      next: result => { this.notice.set(result.message ?? 'Solicitud actualizada.'); this.busy.set(false); this.load(); this.resolved.emit(); },
      error: e => { this.error.set(e?.error?.message || 'No se pudo actualizar la solicitud.'); this.busy.set(false); this.load(); }
    });
  }
}
