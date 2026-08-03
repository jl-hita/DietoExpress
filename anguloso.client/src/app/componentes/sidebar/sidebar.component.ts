import { Component } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../servicios/auth.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [RouterLink, MatListModule, MatIconModule],
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.css']
})
export class SidebarComponent {
  constructor(private router: Router, private authService: AuthService) { }

  get userName(): string {
    const user = this.authService.getUser();
    return user?.unique_name ?? user?.name ?? 'Usuario';
  }

  logout() {
    this.authService.logout();
    this.router.navigate(['/login']);
  }
}
