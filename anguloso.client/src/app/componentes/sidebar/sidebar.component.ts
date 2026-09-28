import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../servicios/auth.service';
import { Profile } from '../../modelos/profile';
import { ProfileService } from '../../servicios/profile.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink, MatListModule, MatIconModule],
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.css']
})
export class SidebarComponent implements OnInit {
  profile: Profile | null = null;
  readonly defaultProfileImage = '/assets/profile.png';

  constructor(
    private router: Router,
    private authService: AuthService,
    private profileService: ProfileService
  ) { }

  ngOnInit(): void {
    this.profileService.profile$.subscribe(profile => {
      this.profile = profile;
    });

    // Carga el perfil si todavía no está disponible en memoria.
    if (!this.profile) {
      this.profileService.getProfile().subscribe();
    }
  }

  get profileImage(): string {
    return this.profile?.clinicLogo || this.defaultProfileImage;
  }

  onProfileImageError(): void {
    if (this.profile?.clinicLogo) {
      this.profile = { ...this.profile, clinicLogo: undefined };
    }
  }

  get userName(): string {
    const user = this.authService.getUser();
    return user?.unique_name ?? user?.name ?? this.profile?.fullName ?? 'Usuario';
  }

  get isSuperAdmin(): boolean {
    return this.authService.isSuperAdmin();
  }

  get isClinic(): boolean {
    const role = this.authService.getRole();
    return role === 'clinic_admin';
  }

  get isProfessionalAccount(): boolean {
    const plan = this.authService.getSubscriptionPlan();
    return this.isSuperAdmin || plan === 'demo_nutri' || plan === 'nutri_full' || plan === 'clinic_full';
  }

  logout() {
    this.authService.logout();
    this.router.navigate(['/']);
  }
}
