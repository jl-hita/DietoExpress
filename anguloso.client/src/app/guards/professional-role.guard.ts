import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivate, Router } from '@angular/router';
import { AuthService } from '../servicios/auth.service';

@Injectable({ providedIn: 'root' })
// Evita abrir vistas profesionales sin contexto de tenant; la API mantiene la autorización definitiva.
export class ProfessionalRoleGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) {}

  canActivate(route: ActivatedRouteSnapshot): boolean {
    const role = this.authService.getRole();
    if (!this.authService.isLoggedIn()) {
      this.router.navigate(['/login']);
      return false;
    }

    if (role === 'nutritionist' || role === 'clinic_admin') return true;

    // SuperAdmin puede entrar solo en vistas con un modo explícito de plataforma; nunca en detalles/edición que exigen un tenant.
    const platformViews = new Set(['clients', 'diets', 'messages', 'appointments', 'automations', 'documents', 'exercise-manager']);
    if (role === 'superadmin' && platformViews.has(route.routeConfig?.path ?? '')) return true;

    this.router.navigate(['/admin']);
    return false;
  }
}
