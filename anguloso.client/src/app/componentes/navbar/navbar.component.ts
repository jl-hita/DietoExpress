import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { AppRoute } from '../../app.routes';
import { MatToolbarModule } from '@angular/material/toolbar';
import { AuthService } from '../../servicios/auth.service';

@Component({
  selector: 'app-menu',
  templateUrl: './navbar.component.html',
  styleUrls: ['./navbar.component.css'],
    standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule],
})
export class NavbarComponent {
  menuRoutes: AppRoute[] = [];

  constructor(private router: Router, private authService: AuthService) {
    // Filtra las rutas que deben mostrarse en el menú
    this.menuRoutes = (this.router.config as AppRoute[]).filter(r => r.showInMenu);
  }

  navigateTo(path: string) {
    this.router.navigate([path]);
  }

  logout() {
    this.authService.logout().subscribe(() => this.router.navigate(['/login']));
  }
}
