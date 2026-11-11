import { CommonModule } from '@angular/common';
import { Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';

export interface ConsultationPdfDialogData {
  plans: Array<{ id: number; name: string; startDate: string; endDate?: string | null }>;
}
export interface ConsultationPdfDialogResult { includeTraining: boolean; startDate: string; endDate: string; }

const MAX_DAYS = 366;

/** Formulario previo a generar el informe de consulta cuando el cliente tiene planificación de entrenamiento. */
@Component({
  selector: 'app-consultation-pdf-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Informe de consulta</h2>
    <mat-dialog-content>
      <p class="intro">Este paciente tiene planificación de entrenamiento. Elige qué periodo de rutinas se incluirá en el PDF.</p>
      <label class="check"><input type="checkbox" [(ngModel)]="includeTraining" name="includeTraining"> Incluir la planificación de entrenamiento</label>
      <div class="fields" [class.disabled]="!includeTraining">
        <label>Desde<input type="date" [(ngModel)]="startDate" name="trainingFrom" [disabled]="!includeTraining" [attr.max]="endDate || null"></label>
        <label>Hasta<input type="date" [(ngModel)]="endDate" name="trainingTo" [disabled]="!includeTraining" [attr.min]="startDate || null"></label>
      </div>
      <div class="presets" *ngIf="includeTraining">
        <button type="button" (click)="nextFourWeeks()">Próximas 4 semanas</button>
        <button type="button" (click)="fullPlan()" [disabled]="!fullPlanFits" [title]="fullPlanFits ? '' : 'El plan supera los 366 días permitidos'">Plan completo</button>
      </div>
      <p class="error" *ngIf="includeTraining && error" role="alert">{{ error }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button type="button" (click)="cancel()">Cancelar</button>
      <button mat-flat-button color="primary" type="button" (click)="confirm()" [disabled]="includeTraining && !!error">Generar PDF</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .intro{margin:0 0 12px;color:#475569}
    .check{display:flex;align-items:center;gap:8px;margin-bottom:12px;cursor:pointer}
    .fields{display:grid;grid-template-columns:1fr 1fr;gap:12px}
    .fields.disabled{opacity:.5}
    .fields label{display:grid;gap:4px;font-size:13px;font-weight:600}
    .fields input{padding:9px;border:1px solid #cbd5e1;border-radius:8px;font:inherit}
    .presets{display:flex;gap:8px;flex-wrap:wrap;margin-top:10px}
    .presets button{padding:6px 12px;border:1px solid #99f6e4;background:#f0fdfa;color:#0f766e;border-radius:999px;font:inherit;font-size:13px;cursor:pointer}
    .presets button:disabled{opacity:.5;cursor:default}
    .error{margin:10px 0 0;color:#b45309;font-size:13px}
    @media (max-width:480px){.fields{grid-template-columns:1fr}}
  `]
})
export class ConsultationPdfDialogComponent {
  includeTraining = true;
  startDate: string;
  endDate: string;

  constructor(private readonly ref: MatDialogRef<ConsultationPdfDialogComponent, ConsultationPdfDialogResult | undefined>,
              @Inject(MAT_DIALOG_DATA) private readonly data: ConsultationPdfDialogData) {
    const period = this.fourWeeksFromThisMonday();
    this.startDate = period.start;
    this.endDate = period.end;
  }

  get error(): string {
    if (!this.startDate || !this.endDate) return 'Indica las dos fechas.';
    if (this.endDate < this.startDate) return 'La fecha final no puede ser anterior a la inicial.';
    if (this.days(this.startDate, this.endDate) > MAX_DAYS) return 'El periodo no puede superar 366 días.';
    return '';
  }

  private get planBounds(): { start: string; end: string } | null {
    const plans = this.data?.plans ?? [];
    if (!plans.length) return null;
    const start = plans.map(plan => plan.startDate.slice(0, 10)).sort()[0];
    const ends = plans.map(plan => plan.endDate ? plan.endDate.slice(0, 10) : null);
    const end = ends.includes(null) ? null : (ends as string[]).sort().slice(-1)[0];
    return { start, end: end ?? start };
  }
  get fullPlanFits(): boolean {
    const bounds = this.planBounds;
    return !!bounds && bounds.end >= bounds.start && this.days(bounds.start, bounds.end) <= MAX_DAYS;
  }

  nextFourWeeks(): void { const period = this.fourWeeksFromThisMonday(); this.startDate = period.start; this.endDate = period.end; }
  fullPlan(): void { const bounds = this.planBounds; if (bounds && this.fullPlanFits) { this.startDate = bounds.start; this.endDate = bounds.end; } }
  cancel(): void { this.ref.close(undefined); }
  confirm(): void {
    if (this.includeTraining && this.error) return;
    this.ref.close({ includeTraining: this.includeTraining, startDate: this.startDate, endDate: this.endDate });
  }

  private days(start: string, end: string): number {
    return Math.floor((Date.parse(end + 'T00:00:00Z') - Date.parse(start + 'T00:00:00Z')) / 86400000) + 1;
  }
  private fourWeeksFromThisMonday(): { start: string; end: string } {
    const today = new Date();
    const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
    start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
    const end = new Date(start);
    end.setDate(end.getDate() + 27);
    const format = (value: Date) => value.getFullYear() + '-' + String(value.getMonth() + 1).padStart(2, '0') + '-' + String(value.getDate()).padStart(2, '0');
    return { start: format(start), end: format(end) };
  }
}
