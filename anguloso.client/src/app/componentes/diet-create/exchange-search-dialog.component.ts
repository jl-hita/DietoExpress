import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MATERIAL_IMPORTS } from '../../shared/material.imports';
import { FoodExchangeGroup } from '../../modelos/food-exchange-group';

@Component({
  selector: 'app-exchange-search-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MATERIAL_IMPORTS],
  template: `
    <h2 mat-dialog-title>Añadir Intercambio</h2>
    <mat-dialog-content>
      <p class="exchange-info">Selecciona un grupo de intercambio y el número de intercambios que deseas añadir a esta comida.</p>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Grupo de Intercambio</mat-label>
        <mat-select [(ngModel)]="selectedGroup" name="group">
          <mat-option *ngFor="let g of data.exchangeGroups" [value]="g">
            {{ g.name }}
            <span class="group-macros"> — {{ g.kcal }} kcal / int. ({{ g.carbs }}g HC, {{ g.protein }}g P, {{ g.fat }}g G)</span>
          </mat-option>
        </mat-select>
      </mat-form-field>

      <mat-form-field appearance="outline" class="full-width mt-2" *ngIf="selectedGroup">
        <mat-label>Número de Intercambios</mat-label>
        <input matInput type="number" [(ngModel)]="count" min="0.5" step="0.5" name="count">
        <mat-hint>Ej: 1, 1.5, 2...</mat-hint>
      </mat-form-field>

      <div *ngIf="selectedGroup && count > 0" class="macros-preview">
        <h4>Totales para {{ count }} intercambio(s):</h4>
        <div class="macro-chips">
          <span class="chip kcal">{{ +(selectedGroup.kcal * count).toFixed(1) }} kcal</span>
          <span class="chip carbs">{{ +(selectedGroup.carbs * count).toFixed(1) }}g HC</span>
          <span class="chip protein">{{ +(selectedGroup.protein * count).toFixed(1) }}g Prot.</span>
          <span class="chip fat">{{ +(selectedGroup.fat * count).toFixed(1) }}g Grasa</span>
        </div>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button color="primary" [disabled]="!selectedGroup || count <= 0" (click)="confirm()">Añadir Intercambio</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .full-width { width: 100%; margin-top: 8px; }
    .mt-2 { margin-top: 16px; }
    .exchange-info { color: #666; font-size: 13px; margin-bottom: 12px; }
    .group-macros { font-size: 11px; color: #888; }
    .macros-preview { background: #e8eaf6; border-radius: 8px; padding: 12px; margin-top: 16px; }
    .macros-preview h4 { margin: 0 0 10px 0; font-size: 13px; color: #3f51b5; }
    .macro-chips { display: flex; gap: 8px; flex-wrap: wrap; }
    .chip { padding: 4px 12px; border-radius: 16px; font-size: 13px; font-weight: 600; }
    .chip.kcal { background: #ffebee; color: #c62828; }
    .chip.carbs { background: #fff8e1; color: #e65100; }
    .chip.protein { background: #e8f5e9; color: #2e7d32; }
    .chip.fat { background: #e3f2fd; color: #1565c0; }
  `]
})
export class ExchangeSearchDialogComponent {
  selectedGroup: FoodExchangeGroup | null = null;
  count: number = 1;

  constructor(
    private dialogRef: MatDialogRef<ExchangeSearchDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: { exchangeGroups: FoodExchangeGroup[] }
  ) {}

  confirm() {
    if (this.selectedGroup && this.count > 0) {
      this.dialogRef.close({ group: this.selectedGroup, count: this.count });
    }
  }
}
