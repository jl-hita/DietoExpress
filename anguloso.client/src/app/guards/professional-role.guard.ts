import { Injectable } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { AuthService } from '../servicios/auth.service';

@Injectable({ providedIn: 'root' })
// Evita abrir vistas profesionales sin contexto de tenant; la API mantiene la autorización definitiva.
export class ProfessionalRoleGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) {}

  canActivate(): boolean {
    const role = this.authService.getRole();
    if (this.authService.isLoggedIn() && (role === 'nutritionist' || role === 'clinic_admin')) {
      return true;
    }

    this.router.navigate(['/admin']);
    return false;
  }
}
