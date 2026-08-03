import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { CommonModule } from '@angular/common';
import { ProfileService } from '../../servicios/profile.service';
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
export class SettingsComponent implements OnInit {
  form!: FormGroup;
  loading = false;
  saving = false;
  logoPreview: string | null = null;
  profile: Profile | null = null;

  constructor(
    private fb: FormBuilder,
    private profileService: ProfileService,
    private snack: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      fullName: [''],
      clinicName: [''],
      clinicAddress: [''],
      clinicPhone: [''],
      clinicLogo: ['']
    });

    this.loading = true;
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
        this.loading = false;
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
    if (this.saving) return;
    this.saving = true;
    this.profileService.updateProfile(this.form.value).subscribe({
      next: () => {
        this.snack.open('Perfil guardado correctamente', 'Cerrar', { duration: 3000 });
        this.saving = false;
      },
      error: () => {
        this.snack.open('Error al guardar el perfil', 'Cerrar', { duration: 3000 });
        this.saving = false;
      }
    });
  }
}
