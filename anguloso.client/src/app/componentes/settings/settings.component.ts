import { Component, OnInit, OnDestroy } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, filter, switchMap } from 'rxjs/operators';
import { CommonModule } from '@angular/common';
import { ProfileService } from '../../servicios/profile.service';
import { AuthService } from '../../servicios/auth.service';
import { Profile } from '../../modelos/profile';
import { LegalConfigurationService } from '../../servicios/legal-configuration.service';

// Angular Material
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatTabsModule } from '@angular/material/tabs';

@Component({
  selector: 'app-settings',
  standalone: true,
  templateUrl: './settings.component.html',
  styleUrls: ['./settings.component.css'],
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatCardModule,
    MatDividerModule,
    MatTabsModule
  ]
})
// Documentación: este componente coordina estado local, validación y llamadas asíncronas; la vista solo refleja ese estado.
// La pantalla de ajustes separa la edición local del envío persistente para evitar mostrar como guardado un cambio que el backend haya rechazado.
export class SettingsComponent implements OnInit, OnDestroy {
  form!: FormGroup;
  loading = false;
  saving = false;
  saveState: 'idle' | 'saving' | 'saved' | 'error' = 'idle';
  private saveChanges$ = new Subject<void>();
  private saveSubscription?: Subscription;
  private formChangesSubscription?: Subscription;
  logoPreview: string | null = null;
  profile: Profile | null = null;
  passwordForm!: FormGroup;
  legalForm!: FormGroup;
  legalSaving = false;
  isClinicAccount = false;
  passwordSaving = false;

  constructor(
    private fb: FormBuilder,
    private profileService: ProfileService,
    private authService: AuthService,
    private legalConfigurationService: LegalConfigurationService,
    private snack: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.passwordForm = this.fb.group({
      oldPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, Validators.minLength(12)]],
      newPasswordRep: ['', [Validators.required, Validators.minLength(12)]]
    });

    this.legalForm = this.fb.group({
      legal_name: [''],
      tax_id: [''],
      address: [''],
      contact_email: [''],
      contact_phone: [''],
      privacy_email: [''],
      dpo_email: [''],
      website: [''],
      professional_title: [''],
      professional_college: [''],
      professional_collegiate_number: [''],
      professional_title_country: [''],
      patient_privacy_legal_basis: [''],
      patient_recipients_summary: [''],
      patient_retention_summary: [''],
      privacy_policy_url: [''],
      consultation_description: [''],
      consultation_limits: [''],
      service_prices_summary: [''],
      booking_payment_summary: [''],
      appointment_cancellation_summary: [''],
      refund_summary: [''],
      no_show_summary: ['']
    });
    this.isClinicAccount = this.authService.getRole() === 'clinic_admin';

    this.form = this.fb.group({
      fullName: [''],
      clinicName: [''],
      clinicAddress: [''],
      clinicPhone: [''],
      clinicLogo: ['']
    });

    this.loading = true;

    this.saveSubscription = this.saveChanges$.pipe(
      debounceTime(700),
      filter(() => !this.loading),
      switchMap(() => {
        this.saving = true;
        this.saveState = 'saving';
        return this.profileService.updateProfile(this.form.value);
      })
    ).subscribe({
      next: () => {
        this.saving = false;
        this.saveState = 'saved';
      },
      error: () => {
        this.saving = false;
        this.saveState = 'error';
      }
    });

    this.legalConfigurationService.getProfessional().subscribe({
      next: settings => {
        const values: Record<string, string> = {};
        settings.forEach(setting => values[setting.key] = setting.value ?? '');
        this.legalForm.patchValue(values);
      },
      error: () => this.snack.open('No se pudo cargar la configuración legal.', 'Cerrar', { duration: 4000 })
    });

    this.profileService.getProfile().subscribe({
      next: (data) => {
        this.profile = data;
        this.form.patchValue({
          fullName: data.fullName ?? '',
          clinicName: data.clinicName ?? '',
          clinicAddress: data.clinicAddress ?? '',
          clinicPhone: data.clinicPhone ?? '',
          clinicLogo: data.clinicLogo ?? ''
        });
        if (data.clinicLogo) {
          this.logoPreview = data.clinicLogo;
        }
        this.formChangesSubscription = this.form.valueChanges.subscribe(() => this.saveChanges$.next());
        this.loading = false;
        this.saveState = 'saved';
      },
      error: () => {
        this.snack.open('Error al cargar el perfil', 'Cerrar', { duration: 3000 });
        this.loading = false;
      }
    });
  }

  saveLegal(): void {
    if (this.legalSaving) return;
    this.legalSaving = true;
    this.legalConfigurationService.saveProfessional(this.legalForm.getRawValue()).subscribe({
      next: () => {
        this.legalSaving = false;
        this.snack.open('Configuración legal guardada.', 'Cerrar', { duration: 3000 });
      },
      error: () => {
        this.legalSaving = false;
        this.snack.open('No se pudo guardar la configuración legal.', 'Cerrar', { duration: 4000 });
      }
    });
  }

  onLogoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    if (!input.files?.length) return;
    const file = input.files[0];
    const reader = new FileReader();
    reader.onload = () => {
      const base64 = reader.result as string;
      this.logoPreview = base64;
      this.form.patchValue({ clinicLogo: base64 });
    };
    reader.readAsDataURL(file);
  }

  removeLogo(): void {
    this.logoPreview = null;
    this.form.patchValue({ clinicLogo: '' });
  }

  save(): void {
    this.saveChanges$.next();
  }

  changePassword(): void {
    if (this.passwordForm.invalid || this.passwordSaving) return;

    const { oldPassword, newPassword, newPasswordRep } = this.passwordForm.value;
    if (newPassword !== newPasswordRep) {
      this.snack.open('Las nuevas contraseñas no coinciden.', 'Cerrar', { duration: 3000 });
      return;
    }

    this.passwordSaving = true;
    this.authService.changePassword(oldPassword, newPassword, newPasswordRep).subscribe({
      next: (result) => {
        this.passwordSaving = false;
        if (result.exito) {
          this.passwordForm.reset();
          this.snack.open('Contraseña cambiada correctamente. Las demás sesiones han quedado invalidadas.', 'Cerrar', { duration: 5000 });
        } else {
          this.snack.open(result.mensaje || 'No se pudo cambiar la contraseña.', 'Cerrar', { duration: 4000 });
        }
      },
      error: (err) => {
        this.passwordSaving = false;
        this.snack.open(err?.error?.mensaje || err?.error || 'No se pudo cambiar la contraseña.', 'Cerrar', { duration: 4000 });
      }
    });
  }

  ngOnDestroy(): void {
    this.saveSubscription?.unsubscribe();
    this.formChangesSubscription?.unsubscribe();
  }
}
