import { Component, OnInit, OnDestroy } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, filter, switchMap } from 'rxjs/operators';
import { CommonModule } from '@angular/common';
import { ProfileService } from '../../servicios/profile.service';
import { AuthService } from '../../servicios/auth.service';
import { Profile } from '../../modelos/profile';

// Angular Material
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';

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
    MatDividerModule
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
  passwordSaving = false;

  constructor(
    private fb: FormBuilder,
    private profileService: ProfileService,
    private authService: AuthService,
    private snack: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.passwordForm = this.fb.group({
      oldPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, Validators.minLength(12)]],
      newPasswordRep: ['', [Validators.required, Validators.minLength(12)]]
    });

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
