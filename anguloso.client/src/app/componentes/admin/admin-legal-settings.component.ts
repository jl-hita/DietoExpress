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
import { LegalConfigurationService } from '../../servicios/legal-configuration.service';

@Component({
  selector: 'app-admin-legal-settings',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatButtonModule, MatCardModule, MatDividerModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSnackBarModule],
  template: `
    <main class="page" [formGroup]="form">
      <header><div><h1>Configuración legal de DietoExpress</h1><p>Datos del titular y de la plataforma que se insertarán en las plantillas legales.</p></div></header>

      <div class="notice"><mat-icon>warning</mat-icon><span>Completar estos campos no equivale a una revisión jurídica. Los documentos publicados deben estar sin placeholders y revisados antes de activarse.</span></div>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>business</mat-icon><mat-card-title>Identidad del titular</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content class="grid">
          <mat-form-field appearance="outline"><mat-label>Nombre / denominación legal</mat-label><input matInput formControlName="legal_name"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>NIF / CIF</mat-label><input matInput formControlName="tax_id"></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Domicilio</mat-label><input matInput formControlName="address"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email de contacto</mat-label><input matInput formControlName="contact_email"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Teléfono</mat-label><input matInput formControlName="contact_phone"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email de privacidad</mat-label><input matInput formControlName="privacy_email"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email DPO (si procede)</mat-label><input matInput formControlName="dpo_email"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Web</mat-label><input matInput formControlName="website"></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Registro, autorización o información profesional que proceda</mat-label><textarea matInput rows="2" formControlName="registration_information"></textarea></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>policy</mat-icon><mat-card-title>Privacidad, proveedores y conservación</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content>
          <mat-form-field appearance="outline" class="full"><mat-label>Resumen de proveedores/subencargados</mat-label><textarea matInput rows="2" formControlName="providers_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Transferencias internacionales</mat-label><textarea matInput rows="2" formControlName="international_transfers_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Referencia a matriz de conservación</mat-label><input matInput formControlName="retention_policy_reference"></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Subencargados y procedimiento de autorización</mat-label><textarea matInput rows="2" formControlName="subprocessors_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Terceros/cookies no esenciales</mat-label><textarea matInput rows="2" formControlName="cookie_third_parties"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Cookies no esenciales</mat-label><textarea matInput rows="2" formControlName="non_essential_cookies_summary"></textarea></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>description</mat-icon><mat-card-title>Contratación, soporte y reclamaciones</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content>
          <mat-form-field appearance="outline" class="full"><mat-label>Cancelación y renovación</mat-label><textarea matInput rows="2" formControlName="cancellation_policy_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Email de soporte</mat-label><input matInput formControlName="support_email"></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Condiciones de soporte</mat-label><textarea matInput rows="2" formControlName="support_policy_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Email de reclamaciones</mat-label><input matInput formControlName="claims_email"></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Ley aplicable / jurisdicción</mat-label><textarea matInput rows="2" formControlName="governing_law_summary"></textarea></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>history</mat-icon><mat-card-title>Control documental</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content class="grid">
          <mat-form-field appearance="outline"><mat-label>Versión documental</mat-label><input matInput formControlName="document_version"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Fecha de actualización</mat-label><input matInput formControlName="last_update_date"></mat-form-field>
        </mat-card-content>
      </mat-card>

      <div class="actions"><button mat-raised-button color="primary" (click)="save()" [disabled]="saving"><mat-icon>save</mat-icon>{{ saving ? 'Guardando...' : 'Guardar configuración legal' }}</button></div>
    </main>
  `,
  styles: [`
    .page{padding:24px;display:grid;gap:18px;max-width:1100px}.page h1{margin:0}.page header p{color:#64748b}.notice{display:flex;gap:10px;padding:14px;border-radius:10px;background:#fff7ed;color:#9a3412}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;padding-top:18px}.wide{grid-column:1/-1}.full{width:100%}.actions{display:flex;justify-content:flex-end}@media(max-width:700px){.grid{grid-template-columns:1fr}.wide{grid-column:auto}}
  `]
})
export class AdminLegalSettingsComponent implements OnInit {
  form: FormGroup;
  saving = false;

  constructor(private fb: FormBuilder, private legal: LegalConfigurationService, private snack: MatSnackBar) {
    this.form = this.fb.group({
      legal_name:[''], tax_id:[''], address:[''], contact_email:[''], contact_phone:[''], privacy_email:[''], dpo_email:[''], website:[''],
      registration_information:[''], providers_summary:[''], international_transfers_summary:[''], retention_policy_reference:[''],
      cancellation_policy_summary:[''], support_email:[''], support_policy_summary:[''], claims_email:[''], governing_law_summary:[''],
      non_essential_cookies_summary:[''], cookie_third_parties:[''], subprocessors_summary:[''], document_version:['1'], last_update_date:['']
    });
  }

  ngOnInit(): void {
    this.legal.getPlatform().subscribe({
      next: settings => { const values: Record<string,string> = {}; settings.forEach(s => values[s.key]=s.value ?? ''); this.form.patchValue(values); },
      error: () => this.snack.open('No se pudo cargar la configuración legal de DietoExpress.', 'Cerrar', {duration:4000})
    });
  }

  save(): void {
    if (this.saving) return;
    this.saving = true;
    this.legal.savePlatform(this.form.getRawValue()).subscribe({
      next: () => { this.saving=false; this.snack.open('Configuración legal de DietoExpress guardada.', 'Cerrar', {duration:3000}); },
      error: () => { this.saving=false; this.snack.open('No se pudo guardar la configuración legal.', 'Cerrar', {duration:4000}); }
    });
  }
}
