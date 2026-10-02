import { Component } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from '../../servicios/auth.service';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-onboarding',
  standalone: true,
  imports: [MatButtonModule, MatIconModule],
  templateUrl: './onboarding.component.html',
  styleUrls: ['./onboarding.component.css']
})
// Documentación: este componente coordina estado local, validación y llamadas asíncronas; la vista solo refleja ese estado.
export class OnboardingComponent {
  constructor(private router: Router, private authService: AuthService) {}

  get userName(): string {
    const user = this.authService.getUser();
    return user?.username ?? 'profesional';
  }

  irABilling(): void {
    this.router.navigate(['/billing']);
  }

  configurarPerfil(): void {
    this.router.navigate(['/settings']);
  }

  cerrarSesion(): void {
    this.authService.logout().subscribe(() => this.router.navigate(['/']));
  }
}
