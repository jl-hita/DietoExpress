import { CommonModule } from '@angular/common';
import { Component, EventEmitter, OnDestroy, OnInit, Output } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Subscription, interval, startWith, switchMap, catchError, forkJoin, of } from 'rxjs';
import { Router, RouterLink, RouterLinkActive } from '@angular/router';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { AuthService } from '../../servicios/auth.service';
import { Profile } from '../../modelos/profile';
import { ProfileService } from '../../servicios/profile.service';
import { LicenseService, LicenseStatus } from '../../servicios/license.service';

@Component({
  selector: 'app-sidebar',
  standalone: true,
  imports: [CommonModule, RouterLink, RouterLinkActive, MatListModule, MatIconModule],
  templateUrl: './sidebar.component.html',
  styleUrls: ['./sidebar.component.css']
})
// El menú deriva sus opciones del contexto actual del usuario para ocultar rutas que no son relevantes, sin sustituir la autorización del servidor.
export class SidebarComponent implements OnInit, OnDestroy {
  @Output() closeMenu = new EventEmitter<void>();

  profile: Profile | null = null;
  license: LicenseStatus | null = null;
  unreadMessagesCount = 0;
  private unreadMessagesSubscription?: Subscription;
  readonly defaultProfileImage = 'data:image/svg+xml,%3Csvg xmlns="http://www.w3.org/2000/svg" width="48" height="48" viewBox="0 0 48 48"%3E%3Ccircle cx="24" cy="24" r="24" fill="%23e0e0e0"/%3E%3Ccircle cx="24" cy="18" r="8" fill="%23909090"/%3E%3Cpath d="M10 40c2-8 9-12 14-12s12 4 14 12" fill="%23909090"/%3E%3C/svg%3E';

  constructor(
    private router: Router,
    private authService: AuthService,
    private profileService: ProfileService,
    private licenseService: LicenseService,
    private http: HttpClient
  ) { }

  ngOnInit(): void {
    this.profileService.profile$.subscribe(profile => {
      this.profile = profile;
    });

    if (!this.profile) {
      this.profileService.getProfile().subscribe();
    }

    // El contador reúne chats privados y comunicaciones oficiales; si un endpoint no está disponible para un rol, no bloquea el otro.
    this.unreadMessagesSubscription = interval(15000).pipe(
      startWith(0),
      switchMap(() => forkJoin({
        conversations: this.http.get<any[]>('/api/messages/conversations').pipe(catchError(() => of([]))),
        broadcasts: this.http.get<any[]>('/api/broadcast-messages/inbox').pipe(catchError(() => of([])))
      }))
    ).subscribe(result => {
      const unreadChats = (result.conversations || []).reduce((sum, item) => sum + Math.max(0, Number(item.unreadCount) || 0), 0);
      const unreadBroadcasts = (result.broadcasts || []).filter(item => !item.readAt).length;
      this.unreadMessagesCount = unreadChats + unreadBroadcasts;
    });
  }

  ngOnDestroy(): void { this.unreadMessagesSubscription?.unsubscribe(); }

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

  get isTrial(): boolean { return this.license?.planCode === 'free' && !!this.license?.expiresAt; }

  get trialDaysRemaining(): number {
    if (!this.license?.expiresAt) return 0;
    return Math.max(0, Math.ceil((new Date(this.license.expiresAt).getTime() - Date.now()) / 86400000));
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
