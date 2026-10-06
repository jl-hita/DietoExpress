import { Component, OnInit, OnDestroy } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Subject, Subscription } from 'rxjs';
import { debounceTime, filter, switchMap } from 'rxjs/operators';
import { CommonModule } from '@angular/common';
import { ProfileService } from '../../servicios/profile.service';
import { AuthService } from '../../servicios/auth.service';
import { Profile } from '../../modelos/profile';
import { AddressSuggestion } from '../../modelos/address';
import { AddressAutocompleteComponent } from '../address-autocomplete/address-autocomplete.component';

// Angular Material
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatAutocompleteModule } from '@angular/material/autocomplete';

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
    MatTabsModule,
    MatSlideToggleModule,
    MatAutocompleteModule,
    AddressAutocompleteComponent
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

  readonly provinces = [
    'Álava', 'Albacete', 'Alicante', 'Almería', 'Asturias', 'Ávila', 'Badajoz', 'Barcelona',
    'Bizkaia', 'Burgos', 'Cáceres', 'Cádiz', 'Cantabria', 'Castellón', 'Ciudad Real', 'Córdoba',
    'Cuenca', 'Gipuzkoa', 'Girona', 'Granada', 'Guadalajara', 'Huelva', 'Huesca', 'Illes Balears',
    'Jaén', 'La Rioja', 'Las Palmas', 'León', 'Lleida', 'Lugo', 'Madrid', 'Málaga', 'Murcia',
    'Navarra', 'Ourense', 'Palencia', 'Pontevedra', 'Salamanca', 'Santa Cruz de Tenerife',
    'Segovia', 'Sevilla', 'Soria', 'Tarragona', 'Teruel', 'Toledo', 'Valencia', 'Valladolid',
    'Vizcaya', 'Zamora', 'Zaragoza'
  ];
  filteredProvinces = [...this.provinces];

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
      clinicLogo: [''],
      directoryEnabled: [false],
      onlineConsultations: [false],
      directoryCity: [''],
      directoryProvince: [''],
      directoryBio: ['', Validators.maxLength(2000)],
      directorySpecialties: ['', Validators.maxLength(500)]
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
          clinicLogo: data.clinicLogo ?? '',
          directoryEnabled: data.directoryEnabled ?? false,
          onlineConsultations: data.onlineConsultations ?? false,
          directoryCity: data.directoryCity ?? '',
          directoryProvince: data.directoryProvince ?? '',
          directoryBio: data.directoryBio ?? '',
          directorySpecialties: data.directorySpecialties ?? ''
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

  onClinicAddressSelected(suggestion: AddressSuggestion): void {
    this.form.patchValue({
      clinicAddress: suggestion.displayName,
      directoryCity: suggestion.city || this.form.get('directoryCity')?.value || '',
      directoryProvince: this.canonicalizeProvince(suggestion.province) || this.form.get('directoryProvince')?.value || ''
    });
    this.filterProvinces();
  }

  private canonicalizeProvince(value: string | null | undefined): string {
    const candidate = String(value ?? '').trim();
    if (!candidate) return '';

    const normalize = (text: string) => text
      .normalize('NFD')
      .replace(/[\\u0300-\\u036f]/g, '')
      .toLocaleLowerCase('es');

    const normalizedCandidate = normalize(candidate);
    return this.provinces.find(province => normalize(province) === normalizedCandidate) ?? candidate;
  }

  filterProvinces(): void {
    const value = String(this.form.get('directoryProvince')?.value ?? '').trim().toLocaleLowerCase('es');
    this.filteredProvinces = value
      ? this.provinces.filter(province => province.toLocaleLowerCase('es').includes(value))
      : [...this.provinces];
  }

  onProvinceSelected(province: string): void {
    this.form.get('directoryProvince')?.setValue(province);
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
