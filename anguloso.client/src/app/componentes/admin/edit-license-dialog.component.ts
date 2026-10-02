import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { AdminUser, UpdateLicenseDto } from '../../servicios/admin.service';

@Component({
  selector: 'app-edit-license-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatButtonModule
  ],
  template: `
    <h2 mat-dialog-title>Gestionar Licencia: {{ data.username }}</h2>
    <mat-dialog-content class="dialog-content">
      <p class="user-meta" *ngIf="data.fullName">{{ data.fullName }} ({{ data.clinicName || 'Sin clínica' }})</p>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Plan de Suscripción</mat-label>
        <mat-select [(ngModel)]="model.subscriptionPlan">
          <mat-option value="free">Cuenta gratuita</mat-option>
          <mat-option value="demo_nutri">Demo nutricionista</mat-option>
          <mat-option value="nutri_full">Nutri Full</mat-option>
          <mat-option value="clinic_full">Clínica Full</mat-option>
        </mat-select>
      </mat-form-field>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Estado de Suscripción</mat-label>
        <mat-select [(ngModel)]="model.subscriptionStatus">
          <mat-option value="active">Activa</mat-option>
          <mat-option value="past_due">Pago Pendiente</mat-option>
          <mat-option value="suspended">Suspendida</mat-option>
          <mat-option value="cancelled">Cancelada</mat-option>
        </mat-select>
      </mat-form-field>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Límite Máximo de Pacientes / Clientes</mat-label>
        <input matInput type="number" [(ngModel)]="model.maxClientsAllowed" min="1" />
      </mat-form-field>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Fecha de Expiración de Licencia (AAAA-MM-DD)</mat-label>
        <input matInput type="date" [ngModel]="formattedExpiryDate" (ngModelChange)="onDateChange($event)" />
      </mat-form-field>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancelar</button>
      <button mat-flat-button color="primary" (click)="save()">Guardar Cambios</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-content {
      display: flex;
      flex-direction: column;
      gap: 12px;
      min-width: 360px;
      padding-top: 10px;
    }
    .user-meta {
      color: #64748b;
      margin-top: 0;
      margin-bottom: 8px;
      font-size: 14px;
    }
    .full-width {
      width: 100%;
    }
  `]
})
// Documentación: este componente coordina estado de interfaz y operaciones asíncronas que deben mantenerse alineadas con la API.
export class EditLicenseDialogComponent {
  model: UpdateLicenseDto;
  formattedExpiryDate: string = '';

  constructor(
    public dialogRef: MatDialogRef<EditLicenseDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: AdminUser
  ) {
    this.model = {
      subscriptionPlan: data.subscriptionPlan || 'free',
      subscriptionStatus: data.subscriptionStatus || 'active',
      licenseExpiresAt: data.licenseExpiresAt,
      maxClientsAllowed: data.maxClientsAllowed ?? 0
    };

    if (data.licenseExpiresAt) {
      const d = new Date(data.licenseExpiresAt);
      this.formattedExpiryDate = d.toISOString().split('T')[0];
    }
  }

  onDateChange(val: string): void {
    this.formattedExpiryDate = val;
    this.model.licenseExpiresAt = val ? new Date(val).toISOString() : null;
  }

  save(): void {
    this.dialogRef.close(this.model);
  }
}
