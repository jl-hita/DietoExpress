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
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly userSubject = new BehaviorSubject<AuthUser | null>(null);
  private sessionRestore$?: Observable<boolean>;

  constructor(private http: HttpClient) { }

  isLoggedIn(): boolean { return this.userSubject.value !== null; }

  login(user: AuthUser): void { this.userSubject.next(user); }

  /**
   * Renueva la sesión profesional usando exclusivamente la cookie HttpOnly.
   * El JWT nunca se devuelve al navegador en la respuesta.
   */
  refreshSession(): Observable<AuthUser> {
    return this.http.post<AuthUser>('/api/auth/refreshSession', {}).pipe(
      tap(user => this.userSubject.next(user))
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
      tap(() => this.userSubject.next(null)),
      catchError(() => { this.userSubject.next(null); return of(void 0); })
    );
  }

  getToken(): null { return null; }
  getUser(): AuthUser | null { return this.userSubject.value; }
  getRole(): string | null { return this.userSubject.value?.role ?? null; }
  isSuperAdmin(): boolean { return this.getRole() === 'superadmin'; }
  getSubscriptionPlan(): string { return this.userSubject.value?.subscriptionPlan ?? 'free'; }
  getSubscriptionStatus(): string { return this.userSubject.value?.subscriptionStatus ?? 'active'; }
}
