import { Component, Inject, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { DietService } from '../../servicios/diet.service';
import { ClientService } from '../../servicios/client.service';
import { Diet } from '../../modelos/diet';
import { MATERIAL_IMPORTS } from '../../shared/material.imports';
import { MatNativeDateModule } from '@angular/material/core';

export interface DietSelectDialogData {
  clientId: number;
}

export interface DietValidationResult {
  foodId?: number;
  foodName: string;
  mealName: string;
  dayIndex: number;
  alertType: string; // "Lactose" | "Gluten" | "Fodmap" | "Allergy"
  severity: string;  // "High" | "Medium"
  message: string;
}

@Component({
  selector: 'app-diet-select-dialog',
  standalone: true,
  imports: [MATERIAL_IMPORTS, MatNativeDateModule],
  template: `
    <h2 mat-dialog-title>Asignar Plan Nutricional</h2>
    
    <mat-dialog-content class="dialog-content">
      <div *ngIf="loadingDiets" class="loading-spinner">
        <mat-progress-spinner mode="indeterminate" diameter="40"></mat-progress-spinner>
        <p>Cargando planes disponibles...</p>
      </div>

      <div *ngIf="!loadingDiets && diets.length === 0" class="error-box">
        <mat-icon color="warn">warning</mat-icon>
        <p>No hay dietas creadas en el sistema. Debes crear una dieta antes de asignarla.</p>
      </div>

      <form [formGroup]="form" *ngIf="!loadingDiets && diets.length > 0" class="form-container">
        <!-- Selector de dieta -->
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Selecciona una Dieta</mat-label>
          <mat-select formControlName="dietId" (selectionChange)="onDietSelected($event.value)">
            <mat-option *ngFor="let d of diets" [value]="d.id">
              {{ d.name }} ({{ d.targetKcal ?? '—' }} kcal)
            </mat-option>
          </mat-select>
          <mat-error *ngIf="form.get('dietId')?.hasError('required')">La dieta es obligatoria.</mat-error>
        </mat-form-field>

        <!-- Fecha de inicio -->
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Fecha de Inicio</mat-label>
          <input matInput [matDatepicker]="picker" formControlName="startDate">
          <mat-datepicker-toggle matSuffix [for]="picker"></mat-datepicker-toggle>
          <mat-datepicker #picker></mat-datepicker>
          <mat-error *ngIf="form.get('startDate')?.hasError('required')">La fecha de inicio es obligatoria.</mat-error>
        </mat-form-field>

        <!-- Notas adicionales -->
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Notas de la Asignación</mat-label>
          <textarea matInput rows="2" formControlName="notes" placeholder="Ej: Dieta para la fase de volumen..."></textarea>
        </mat-form-field>

        <!-- Resultados de Validación Clínica -->
        <div class="validation-container" *ngIf="isValidating">
          <mat-progress-bar mode="query"></mat-progress-bar>
          <p class="validating-text">Validando compatibilidad con el historial del paciente...</p>
        </div>

        <div class="validation-results" *ngIf="!isValidating && validationRun">
          <!-- Compatible -->
          <div class="alert-box success" *ngIf="warnings.length === 0">
            <mat-icon>check_circle</mat-icon>
            <div class="alert-text">
              <strong>Plan Compatible:</strong> No se han detectado conflictos de alérgenos o intolerancias.
            </div>
          </div>

          <!-- Conflictos -->
          <div class="alert-box warning-box" *ngIf="warnings.length > 0">
            <mat-icon>warning</mat-icon>
            <div class="alert-text">
              <strong>Conflictos Detectados:</strong> El plan contiene ingredientes incompatibles con las intolerancias o alergias del paciente.
            </div>
          </div>

          <!-- Lista de alertas -->
          <div class="warnings-list" *ngIf="warnings.length > 0">
            <div class="warning-item" *ngFor="let w of warnings" [class.high-severity]="w.severity === 'High'">
              <span class="warning-badge" [class.badge-high]="w.severity === 'High'">
                {{ w.severity === 'High' ? 'ALERTA' : 'Aviso' }}
              </span>
              <div class="warning-body">
                <span class="warning-loc">Día {{ w.dayIndex + 1 }} - {{ w.mealName }}:</span>
                <span class="warning-msg"><strong>{{ w.foodName }}</strong>. {{ w.message }}</span>
              </div>
            </div>
          </div>
        </div>
      </form>
    </mat-dialog-content>

    <mat-dialog-actions align="end" class="dialog-actions">
      <button mat-button (click)="cancel()">Cancelar</button>
      <button mat-raised-button 
              [color]="hasHighSeverityWarnings ? 'warn' : 'primary'"
              [disabled]="form.invalid || loadingDiets || isValidating" 
              (click)="save()">
        Asignar Dieta
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-content {
      min-width: 320px;
      max-width: 500px;
      display: flex;
      flex-direction: column;
      gap: 16px;
      padding-top: 10px !important;
    }
    .form-container {
      display: flex;
      flex-direction: column;
      gap: 10px;
    }
    .full-width {
      width: 100%;
    }
    .loading-spinner {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 30px;
      gap: 10px;
      color: #666;
    }
    .error-box {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 16px;
      background: #fff8e1;
      border-radius: 4px;
      color: #b78103;
    }
    .validation-container {
      margin-top: 10px;
    }
    .validating-text {
      font-size: 11px;
      color: #666;
      margin-top: 4px;
    }
    .alert-box {
      display: flex;
      align-items: flex-start;
      gap: 10px;
      padding: 12px;
      border-radius: 4px;
      font-size: 13px;
      margin-top: 10px;
      line-height: 1.4;
    }
    .alert-box.success {
      background-color: #e8f5e9;
      color: #2e7d32;
    }
    .alert-box.success mat-icon {
      color: #2e7d32;
    }
    .alert-box.warning-box {
      background-color: #ffebee;
      color: #c62828;
      border-left: 4px solid #d32f2f;
    }
    .alert-box.warning-box mat-icon {
      color: #c62828;
    }
    .warnings-list {
      max-height: 180px;
      overflow-y: auto;
      border: 1px solid #e0e0e0;
      border-radius: 4px;
      margin-top: 8px;
      padding: 8px;
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .warning-item {
      display: flex;
      align-items: flex-start;
      gap: 8px;
      font-size: 12px;
      padding: 6px;
      border-radius: 4px;
      background: #fafafa;
    }
    .warning-item.high-severity {
      background: #fff5f5;
    }
    .warning-badge {
      font-size: 9px;
      font-weight: bold;
      text-transform: uppercase;
      padding: 2px 6px;
      border-radius: 10px;
      background: #ffb74d;
      color: #fff;
    }
    .warning-badge.badge-high {
      background: #ef5350;
    }
    .warning-body {
      display: flex;
      flex-direction: column;
    }
    .warning-loc {
      color: #666;
      font-weight: 500;
    }
    .warning-msg {
      color: #333;
    }
    .dialog-actions {
      padding: 16px 24px !important;
    }
  `]
})
export class DietSelectDialogComponent implements OnInit {
  form: FormGroup;
  diets: Diet[] = [];
  warnings: DietValidationResult[] = [];
  loadingDiets = true;
  isValidating = false;
  validationRun = false;

  constructor(
    private fb: FormBuilder,
    private dietService: DietService,
    private clientService: ClientService,
    private dialogRef: MatDialogRef<DietSelectDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: DietSelectDialogData
  ) {
    this.form = this.fb.group({
      dietId: [null, Validators.required],
      startDate: [new Date(), Validators.required],
      notes: ['']
    });
  }

  ngOnInit(): void {
    this.dietService.getDiets().subscribe({
      next: (list) => {
        this.diets = list || [];
        this.loadingDiets = false;
      },
      error: () => {
        this.loadingDiets = false;
      }
    });
  }

  onDietSelected(dietId: number): void {
    if (!dietId) return;
    this.isValidating = true;
    this.validationRun = false;
    this.warnings = [];

    this.clientService.validateSavedDiet(this.data.clientId, dietId).subscribe({
      next: (res) => {
        this.warnings = res || [];
        this.isValidating = false;
        this.validationRun = true;
      },
      error: (err) => {
        console.error('Error al validar dieta', err);
        this.isValidating = false;
        this.validationRun = true;
      }
    });
  }

  get hasHighSeverityWarnings(): boolean {
    return this.warnings.some(w => w.severity === 'High');
  }

  cancel(): void {
    this.dialogRef.close();
  }

  save(): void {
    if (this.form.invalid) return;

    if (this.warnings.length > 0) {
      const confirmMsg = this.hasHighSeverityWarnings
        ? '¡Atención! Este plan alimentario contiene conflictos severos de alérgenos/intolerancias con el historial del paciente. ¿Deseas asignarlo de todas formas?'
        : 'Esta dieta contiene algunos conflictos menores de compatibilidad. ¿Deseas asignarla?';
        
      if (!confirm(confirmMsg)) {
        return;
      }
    }

    const val = this.form.value;
    
    // Normalizar fecha de inicio a string YYYY-MM-DD
    const date: Date = val.startDate;
    const offset = date.getTimezoneOffset();
    const localDate = new Date(date.getTime() - (offset * 60 * 1000));
    const startDateStr = localDate.toISOString().split('T')[0];

    const payload = {
      dietId: val.dietId,
      startDate: startDateStr,
      notes: val.notes
    };

    this.dialogRef.close(payload);
  }
}
