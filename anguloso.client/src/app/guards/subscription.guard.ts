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
    if (plan === 'free') {
      this.router.navigate(['/onboarding']);
      return false;
    }

    return plan === 'demo_nutri' || plan === 'nutri_full' || plan === 'clinic_full';
  }
}
