import { Component } from '@angular/core';
import { Router } from '@angular/router';

@Component({
  selector: 'app-landing',
  standalone: true,
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.css']
})
export class LandingComponent {
  constructor(private router: Router) {}

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
