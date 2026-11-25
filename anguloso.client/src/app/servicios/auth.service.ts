import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { catchError, map, tap } from 'rxjs/operators';

export interface AuthUser {
  username: string;
  email?: string;
  role: string;
  subscriptionPlan?: string;
  subscriptionStatus?: string;
  archivedSupport?: boolean;
}

@Injectable({ providedIn: 'root' })
// Centraliza autenticación, sesión y almacenamiento del token para que los guards y componentes compartan la misma representación de identidad.
export class AuthService {
  private readonly userSubject = new BehaviorSubject<AuthUser | null>(null);
  private sessionRestore$?: Observable<boolean>;
  private renewalTimer?: ReturnType<typeof setInterval>;
  private activeSinceRenewal = false;
  private verifying = false;

  /** La cookie dura 3 horas: mientras se use la aplicación se renueva cada 20 minutos. */
  private static readonly RenewalMs = 20 * 60_000;

  constructor(private http: HttpClient) {
    if (typeof window !== 'undefined') {
      const markActive = () => { this.activeSinceRenewal = true; };
      window.addEventListener('pointerdown', markActive, { passive: true });
      window.addEventListener('keydown', markActive, { passive: true });
    }
  }

  isLoggedIn(): boolean { return this.userSubject.value !== null; }

  login(user: AuthUser): void {
    this.userSubject.next(user);
    this.startRenewal();
  }

  /**
   * Comprueba si la sesión sigue siendo válida; si el servidor la rechaza, la cierra localmente y avisa.
   * Las cuentas archivadas solo acceden a soporte y no se pueden renovar.
   */
  verifySession(onExpired: () => void): void {
    const user = this.userSubject.value;
    if (!user || user.archivedSupport || this.verifying) return;
    this.verifying = true;
    this.refreshSession().subscribe({
      next: () => { this.verifying = false; },
      error: error => {
        this.verifying = false;
        if (error?.status !== 401) return;
        this.clearSession();
        onExpired();
      }
    });
  }

  private startRenewal(): void {
    if (this.renewalTimer !== undefined) return;
    this.renewalTimer = setInterval(() => {
      if (!this.activeSinceRenewal) return;
      this.activeSinceRenewal = false;
      // Un fallo aquí no cierra nada: la siguiente petición rechazada hace la comprobación.
      const user = this.userSubject.value;
      if (user && !user.archivedSupport) this.refreshSession().subscribe({ error: () => undefined });
    }, AuthService.RenewalMs);
  }

  private clearSession(): void {
    if (this.renewalTimer !== undefined) clearInterval(this.renewalTimer);
    this.renewalTimer = undefined;
    this.userSubject.next(null);
  }

  /**
   * Renueva la sesión profesional usando exclusivamente la cookie HttpOnly.
   * El JWT nunca se devuelve al navegador en la respuesta.
   */
  refreshSession(): Observable<AuthUser> {
    return this.http.post<AuthUser>('/api/auth/refreshSession', {}).pipe(
      tap(user => this.login(user))
    );
  }

  restoreSession(): Observable<boolean> {
    if (this.isLoggedIn()) return of(true);
    if (this.sessionRestore$) return this.sessionRestore$;
    this.sessionRestore$ = this.refreshSession().pipe(
      map(() => true),
      catchError(() => { this.userSubject.next(null); return of(false); }),
      tap(() => { this.sessionRestore$ = undefined; })
    );
    return this.sessionRestore$;
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/auth/logout', {}).pipe(
      tap(() => this.clearSession()),
      catchError(() => { this.clearSession(); return of(void 0); })
    );
  }

  changePassword(oldPassword: string, newPassword: string, newPasswordRep: string): Observable<{ exito: boolean; mensaje: string }> {
    return this.http.put<{ exito: boolean; mensaje: string }>('/api/auth/cambiarPassword', {
      oldPassword,
      newPassword,
      newPasswordRep
    });
  }

  getToken(): null { return null; }
  getUser(): AuthUser | null { return this.userSubject.value; }
  getRole(): string | null { return this.userSubject.value?.role ?? null; }
  isSuperAdmin(): boolean { return this.getRole() === 'superadmin'; }
  getSubscriptionPlan(): string { return this.userSubject.value?.subscriptionPlan ?? 'free'; }
  getSubscriptionStatus(): string { return this.userSubject.value?.subscriptionStatus ?? 'active'; }
}
