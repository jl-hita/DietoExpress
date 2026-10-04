import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AuthService } from '../../servicios/auth.service';
import { LegalConfigurationService } from '../../servicios/legal-configuration.service';

@Component({
  selector: 'app-legal-settings',
  standalone: true,
  imports: [
    CommonModule, ReactiveFormsModule, MatButtonModule, MatCardModule,
    MatDividerModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatSnackBarModule
  ],
  template: `
    <div class="legal-tab" [formGroup]="form">
      <div class="intro">
        <mat-icon>gavel</mat-icon>
        <div>
          <h2>Configuración legal</h2>
          <p>Estos datos alimentan las plantillas legales. Un campo vacío significa que el documento sigue pendiente de completar y revisar.</p>
        </div>
      </div>

      <mat-card>
        <mat-card-header>
          <mat-icon mat-card-avatar>business</mat-icon>
          <mat-card-title>{{ isClinic ? 'Datos legales de la clínica' : 'Datos legales del profesional' }}</mat-card-title>
          <mat-card-subtitle>Identidad, domicilio y canales de contacto.</mat-card-subtitle>
        </mat-card-header>
        <mat-divider></mat-divider>
        <mat-card-content class="grid">
          <mat-form-field appearance="outline"><mat-label>Nombre o denominación legal</mat-label><input matInput formControlName="legal_name"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>NIF / CIF</mat-label><input matInput formControlName="tax_id"></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Domicilio legal</mat-label><input matInput formControlName="address"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email de contacto</mat-label><input matInput type="email" formControlName="contact_email"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Teléfono</mat-label><input matInput formControlName="contact_phone"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email de privacidad / derechos</mat-label><input matInput type="email" formControlName="privacy_email"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email DPO (si procede)</mat-label><input matInput type="email" formControlName="dpo_email"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Web</mat-label><input matInput formControlName="website"></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card *ngIf="!isClinic">
        <mat-card-header><mat-icon mat-card-avatar>workspace_premium</mat-icon><mat-card-title>Datos profesionales</mat-card-title></mat-card-header>
        <mat-divider></mat-divider>
        <mat-card-content class="grid">
          <mat-form-field appearance="outline"><mat-label>Título profesional</mat-label><input matInput formControlName="professional_title"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Colegio profesional</mat-label><input matInput formControlName="professional_college"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Número de colegiado</mat-label><input matInput formControlName="professional_collegiate_number"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Estado que expidió el título</mat-label><input matInput formControlName="professional_title_country"></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card>
        <mat-card-header><mat-icon mat-card-avatar>privacy_tip</mat-icon><mat-card-title>Privacidad y pacientes</mat-card-title><mat-card-subtitle>Información que aparecerá en la documentación generada.</mat-card-subtitle></mat-card-header>
        <mat-divider></mat-divider>
        <mat-card-content>
          <mat-form-field appearance="outline" class="full"><mat-label>Base jurídica para la información de privacidad</mat-label><textarea matInput rows="2" formControlName="patient_privacy_legal_basis"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Destinatarios / encargados</mat-label><textarea matInput rows="2" formControlName="patient_recipients_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Conservación de datos del paciente</mat-label><textarea matInput rows="2" formControlName="patient_retention_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>URL de política de privacidad</mat-label><input matInput formControlName="privacy_policy_url"></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Descripción del servicio / consulta</mat-label><textarea matInput rows="2" formControlName="consultation_description"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Finalidades adicionales del tratamiento</mat-label><textarea matInput rows="2" formControlName="patient_additional_purposes"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Categorías especiales u otros datos adicionales</mat-label><textarea matInput rows="2" formControlName="patient_special_categories_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Riesgos relevantes de la consulta, cuando proceda</mat-label><textarea matInput rows="2" formControlName="consultation_risks"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Límites, alternativas e información relevante</mat-label><textarea matInput rows="2" formControlName="consultation_limits"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Duración del encargo de tratamiento</mat-label><input matInput formControlName="dpa_duration"></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Devolución / supresión al finalizar el servicio</mat-label><textarea matInput rows="2" formControlName="dpa_end_of_service_summary"></textarea></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card>
        <mat-card-header><mat-icon mat-card-avatar>payments</mat-icon><mat-card-title>Condiciones económicas</mat-card-title></mat-card-header>
        <mat-divider></mat-divider>
        <mat-card-content>
          <mat-form-field appearance="outline" class="full"><mat-label>Servicios y precios</mat-label><textarea matInput rows="2" formControlName="service_prices_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Reserva y pago</mat-label><textarea matInput rows="2" formControlName="booking_payment_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Cancelación de citas</mat-label><textarea matInput rows="2" formControlName="appointment_cancellation_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Reembolsos</mat-label><textarea matInput rows="2" formControlName="refund_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>No asistencia</mat-label><textarea matInput rows="2" formControlName="no_show_summary"></textarea></mat-form-field>
        </mat-card-content>
      </mat-card>

      <div class="actions">
        <button mat-raised-button color="primary" type="button" (click)="save()" [disabled]="saving">
          <mat-icon>save</mat-icon>{{ saving ? 'Guardando...' : 'Guardar configuración legal' }}
        </button>
      </div>
    </div>
  `,
  styles: [`
    .legal-tab { padding: 24px 0 12px; display: grid; gap: 18px; }
    .intro { display:flex; gap:14px; align-items:flex-start; padding:16px; border-radius:12px; background:#f8fafc; }
    .intro mat-icon { color:#0f766e; }
    .intro h2 { margin:0 0 4px; }
    .intro p { margin:0; color:#64748b; }
    mat-card { margin:0; }
    .grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:12px; padding-top:18px; }
    .wide { grid-column:1/-1; }
    .full { width:100%; }
    .actions { display:flex; justify-content:flex-end; }
    @media(max-width:700px) { .grid { grid-template-columns:1fr; } .wide { grid-column:auto; } }
  `]
})
export class LegalSettingsComponent implements OnInit {
  form: FormGroup;
  saving = false;
  isClinic = false;

  constructor(
    private fb: FormBuilder,
    private auth: AuthService,
    private legal: LegalConfigurationService,
    private snack: MatSnackBar
  ) {
    this.isClinic = this.auth.getRole() === 'clinic_admin';
    this.form = this.fb.group({
      legal_name: [''], tax_id: [''], address: [''], contact_email: [''], contact_phone: [''],
      privacy_email: [''], dpo_email: [''], website: [''],
      professional_title: [''], professional_college: [''], professional_collegiate_number: [''],
      professional_title_country: [''], patient_privacy_legal_basis: [''],
      patient_recipients_summary: [''], patient_retention_summary: [''], privacy_policy_url: [''],
      consultation_description: [''], consultation_limits: [''], service_prices_summary: [''],
      booking_payment_summary: [''], appointment_cancellation_summary: [''], refund_summary: [''],
      no_show_summary: [''], dpa_duration: [''], dpa_end_of_service_summary: [''], patient_additional_purposes: [''], patient_special_categories_summary: [''], consultation_risks: ['']
    });
  }

  ngOnInit(): void {
    this.legal.getProfessional().subscribe({
      next: settings => {
        const values: Record<string, string> = {};
        settings.forEach(s => values[s.key] = s.value ?? '');
        this.form.patchValue(values);
      },
      error: () => this.snack.open('No se pudo cargar la configuración legal.', 'Cerrar', { duration: 4000 })
    });
  }

  save(): void {
    if (this.saving) return;
    this.saving = true;
    this.legal.saveProfessional(this.form.getRawValue()).subscribe({
      next: () => { this.saving = false; this.snack.open('Configuración legal guardada.', 'Cerrar', { duration: 3000 }); },
      error: () => { this.saving = false; this.snack.open('No se pudo guardar la configuración legal.', 'Cerrar', { duration: 4000 }); }
    });
  }
}
