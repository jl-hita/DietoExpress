import { Component, Inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { DietService } from '../../servicios/diet.service';
import { ClientService } from '../../servicios/client.service';

export interface DietGeneratorDialogData {
  clientId?: number | null;
  clientName?: string | null;
  defaultKcal?: number;
}

type KcalSource = 'default' | 'biometrics' | 'error';

@Component({
  selector: 'app-diet-generator-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  template: `
    <div class="generator-dialog">
      <h2 mat-dialog-title style="display: flex; align-items: center; gap: 8px; margin: 0 0 16px;">
        <mat-icon color="primary">auto_awesome</mat-icon>
        Generador Automático de Dietas
      </h2>

      <mat-dialog-content>
        <p style="color: #64748b; font-size: 13px; margin-bottom: 16px;">
          Configura los parámetros del motor determinista para generar un plan semanal estructurado, variado y libre de alérgenos.
        </p>

        <div *ngIf="data.clientName" style="background: #e0f2fe; border: 1px solid #7dd3fc; border-radius: 6px; padding: 8px 12px; margin-bottom: 16px; font-size: 13px; color: #0369a1; display: flex; align-items: center; gap: 6px;">
          <mat-icon style="font-size: 18px; width: 18px; height: 18px;">person</mat-icon>
          <span>Adaptando plan a: <strong>{{ data.clientName }}</strong> (se respetarán sus intolerancias y alergias clínicas)</span>
        </div>

        <form [formGroup]="form" class="dialog-form" style="display: flex; flex-direction: column; gap: 12px;">
          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
            <div style="display: flex; flex-direction: column; gap: 4px;">
              <mat-form-field appearance="outline">
                <mat-label>Calorías diarias (kcal)</mat-label>
                <input matInput type="number" formControlName="targetKcal" min="1000" max="5000" />
              </mat-form-field>
              <!-- Badge de origen de kcal -->
              <div *ngIf="data.clientId">
                <span *ngIf="kcalSource === 'biometrics'"
                  style="font-size: 11px; color: #16a34a; display: flex; align-items: center; gap: 4px; margin-top: -8px;">
                  <mat-icon style="font-size: 14px; width: 14px; height: 14px;">verified</mat-icon>
                  Calculado desde las biometrías del paciente
                </span>
                <span *ngIf="kcalSource === 'default'"
                  style="font-size: 11px; color: #92400e; display: flex; align-items: center; gap: 4px; margin-top: -8px;">
                  <mat-icon style="font-size: 14px; width: 14px; height: 14px;">info</mat-icon>
                  Valor por defecto (sin biometrías disponibles)
                </span>
                <span *ngIf="kcalSource === 'error'"
                  style="font-size: 11px; color: #dc2626; display: flex; align-items: center; gap: 4px; margin-top: -8px;">
                  <mat-icon style="font-size: 14px; width: 14px; height: 14px;">warning</mat-icon>
                  No se pudo calcular el TDEE — revisa las biometrías del paciente
                </span>
              </div>
            </div>

            <mat-form-field appearance="outline">
              <mat-label>Tipo de Dieta</mat-label>
              <mat-select formControlName="dietType">
                <mat-option value="Equilibrada">Equilibrada (Mediterránea)</mat-option>
                <mat-option value="AltaProteina">Alta en Proteína (Fitness)</mat-option>
                <mat-option value="BajaCarbos">Baja en Carbohidratos</mat-option>
                <mat-option value="Vegetariana">Vegetariana</mat-option>
                <mat-option value="Vegana">Vegana</mat-option>
                <mat-option value="Cetogenica">Cetogénica (&lt;50g carbos)</mat-option>
              </mat-select>
            </mat-form-field>
          </div>

          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
            <mat-form-field appearance="outline">
              <mat-label>Comidas al día</mat-label>
              <mat-select formControlName="mealsPerDay">
                <mat-option [value]="3">3 comidas (Desayuno, Comida, Cena)</mat-option>
                <mat-option [value]="4">4 comidas (+ Merienda)</mat-option>
                <mat-option [value]="5">5 comidas (+ Media Mañana y Merienda)</mat-option>
              </mat-select>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Número de Días</mat-label>
              <mat-select formControlName="numberOfDays">
                <mat-option [value]="1">1 Día (Pauta diaria)</mat-option>
                <mat-option [value]="3">3 Días (Rotativo)</mat-option>
                <mat-option [value]="5">5 Días (Lunes a Viernes)</mat-option>
                <mat-option [value]="7">7 Días (Semana completa)</mat-option>
              </mat-select>
            </mat-form-field>
          </div>

          <mat-form-field appearance="outline">
            <mat-label>Palabras clave a excluir (separadas por comas)</mat-label>
            <input matInput formControlName="excludedKeywords" placeholder="Ej: cerdo, marisco, champiñones" />
          </mat-form-field>
        </form>

        <div *ngIf="generating" style="display: flex; flex-direction: column; align-items: center; justify-content: center; padding: 20px; gap: 10px;">
          <mat-spinner diameter="40"></mat-spinner>
          <span style="font-size: 13px; color: #475569;">Optimizando combinaciones de alimentos y macronutrientes...</span>
        </div>

        <div *ngIf="errorMessage" style="color: #dc2626; font-size: 13px; margin-top: 8px;">
          {{ errorMessage }}
        </div>
      </mat-dialog-content>

      <mat-dialog-actions align="end" style="margin-top: 16px; gap: 8px;">
        <button mat-button (click)="dialogRef.close()" [disabled]="generating">Cancelar</button>
        <button mat-raised-button color="primary" (click)="generate()" [disabled]="form.invalid || generating">
          <mat-icon>bolt</mat-icon> Generar Plan
        </button>
      </mat-dialog-actions>
    </div>
  `,
  styles: [`
    .generator-dialog {
      min-width: 380px;
      max-width: 520px;
      padding: 8px;
    }
  `]
})
// El diálogo encapsula la generación de dietas y su estado asíncrono para evitar que el componente principal gestione directamente el proceso.
export class DietGeneratorDialogComponent implements OnInit {
  form: FormGroup;
  generating = false;
  errorMessage = '';
  kcalSource: KcalSource = 'default';

  constructor(
    private fb: FormBuilder,
    private dietService: DietService,
    private clientService: ClientService,
    public dialogRef: MatDialogRef<DietGeneratorDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: DietGeneratorDialogData
  ) {
    this.form = this.fb.group({
      targetKcal: [data.defaultKcal || 2000, [Validators.required, Validators.min(800)]],
      dietType: ['Equilibrada', Validators.required],
      mealsPerDay: [5, Validators.required],
      numberOfDays: [7, Validators.required],
      excludedKeywords: ['']
    });
  }

  // Si el diálogo recibe un paciente sin unas kcal explícitas, consulta sus biometrías para evitar usar silenciosamente el valor genérico.
  ngOnInit(): void {
    // Si tenemos clientId y no tenemos defaultKcal, intentamos calcular sus requerimientos
    if (this.data.clientId && (!this.data.defaultKcal || this.data.defaultKcal === 2000)) {
      this.clientService.getEnergyRequirements(this.data.clientId).subscribe({
        next: (req) => {
          if (req?.tdee?.moderateExercise) {
            this.form.patchValue({ targetKcal: Math.round(req.tdee.moderateExercise) });
            this.kcalSource = 'biometrics';
          } else if (req?.mifflinStJeor) {
            this.form.patchValue({ targetKcal: Math.round(req.mifflinStJeor * 1.4) });
            this.kcalSource = 'biometrics';
          } else {
            this.kcalSource = 'default';
          }
        },
        error: () => {
          this.kcalSource = 'error';
        }
      });
    }
  }

  // El diálogo transforma la configuración visual en el DTO del motor y devuelve la dieta generada al componente que lo abrió.
  generate(): void {
    if (this.form.invalid) return;
    this.generating = true;
    this.errorMessage = '';

    const raw = this.form.value;
    const excluded = raw.excludedKeywords
      ? raw.excludedKeywords.split(',').map((s: string) => s.trim()).filter((s: string) => s.length > 0)
      : [];

    const payload = {
      targetKcal: raw.targetKcal,
      dietType: raw.dietType,
      mealsPerDay: raw.mealsPerDay,
      numberOfDays: raw.numberOfDays,
      clientId: this.data.clientId || null,
      excludedFoodKeywords: excluded
    };

    this.dietService.generateDiet(payload).subscribe({
      next: (generatedDiet) => {
        this.generating = false;
        this.dialogRef.close(generatedDiet);
      },
      error: (err) => {
        this.generating = false;
        this.errorMessage = 'Hubo un error al generar la dieta. Por favor verifica las opciones.';
        console.error('Error generating diet:', err);
      }
    });
  }
}

