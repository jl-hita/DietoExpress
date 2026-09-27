import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { AdminPlan, CreateAdminAccountDto } from '../../servicios/admin.service';

@Component({
  selector: 'app-create-admin-account-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  template: `
    <h2 mat-dialog-title>Crear cuenta</h2>
    <mat-dialog-content class="dialog-content">
      <div class="section-title">Tipo de cuenta y licencia</div>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Tipo de cuenta</mat-label>
        <mat-select [(ngModel)]="model.accountType" (selectionChange)="onAccountTypeChange()">
          <mat-option value="nutritionist">Nutricionista</mat-option>
          <mat-option value="clinic">Clínica</mat-option>
        </mat-select>
      </mat-form-field>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Plan</mat-label>
        <mat-select [(ngModel)]="model.subscriptionPlan" (selectionChange)="applyPlanDefaults()">
          <mat-option *ngFor="let plan of plans" [value]="plan.code">
            {{ plan.name }} — {{ plan.monthly_price | number:'1.2-2' }} €/mes
          </mat-option>
        </mat-select>
      </mat-form-field>

      <div class="section-title">Acceso</div>

      <div class="two-cols">
        <mat-form-field appearance="outline">
          <mat-label>Usuario</mat-label>
          <input matInput [(ngModel)]="model.username" autocomplete="off">
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Contraseña inicial</mat-label>
          <input matInput type="password" [(ngModel)]="model.password" autocomplete="new-password">
        </mat-form-field>
      </div>

      <div class="two-cols">
        <mat-form-field appearance="outline">
          <mat-label>Nombre completo</mat-label>
          <input matInput [(ngModel)]="model.fullName">
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Email</mat-label>
          <input matInput type="email" [(ngModel)]="model.email">
        </mat-form-field>
      </div>

      <div class="section-title" *ngIf="model.accountType === 'clinic'">Datos de la clínica</div>

      <mat-form-field appearance="outline" class="full-width" *ngIf="model.accountType === 'clinic'">
        <mat-label>Nombre comercial de la clínica</mat-label>
        <input matInput [(ngModel)]="model.clinicName">
      </mat-form-field>

      <div class="two-cols" *ngIf="model.accountType === 'clinic'">
        <mat-form-field appearance="outline">
          <mat-label>Razón social</mat-label>
          <input matInput [(ngModel)]="model.legalName">
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>CIF / NIF</mat-label>
          <input matInput [(ngModel)]="model.cifNif">
        </mat-form-field>
      </div>

      <div class="two-cols">
        <mat-form-field appearance="outline">
          <mat-label>Dirección</mat-label>
          <input matInput [(ngModel)]="model.clinicAddress">
        </mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Teléfono</mat-label>
          <input matInput [(ngModel)]="model.clinicPhone">
        </mat-form-field>
      </div>

      <div class="section-title">Condiciones de la licencia</div>

      <div class="two-cols">
        <mat-form-field appearance="outline">
          <mat-label>Estado</mat-label>
          <mat-select [(ngModel)]="model.subscriptionStatus">
            <mat-option value="active">Activa</mat-option>
            <mat-option value="past_due">Pago pendiente</mat-option>
            <mat-option value="suspended">Suspendida</mat-option>
          </mat-select>
        </mat-form-field>

        <mat-form-field appearance="outline">
          <mat-label>Máximo de pacientes</mat-label>
          <input matInput type="number" min="1" [(ngModel)]="model.maxClientsAllowed">
        </mat-form-field>
      </div>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Vencimiento de licencia</mat-label>
        <input matInput type="date" [(ngModel)]="expiryDate">
        <mat-hint>Si se deja vacío, un plan con periodo de prueba usa automáticamente sus días de prueba.</mat-hint>
      </mat-form-field>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancelar</button>
      <button mat-flat-button color="primary" [disabled]="!isValid()" (click)="save()">Crear cuenta</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-content { display:flex; flex-direction:column; gap:8px; min-width:620px; padding-top:10px; }
    .full-width { width:100%; }
    .two-cols { display:grid; grid-template-columns:1fr 1fr; gap:12px; }
    .section-title { font-size:13px; font-weight:700; color:#475569; margin:8px 0 2px; text-transform:uppercase; letter-spacing:.4px; }
    @media (max-width: 700px) { .dialog-content { min-width:0; } .two-cols { grid-template-columns:1fr; } }
  `]
})
export class CreateAdminAccountDialogComponent {
  model: CreateAdminAccountDto;
  expiryDate = '';

  constructor(
    public dialogRef: MatDialogRef<CreateAdminAccountDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: { plans: AdminPlan[] }
  ) {
    const first = data.plans?.[0];
    this.model = {
      accountType: 'nutritionist',
      username: '',
      fullName: '',
      email: '',
      password: '',
      subscriptionPlan: first?.code || 'trial_nutri',
      subscriptionStatus: 'active',
      maxClientsAllowed: first?.max_clients_per_nutritionist ?? 10
    };
    this.applyPlanDefaults();
  }

  applyPlanDefaults(): void {
    const plan = this.data.plans.find(p => p.code === this.model.subscriptionPlan);
    if (plan?.max_clients_per_nutritionist) {
      this.model.maxClientsAllowed = plan.max_clients_per_nutritionist;
    }
  }

  onAccountTypeChange(): void {
    if (this.model.accountType === 'clinic') {
      const clinicPlan = this.data.plans.find(p => p.code === 'clinic_full');
      if (clinicPlan) {
        this.model.subscriptionPlan = clinicPlan.code;
        this.applyPlanDefaults();
      }
    } else if (this.model.subscriptionPlan === 'clinic_full') {
      const nutriPlan = this.data.plans.find(p => p.code !== 'clinic_full');
      if (nutriPlan) {
        this.model.subscriptionPlan = nutriPlan.code;
        this.applyPlanDefaults();
      }
    }
  }

  isValid(): boolean {
    const commonValid = !!this.model.username.trim() &&
      !!this.model.fullName.trim() &&
      !!this.model.email.trim() &&
      !!this.model.password &&
      this.model.password.length >= 6;

    return commonValid &&
      (this.model.accountType === 'nutritionist' ||
       (!!this.model.clinicName?.trim() && this.model.subscriptionPlan === 'clinic_full'));
  }

  save(): void {
    if (!this.isValid()) return;
    this.dialogRef.close({
      ...this.model,
      licenseExpiresAt: this.expiryDate ? new Date(this.expiryDate).toISOString() : null
    });
  }
}
