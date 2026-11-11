import { Component, ChangeDetectionStrategy, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../servicios/auth.service';
import { AdminService } from '../../servicios/admin.service';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { PublicPlan, PublicPlansService, planFeatureLabels, yearlySavingsPercent } from '../../servicios/public-plans.service';
import { LANDING_CONTENT, publishableScreenshots, publishableTestimonials, publishableVideo } from './landing.content';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-landing',
  standalone: true,
  imports: [CommonModule, RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.css']
})
// La landing mantiene su estado de presentación independiente de la lógica autenticada para que no pueda alterar el contexto de sesión.
export class LandingComponent implements OnInit {
  // Planes reales desde el catálogo de cobro. Si no se pueden cargar, la sección vuelve a mostrar solo la invitación a empezar.
  plans: PublicPlan[] = [];
  billingInterval: 'monthly' | 'yearly' = 'yearly';

  // Vídeo, capturas y testimonios: se rellenan en landing.content.ts. Cada sección solo se muestra si tiene contenido publicable.
  readonly video = publishableVideo(LANDING_CONTENT);
  readonly screenshots = publishableScreenshots(LANDING_CONTENT);
  readonly testimonials = publishableTestimonials(LANDING_CONTENT);

  private getPostLoginRoute(role: string | null): string[] {
    switch (role) {
      case "superadmin": return ["/admin"];
      case "clinic_admin": return ["/clinic"];
      case "nutritionist": return ["/dashboard"];
      default: return ["/login"];
    }
  }
  constructor(
    private router: Router,
    private authService: AuthService,
    private adminService: AdminService,
    private publicPlans: PublicPlansService
  ) {}

  ngOnInit(): void {
    if (this.authService.isLoggedIn()) {
      this.router.navigate(this.getPostLoginRoute(this.authService.getRole()));
      return;
    }

    this.publicPlans.getPlans().subscribe({
      next: res => { this.plans = res.plans ?? []; },
      error: () => { this.plans = []; }
    });

    // En una instalación nueva, llevar directamente al asistente inicial.
    this.adminService.getSetupStatus().subscribe({
      next: (res) => {
        if (!res.isConfigured) {
          this.router.navigate(['/setup']);
        }
      },
      error: (err) => {
        console.error('No se pudo comprobar el estado de configuración', err);
      }
    });
  }

  crearCuenta(): void {
    this.router.navigate(['/crear-usuario']);
  }

  iniciarSesion(): void {
    this.router.navigate(['/login']);
  }

  // Con facturación anual el precio principal es la media mensual, que es lo que el profesional compara.
  displayedPrice(plan: PublicPlan): number {
    return this.billingInterval === 'yearly' ? plan.yearlyPrice / 12 : plan.monthlyPrice;
  }

  savings(plan: PublicPlan): number { return yearlySavingsPercent(plan); }

  hasYearlySavings(): boolean { return this.plans.some(plan => yearlySavingsPercent(plan) > 0); }

  maxSavings(): number { return Math.max(0, ...this.plans.map(plan => yearlySavingsPercent(plan))); }

  featureLabels(plan: PublicPlan): string[] { return planFeatureLabels(plan); }

  // Los días de prueba de los planes que la ofrecen (hoy solo el individual). Ninguno => no se menciona la prueba.
  trialPlan(): PublicPlan | undefined { return this.plans.find(plan => !!plan.trialDays && plan.trialDays > 0); }

  isFeatured(plan: PublicPlan): boolean { return this.plans.length > 1 && plan.code === 'nutri_full'; }

  verPlanes(): void {
    this.router.navigate(['/crear-usuario']);
  }
}
