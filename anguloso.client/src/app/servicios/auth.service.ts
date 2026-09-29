import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { jwtDecode } from 'jwt-decode';
import { Observable } from 'rxjs';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly TOKEN_KEY = 'auth_token';

  constructor(private http: HttpClient) { }

  isLoggedIn(): boolean {
    return !!localStorage.getItem(this.TOKEN_KEY);
  }

  login(token: string): void {
    localStorage.setItem(this.TOKEN_KEY, token);
  }

  refreshSession(): Observable<{ token: string; username: string; email: string; role: string }> {
    return this.http.post<{ token: string; username: string; email: string; role: string }>('/api/auth/refreshSession', {});
  }

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
  }

  getToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  getUser(): any | null {
    const token = this.getToken();
    if (!token) return null;
    try {
      return jwtDecode(token);
    } catch {
      return null;
    }
  }

  getRole(): string | null {
    const user = this.getUser();
    if (!user) return null;
    return user['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] || user.role || null;
  }

  isSuperAdmin(): boolean {
    return this.getRole() === 'superadmin';
  }

  getSubscriptionPlan(): string {
    const user = this.getUser();
    return user?.subscriptionPlan ?? 'free';
  }

  getSubscriptionStatus(): string {
    const user = this.getUser();
    return user?.subscriptionStatus ?? 'active';
  }
}
