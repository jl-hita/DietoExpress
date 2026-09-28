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
export class OnboardingComponent {
  constructor(private router: Router, private authService: AuthService) {}

  get userName(): string {
    const user = this.authService.getUser();
    return user?.unique_name ?? user?.name ?? 'profesional';
  }

  irABilling(): void {
    this.router.navigate(['/billing']);
  }

  continuar(): void {
    this.router.navigate(['/billing']);
  }

  cerrarSesion(): void {
    this.authService.logout();
    this.router.navigate(['/']);
  }
}
