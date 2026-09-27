import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../servicios/auth.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink, MatListModule, MatIconModule],
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.css']
})
export class SidebarComponent {
  constructor(private router: Router, private authService: AuthService) { }

  get userName(): string {
    const user = this.authService.getUser();
    return user?.unique_name ?? user?.name ?? 'Usuario';
  }

  get isSuperAdmin(): boolean {
    return this.authService.isSuperAdmin();
  }

  get isClinic(): boolean {
    const role = this.authService.getRole();
    return role === 'clinic_admin';
  }

  logout() {
    this.authService.logout();
    this.router.navigate(['/login']);
  }
}
