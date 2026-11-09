import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, of } from 'rxjs';

export type PendingKind = 'review_request' | 'adaptation' | 'draft_block';
export interface PendingTrainingItem {
  kind: PendingKind; id: number; clientId: number; clientName: string; planId: number; planName: string; createdAt: string; detail?: string | null;
}

const KIND_LABELS: Record<PendingKind, string> = {
  review_request: 'Solicitud de revisión',
  adaptation: 'Adaptación por feedback',
  draft_block: 'Bloque sin revisar'
};

const ADAPTATION_LABELS: Record<string, string> = {
  reorganize_future_sessions: 'Reorganizar sesiones futuras',
  hold_progression_and_review: 'Pausar progresión y revisar recuperación',
  review_and_reduce_training_stress: 'Revisar y reducir carga',
  progress_according_to_prescription: 'Revisar progresión prescrita',
  pause_affected_exercise_and_review: 'Dolor registrado: revisión prioritaria',
  restore_volume: 'Recuperar una serie reducida',
  propose_deload: 'Valorar una semana de descarga'
};

/** Bandeja única de todo lo que espera al profesional en entrenamientos, de todos sus clientes. */
@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-training-pending-inbox',
  standalone: true,
  imports: [CommonModule],
  styles: [`
    :host { display: block; padding: 24px; max-width: 980px; margin: 0 auto; }
    h1 { margin: 0 0 4px; font-size: 1.5rem; }
    .lead { color: #64748b; margin: 0 0 16px; }
    .filters { display: flex; gap: 8px; flex-wrap: wrap; margin-bottom: 16px; }
    .filters button { border: 1px solid #cbd5e1; background: #fff; border-radius: 999px; padding: 6px 14px; cursor: pointer; font: inherit; }
    .filters button[aria-pressed="true"] { background: #0f766e; border-color: #0f766e; color: #fff; }
    .filters button:focus-visible, .row button:focus-visible { outline: 2px solid #0f766e; outline-offset: 2px; }
    .row { display: flex; align-items: center; gap: 12px; justify-content: space-between; padding: 12px 14px; margin-bottom: 8px;
      border: 1px solid #e2e8f0; border-radius: 12px; background: #fff; }
    .row strong { display: block; }
    .row small { color: #64748b; }
    .tag { display: inline-block; font-size: .75rem; padding: 2px 8px; border-radius: 999px; margin-right: 6px; background: #ccfbf1; color: #115e59; }
    .tag.adaptation { background: #fef3c7; color: #92400e; }
    .tag.draft_block { background: #e0e7ff; color: #3730a3; }
    .row button { border: 1px solid #0f766e; color: #0f766e; background: #fff; border-radius: 8px; padding: 8px 14px; cursor: pointer; font: inherit; white-space: nowrap; }
    .empty, .error { padding: 18px; border-radius: 12px; }
    .empty { background: #f8fafc; color: #475569; }
    .error { background: #fef2f2; color: #991b1b; }
  `],
  template: `
    <h1>Pendientes de entrenamiento</h1>
    <p class="lead">Solicitudes de revisión, adaptaciones propuestas por el feedback de tus clientes y bloques generados que aún no has revisado.</p>
    <div class="filters" role="group" aria-label="Filtrar por tipo">
      <button type="button" *ngFor="let option of filters" [attr.aria-pressed]="filter() === option.value" (click)="filter.set(option.value)">
        {{ option.label }} ({{ count(option.value) }})
      </button>
    </div>
    <p *ngIf="loading()" role="status">Cargando pendientes…</p>
    <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
    <p *ngIf="!loading() && !error() && !visible().length" class="empty">No tienes nada pendiente. Cuando un cliente solicite una revisión o llegue feedback relevante, aparecerá aquí.</p>
    <article class="row" *ngFor="let item of visible()">
      <div>
        <span class="tag" [ngClass]="item.kind">{{ kindLabel(item.kind) }}</span>
        <strong>{{ item.clientName }} · {{ item.planName }}</strong>
        <small>{{ describe(item) }} · {{ item.createdAt | date:'dd/MM/yyyy HH:mm' }}</small>
      </div>
      <button type="button" (click)="open(item)" [attr.aria-label]="'Abrir ' + kindLabel(item.kind) + ' de ' + item.clientName">Abrir</button>
    </article>
  `
})
export class TrainingPendingInboxComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  readonly items = signal<PendingTrainingItem[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');
  readonly filter = signal<PendingKind | 'all'>('all');
  readonly filters: Array<{ value: PendingKind | 'all'; label: string }> = [
    { value: 'all', label: 'Todo' },
    { value: 'review_request', label: 'Solicitudes' },
    { value: 'adaptation', label: 'Adaptaciones' },
    { value: 'draft_block', label: 'Bloques sin revisar' }
  ];
  readonly visible = computed(() => this.items().filter(item => this.filter() === 'all' || item.kind === this.filter()));

  ngOnInit(): void {
    this.http.get<PendingTrainingItem[]>('/api/training/pending-reviews')
      .pipe(catchError(() => { this.error.set('No se han podido cargar los pendientes. Inténtalo de nuevo en unos minutos.'); return of([] as PendingTrainingItem[]); }))
      .subscribe(items => { this.items.set(Array.isArray(items) ? items : []); this.loading.set(false); });
  }

  count(value: PendingKind | 'all'): number {
    return value === 'all' ? this.items().length : this.items().filter(item => item.kind === value).length;
  }

  kindLabel(kind: PendingKind): string { return KIND_LABELS[kind] ?? 'Pendiente'; }

  describe(item: PendingTrainingItem): string {
    if (item.kind === 'adaptation') return ADAPTATION_LABELS[item.detail ?? ''] ?? 'Revisión de adaptación';
    if (item.kind === 'draft_block') return item.detail ? 'Bloque «' + item.detail + '»' : 'Bloque generado';
    return item.detail ? item.detail : 'Sin nota del cliente';
  }

  open(item: PendingTrainingItem): void {
    const query: Record<string, string | number> = { plan: item.planId };
    if (item.kind === 'review_request') query['request'] = item.id;
    this.router.navigate(['/clients', item.clientId, 'training'], { queryParams: query });
  }
}
