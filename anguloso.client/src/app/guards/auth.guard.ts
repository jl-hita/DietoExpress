import { ActivatedRouteSnapshot, CanActivate, Router, RouterStateSnapshot } from '@angular/router';
import { AuthService } from '../servicios/auth.service';
import { AdminService } from '../servicios/admin.service';
import { Injectable } from '@angular/core';
import { Observable, of } from 'rxjs';
import { map, catchError, switchMap } from 'rxjs/operators';

@Injectable({
  providedIn: 'root'
})
// Primero se usa la sesión local; si no existe, se intenta restaurarla antes de resolver definitivamente la navegación.
export class AuthGuard implements CanActivate {
  constructor(
    private authService: AuthService,
    private adminService: AdminService,
    private router: Router
  ) { }

  canActivate(
    route: ActivatedRouteSnapshot,
    state: RouterStateSnapshot
  ): Observable<boolean> | boolean {
    if (this.authService.isLoggedIn()) {
      return true;
    }

    return this.authService.restoreSession().pipe(
      switchMap(isAuthenticated => {
        if (isAuthenticated) {
          return of(true);
        }

        return this.adminService.getSetupStatus().pipe(
          map(res => {
            if (!res.isConfigured) {
              this.router.navigate(['/setup']);
              return false;
            }

            this.router.navigate(['/login']);
            return false;
          }),
          catchError(() => {
            this.router.navigate(['/login']);
            return of(false);
          })
        );
      }),
      catchError(() => {
        this.router.navigate(['/login']);
        return of(false);
      })
    );
  }
}
