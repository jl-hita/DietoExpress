import { Component, Input, ChangeDetectionStrategy, signal } from '@angular/core';
import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { PatientDietHistoryEntry, PatientPortalService } from '../../../servicios/patient-portal.service';
import { FoodService } from '../../../servicios/food.service';
import { SumPipe } from '../../../shared/pipes/sum.pipe';

/** Historial de planes del paciente (solo lectura). Se carga al desplegarlo para no penalizar el plan de hoy. */
@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-patient-diet-history',
  standalone: true,
  imports: [CommonModule, DatePipe, DecimalPipe, MatIconModule, SumPipe],
  templateUrl: './patient-diet-history.component.html',
  styleUrls: ['./patient-diet-history.component.css']
})
export class PatientDietHistoryComponent {
  /** Solo en la vista previa del profesional; el paciente se identifica por su sesión. */
  @Input() previewClientId?: number;

  readonly dayNames = ['Lunes', 'Martes', 'Miércoles', 'Jueves', 'Viernes', 'Sábado', 'Domingo'];
  readonly open = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly entries = signal<PatientDietHistoryEntry[] | null>(null);
  readonly selectedId = signal<number | null>(null);
  readonly detail = signal<any>(null);
  readonly detailError = signal('');
  /** Línea de intercambio desplegada y alimentos equivalentes ya consultados, por grupo. */
  readonly expandedItemId = signal<number | null>(null);
  readonly equivalents = signal<Record<number, any[]>>({});
  readonly equivalentsError = signal(false);

  constructor(private portalService: PatientPortalService, private foodService: FoodService) {}

  toggle(): void {
    this.open.set(!this.open());
    // Se vuelve a pedir en cada apertura: el profesional puede haber cambiado el plan durante la sesión.
    if (this.open()) this.load();
    else this.clearSelection();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.portalService.getMyDietHistory(this.previewClientId).subscribe({
      next: entries => { this.entries.set(entries || []); this.loading.set(false); },
      error: () => { this.error.set('No hemos podido cargar tu historial de planes.'); this.loading.set(false); }
    });
  }

  select(assignmentId: number): void {
    if (this.selectedId() === assignmentId && !this.detailError()) { this.clearSelection(); return; }
    this.clearSelection();
    this.selectedId.set(assignmentId);
    this.portalService.getMyDietHistoryDetail(assignmentId, this.previewClientId).subscribe({
      // Una respuesta tardía de otro plan no sustituye al que está seleccionado.
      next: diet => { if (this.selectedId() === assignmentId) this.detail.set(diet); },
      error: () => { if (this.selectedId() === assignmentId) this.detailError.set('No hemos podido cargar este plan.'); }
    });
  }

  toggleEquivalents(itemId: number, groupId: number): void {
    if (this.expandedItemId() === itemId && !this.equivalentsError()) { this.expandedItemId.set(null); return; }
    this.expandedItemId.set(itemId);
    this.equivalentsError.set(false);
    if (this.equivalents()[groupId]) return;
    this.foodService.getFoodsInExchangeGroup(groupId).subscribe({
      next: foods => this.equivalents.update(cache => ({ ...cache, [groupId]: foods || [] })),
      error: () => { if (this.expandedItemId() === itemId) this.equivalentsError.set(true); }
    });
  }

  private clearSelection(): void {
    this.expandedItemId.set(null);
    this.equivalentsError.set(false);
    this.selectedId.set(null);
    this.detail.set(null);
    this.detailError.set('');
  }
}
