import { Component } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../servicios/auth.service';
import { AdminService } from '../../servicios/admin.service';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-landing',
  standalone: true,
  imports: [RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.css']
})
// La landing mantiene su estado de presentación independiente de la lógica autenticada para que no pueda alterar el contexto de sesión.
export class LandingComponent {
  constructor(
    private router: Router,
    private authService: AuthService,
    private adminService: AdminService
  ) {}

  ngOnInit(): void {
    if (this.authService.isLoggedIn()) {
      this.router.navigate(['/clients']);
      return;
    }

    // En una instalación nueva, llevar directamente al asistente inicial.
    this.adminService.getSetupStatus().subscribe({
      next: (res) => {
        if (!res.isConfigured) {
          this.router.navigate(['/setup']);
        }
      },
      error: (err) => {
        console.error('No se pudo comprobar el estado de configuración', err);
      }
    });
  }

  crearCuenta(): void {
    this.router.navigate(['/crear-usuario']);
  }

  iniciarSesion(): void {
    this.router.navigate(['/login']);
  }

  verPlanes(): void {
    this.router.navigate(['/crear-usuario']);
  }
}
