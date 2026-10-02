import { Injectable } from '@angular/core';
import { CanActivate, Router } from '@angular/router';
import { AuthService } from '../servicios/auth.service';

@Injectable({
  providedIn: 'root'
})
// El guard evita navegación administrativa desde el cliente, pero la autorización efectiva sigue estando en el backend.
export class SuperAdminGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) {}

  canActivate(): boolean {
    if (this.authService.isLoggedIn() && this.authService.isSuperAdmin()) {
      return true;
    }
    this.router.navigate(['/']);
    return false;
  }
}
