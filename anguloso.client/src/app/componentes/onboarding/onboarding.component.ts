import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { AuthService } from '../../servicios/auth.service';
import { ProfileService } from '../../servicios/profile.service';
import { Profile } from '../../modelos/profile';
import { LicenseService, LicenseStatus } from '../../servicios/license.service';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatCardModule } from '@angular/material/card';

interface OnboardingStep {
  key: string;
  title: string;
  description: string;
  icon: string;
  route: string;
  complete: boolean;
  optional?: boolean;
}

// Documentación: este componente concentra el checklist de activación profesional sin duplicar los formularios existentes.
// Cada paso refleja datos ya persistidos por los módulos correspondientes; el onboarding no mantiene un segundo estado de configuración.
@Component({
  selector: 'app-onboarding',
  standalone: true,
  imports: [
    CommonModule,
    RouterLink,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatProgressBarModule,
    MatCardModule
  ],
  templateUrl: './onboarding.component.html',
  styleUrls: ['./onboarding.component.css']
})
export class OnboardingComponent implements OnInit {
  profile: Profile | null = null;
  license: LicenseStatus | null = null;
  loading = true;
  error = false;
  steps: OnboardingStep[] = [];

  constructor(
    private router: Router,
    private authService: AuthService,
    private profileService: ProfileService,
    private licenseService: LicenseService
  ) {}

  ngOnInit(): void {
    if (this.authService.isSuperAdmin()) {
      this.loading = false;
      this.buildSteps(null, null);
      return;
    }

    forkJoin({
      profile: this.profileService.getProfile(),
      license: this.licenseService.getLicense()
    }).subscribe({
      next: ({ profile, license }) => {
        this.profile = profile;
        this.license = license;
        this.buildSteps(profile, license);
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.error = true;
      }
    });
  }

  get userName(): string {
    return this.profile?.fullName || this.authService.getUser()?.username || 'profesional';
  }

  get completedCount(): number {
    return this.steps.filter(step => step.complete).length;
  }

  get progress(): number {
    return this.steps.length ? Math.round(this.completedCount * 100 / this.steps.length) : 100;
  }

  get isComplete(): boolean {
    return this.steps.length > 0 && this.completedCount === this.steps.length;
  }

  private buildSteps(profile: Profile | null, license: LicenseStatus | null): void {
    const hasProfessionalName = !!profile?.fullName?.trim();
    const hasClinic = !!profile?.clinicName?.trim() && !!profile?.clinicAddress?.trim();
    const hasSpecialties = !!profile?.directorySpecialties?.trim();
    const hasPublicProfile = !!profile?.directoryEnabled && !!profile?.directorySlug;
    const hasPatient = (license?.clients ?? 0) > 0;
    const hasPaidPlan = license ? ['nutri_full', 'clinic_full'].includes(license.planCode) : false;

    this.steps = [
      {
        key: 'professional',
        title: 'Completa tus datos profesionales',
        description: 'Nombre profesional y datos básicos que aparecerán en tu consulta.',
        icon: 'badge',
        route: '/settings',
        complete: hasProfessionalName
      },
      {
        key: 'clinic',
        title: 'Configura tu consulta',
        description: 'Nombre y dirección de la clínica o consulta para tus documentos y perfil.',
        icon: 'business',
        route: '/settings',
        complete: hasClinic
      },
      {
        key: 'specialties',
        title: 'Define tus especialidades',
        description: 'Indica tus áreas de trabajo para que los pacientes encuentren el perfil adecuado.',
        icon: 'workspace_premium',
        route: '/settings',
        complete: hasSpecialties
      },
      {
        key: 'documents',
        title: 'Revisa tu documentación',
        description: 'Comprueba las plantillas y documentos que utilizarás con tus pacientes.',
        icon: 'description',
        route: '/documents',
        complete: false,
        optional: true
      },
      {
        key: 'public',
        title: 'Publica tu perfil',
        description: 'Activa el directorio y, si corresponde, las consultas online para empezar a recibir solicitudes.',
        icon: 'public',
        route: '/settings',
        complete: hasPublicProfile,
        optional: true
      },
      {
        key: 'patient',
        title: 'Crea tu primer paciente',
        description: 'Da el primer paso del flujo profesional y prepara una consulta real.',
        icon: 'person_add',
        route: '/clients/nuevo',
        complete: hasPatient
      },
      {
        key: 'billing',
        title: hasPaidPlan ? 'Suscripción configurada' : 'Revisa tu plan',
        description: hasPaidPlan
          ? 'Tu plan profesional está activo.'
          : 'Consulta los planes y activa el que necesites cuando quieras.',
        icon: 'payments',
        route: '/billing',
        complete: hasPaidPlan,
        optional: true
      }
    ];
  }

  goToNext(): void {
    const next = this.steps.find(step => !step.complete && !step.optional)
      ?? this.steps.find(step => !step.complete);
    this.router.navigate([next?.route ?? '/dashboard']);
  }

  goToDashboard(): void {
    this.router.navigate(['/dashboard']);
  }

  cerrarSesion(): void {
    this.authService.logout().subscribe(() => this.router.navigate(['/']));
  }
}
