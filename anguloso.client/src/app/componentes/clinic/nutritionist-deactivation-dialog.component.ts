import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-nutritionist-deactivation-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatSelectModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>Archivar nutricionista</h2>
    <mat-dialog-content>
      <p><strong>{{data.nutritionist.fullName}}</strong> tiene {{data.clients.length}} pacientes activos.</p>
      <p class="hint">Antes de desactivarlo debes decidir qué hacer con cada paciente. Puedes reasignarlo a otro nutricionista o dejarlo sin asignar.</p>

      <div class="client-row" *ngFor="let client of data.clients">
        <div class="client-info">
          <strong>{{client.fullName}}</strong>
          <small>{{client.email}}</small>
        </div>
        <mat-form-field appearance="outline">
          <mat-label>Nuevo nutricionista</mat-label>
          <mat-select [(ngModel)]="selection[client.clientId]">
            <mat-option [value]="null">Sin asignar</mat-option>
            <mat-option *ngFor="let n of data.candidates" [value]="n.id">{{n.fullName}}</mat-option>
          </mat-select>
        </mat-form-field>
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="cancel()">Cancelar</button>
      <button mat-flat-button color="warn" [disabled]="!complete" (click)="confirm()">
        <mat-icon>archive</mat-icon> Archivar y reasignar
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content { min-width: 620px; max-width: 760px; }
    .hint { color:#64748b; margin-bottom:18px; }
    .client-row { display:grid; grid-template-columns: 1fr 260px; gap:16px; align-items:center; padding:10px 0; border-bottom:1px solid #e2e8f0; }
    .client-info { display:flex; flex-direction:column; gap:3px; }
    .client-info small { color:#64748b; }
    mat-form-field { width:100%; }
    @media(max-width:700px){ mat-dialog-content{min-width:0}.client-row{grid-template-columns:1fr;} }
  `]
})
export class NutritionistDeactivationDialogComponent {
  selection: Record<number, number|null> = {};

  constructor(
    private dialogRef: MatDialogRef<NutritionistDeactivationDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: any
  ) {
    for (const client of data.clients) this.selection[client.clientId] = null;
  }

  get complete(): boolean {
    return this.data.clients.every((c: any) => Object.prototype.hasOwnProperty.call(this.selection, c.clientId));
  }

  cancel(): void { this.dialogRef.close(); }

  confirm(): void {
    if (!this.complete) return;
    this.dialogRef.close({
      assignments: this.data.clients.map((c: any) => ({
        clientId: c.clientId,
        nutritionistId: this.selection[c.clientId]
      }))
    });
  }
}
