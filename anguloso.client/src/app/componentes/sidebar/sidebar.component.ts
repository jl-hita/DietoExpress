import { CommonModule } from '@angular/common';
import { Component, EventEmitter, OnInit, Output } from '@angular/core';
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
// El menú deriva sus opciones del contexto actual del usuario para ocultar rutas que no son relevantes, sin sustituir la autorización del servidor.
export class SidebarComponent implements OnInit {
  @Output() closeMenu = new EventEmitter<void>();

  profile: Profile | null = null;
  readonly defaultProfileImage = 'data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 48 48"%3E%3Ccircle cx="24" cy="24" r="24" fill="%23e0e0e0"/%3E%3Ccircle cx="24" cy="18" r="8" fill="%23909090"/%3E%3Cpath d="M10 40c2-8 9-12 14-12s12 4 14 12" fill="%23909090"/%3E%3C/svg%3E';

  constructor(
    private router: Router,
    private authService: AuthService,
    private profileService: ProfileService
  ) { }

  ngOnInit(): void {
    this.profileService.profile$.subscribe(profile => {
      this.profile = profile;
    });

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
    return user?.username ?? this.profile?.fullName ?? 'Usuario';
  }

  get isSuperAdmin(): boolean {
    return this.authService.isSuperAdmin();
  }

  get isClinic(): boolean {
    const role = this.authService.getRole();
    return role === 'clinic_admin';
  }

  get isNutritionist(): boolean {
    return this.authService.getRole() === 'nutritionist';
  }

  get isFreeAccount(): boolean {
    return this.authService.getSubscriptionPlan() === 'free';
  }

  get isProfessionalAccount(): boolean {
    const plan = this.authService.getSubscriptionPlan();
    return this.isSuperAdmin || plan === 'demo_nutri' || plan === 'nutri_full' || plan === 'clinic_full';
  }

  logout() {
    this.authService.logout().subscribe(() => {
      this.closeMenu.emit();
      this.router.navigate(['/']);
    });
  }

  navigateAndClose(): void {
    this.closeMenu.emit();
  }
}
