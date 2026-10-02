import { Injectable } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { AdminService } from '../servicios/admin.service';
import { Observable, of } from 'rxjs';
import { map, catchError } from 'rxjs/operators';

@Injectable({
  providedIn: 'root'
})
// El acceso al asistente depende del estado persistido de configuración; ante un error se evita abrir una ruta de inicialización insegura.
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
        // Si no podemos verificar el estado, no debemos abrir el asistente de inicialización.
        this.router.navigate(['/login']);
        return of(false);
      })
    );
  }
}
