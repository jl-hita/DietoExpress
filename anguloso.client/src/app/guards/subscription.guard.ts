import { Injectable } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { Observable, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { AuthService } from '../servicios/auth.service';
import { LicenseService } from '../servicios/license.service';

@Injectable({ providedIn: 'root' })
export class SubscriptionGuard implements CanActivate {
  constructor(
    private authService: AuthService,
    private licenseService: LicenseService,
    private router: Router
  ) {}

  canActivate(): Observable<boolean> {
    if (!this.authService.isLoggedIn()) {
      this.router.navigate(['/login']);
      return of(false);
    }

    if (this.authService.isSuperAdmin()) {
      return of(true);
    }

    return this.licenseService.getLicense().pipe(
      map(license => {
        const active = license.status === 'active'
          && (!license.expiresAt || new Date(license.expiresAt).getTime() > Date.now());

        if (!active) {
          this.router.navigate(['/billing'], {
            queryParams: { reason: 'expired' }
          });
          return false;
        }

        return license.planCode === 'free'
          || license.planCode === 'demo_nutri'
          || license.planCode === 'nutri_full'
          || license.planCode === 'clinic_full';
      }),
      catchError(() => {
        this.router.navigate(['/billing']);
        return of(false);
      })
    );
  }
}
