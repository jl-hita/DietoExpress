import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AdminService } from '../../servicios/admin.service';

@Component({
  selector: 'app-setup-wizard',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    ReactiveFormsModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatSnackBarModule
  ],
  template: `
    <div class="setup-container">
      <div class="setup-card-wrapper">
        <div class="brand-header">
          <mat-icon class="brand-icon">admin_panel_settings</mat-icon>
          <h1>DietoExpress</h1>
          <p class="subtitle">Asistente de Inicialización del Sistema</p>
        </div>

        <mat-card class="setup-card">
          <mat-card-header>
            <mat-card-title>Crear Cuenta de SuperAdministrador</mat-card-title>
            <mat-card-subtitle>
              Es la primera vez que se inicia la aplicación. Configura la cuenta del propietario para gestionar usuarios, licencias y suscripciones.
            </mat-card-subtitle>
          </mat-card-header>

          <mat-card-content>
            <form [formGroup]="form" (ngSubmit)="submit()">
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>Nombre Completo</mat-label>
                <input matInput formControlName="fullName" placeholder="Ej: Dr. Alejandro Sanz" />
                <mat-icon matSuffix>badge</mat-icon>
                <mat-error *ngIf="form.get('fullName')?.hasError('required')">El nombre es requerido</mat-error>
              </mat-form-field>

              <mat-form-field appearance="outline" class="full-width">
                <mat-label>Nombre de Usuario</mat-label>
                <input matInput formControlName="username" placeholder="Ej: superadmin" />
                <mat-icon matSuffix>person</mat-icon>
                <mat-error *ngIf="form.get('username')?.hasError('required')">El usuario es requerido</mat-error>
              </mat-form-field>

              <mat-form-field appearance="outline" class="full-width">
                <mat-label>Correo Electrónico</mat-label>
                <input matInput type="email" formControlName="email" placeholder="admin@dietoexpress.com" />
                <mat-icon matSuffix>email</mat-icon>
                <mat-error *ngIf="form.get('email')?.hasError('required')">El correo es requerido</mat-error>
                <mat-error *ngIf="form.get('email')?.hasError('email')">Formato de correo inválido</mat-error>
              </mat-form-field>

              <mat-form-field appearance="outline" class="full-width">
                <mat-label>Contraseña Maestra</mat-label>
                <input matInput [type]="hidePassword ? 'password' : 'text'" formControlName="password" />
                <button mat-icon-button matSuffix type="button" (click)="hidePassword = !hidePassword">
                  <mat-icon>{{ hidePassword ? 'visibility_off' : 'visibility' }}</mat-icon>
                </button>
                <mat-error *ngIf="form.get('password')?.hasError('required')">La contraseña es requerida</mat-error>
                <mat-error *ngIf="form.get('password')?.hasError('minlength')">Mínimo 8 caracteres</mat-error>
              </mat-form-field>

              <div class="alert-info-box">
                <mat-icon>info</mat-icon>
                <span>Este paso solo se puede ejecutar una vez. Al completarlo, este asistente quedará deshabilitado permanentemente.</span>
              </div>

              <div class="actions">
                <button mat-flat-button color="primary" type="submit" [disabled]="form.invalid || loading" class="submit-btn">
                  <span *ngIf="!loading">Inicializar Sistema</span>
                  <span *ngIf="loading">Configurando...</span>
                </button>
              </div>
            </form>
          </mat-card-content>
        </mat-card>
      </div>
    </div>
  `,
  styles: [`
    .setup-container {
      display: flex;
      justify-content: center;
      align-items: center;
      min-height: 100vh;
      background: linear-gradient(135deg, #0f172a 0%, #1e293b 50%, #0f766e 100%);
      padding: 24px;
      box-sizing: border-box;
      font-family: 'Roboto', sans-serif;
    }
    .setup-card-wrapper {
      max-width: 520px;
      width: 100%;
    }
    .brand-header {
      text-align: center;
      margin-bottom: 24px;
      color: #ffffff;
    }
    .brand-icon {
      font-size: 52px;
      width: 52px;
      height: 52px;
      color: #14b8a6;
      margin-bottom: 8px;
    }
    .brand-header h1 {
      margin: 0;
      font-size: 32px;
      font-weight: 700;
      letter-spacing: -0.5px;
    }
    .subtitle {
      margin-top: 6px;
      color: #94a3b8;
      font-size: 15px;
    }
    .setup-card {
      border-radius: 16px;
      box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.4), 0 8px 10px -6px rgba(0, 0, 0, 0.3);
      padding: 24px;
    }
    mat-card-header {
      margin-bottom: 20px;
    }
    mat-card-title {
      font-size: 20px;
      font-weight: 600;
      color: #0f172a;
    }
    mat-card-subtitle {
      margin-top: 6px;
      color: #64748b;
      line-height: 1.4;
    }
    .full-width {
      width: 100%;
      margin-bottom: 12px;
    }
    .alert-info-box {
      display: flex;
      align-items: center;
      gap: 12px;
      background: #f0fdfa;
      border: 1px solid #99f6e4;
      color: #0f766e;
      padding: 12px 16px;
      border-radius: 8px;
      font-size: 13px;
      margin-bottom: 20px;
    }
    .alert-info-box mat-icon {
      font-size: 20px;
      width: 20px;
      height: 20px;
      flex-shrink: 0;
    }
    .actions {
      display: flex;
      justify-content: flex-end;
    }
    .submit-btn {
      width: 100%;
      padding: 12px;
      font-size: 16px;
      font-weight: 600;
      border-radius: 8px;
      background-color: #0d9488 !important;
      color: white !important;
    }
  `]
})
export class SetupWizardComponent implements OnInit {
  form: FormGroup;
  hidePassword = true;
  loading = false;

  constructor(
    private fb: FormBuilder,
    private adminService: AdminService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {
    this.form = this.fb.group({
      fullName: ['', Validators.required],
      username: ['', Validators.required],
      email: ['', [Validators.required, Validators.email]],
      password: ['', [Validators.required, Validators.minLength(8)]]
    });
  }

  ngOnInit(): void {
    // Si ya está configurado, mandar a login
    this.adminService.getSetupStatus().subscribe({
      next: (res) => {
        if (res.isConfigured) {
          this.router.navigate(['/login']);
        }
      }
    });
  }

  submit(): void {
    if (this.form.invalid) return;

    this.loading = true;
    this.adminService.initSuperAdmin(this.form.value).subscribe({
      next: () => {
        this.snackBar.open('¡Sistema configurado con éxito! Por favor inicia sesión.', 'Cerrar', {
          duration: 5000,
          horizontalPosition: 'center',
          verticalPosition: 'bottom'
        });
        this.router.navigate(['/login']);
      },
      error: (err) => {
        this.loading = false;
        const msg = err.error?.message || err.error || 'Error al configurar el sistema.';
        this.snackBar.open(msg, 'Entendido', { duration: 6000 });
      }
    });
  }
}
