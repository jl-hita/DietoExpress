import { Injectable } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { AdminService } from '../servicios/admin.service';
import { Observable, of } from 'rxjs';
import { map, catchError } from 'rxjs/operators';

@Injectable({
  providedIn: 'root'
})
export class SetupGuard implements CanActivate {
  constructor(private adminService: AdminService, private router: Router) {}

  canActivate(): Observable<boolean> {
    return this.adminService.getSetupStatus().pipe(
      map(res => {
        if (!res.isConfigured) {
          // No está configurado: permitir acceso al setup wizard
          return true;
        } else {
          // Ya está configurado: no permitir volver al setup
          this.router.navigate(['/login']);
          return false;
        }
      }),
      catchError(() => {
        return of(true);
      })
    );
  }
}
