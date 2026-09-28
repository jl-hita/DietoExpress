import { Injectable } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { AuthService } from '../servicios/auth.service';

@Injectable({ providedIn: 'root' })
export class SubscriptionGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) {}

  canActivate(): boolean {
    if (!this.authService.isLoggedIn()) {
      this.router.navigate(['/login']);
      return false;
    }

    if (this.authService.isSuperAdmin()) {
      return true;
    }

    const plan = this.authService.getSubscriptionPlan();

    // FREE es una cuenta sandbox: puede entrar en clientes y dietas
    // y probar el producto dentro de sus límites.
    return plan === 'free'
      || plan === 'demo_nutri'
      || plan === 'nutri_full'
      || plan === 'clinic_full';
  }
}
