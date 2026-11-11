import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { AuthUser } from './auth.service';

export interface TwoFactorStatus {
  enabled: boolean;
  enabledAt?: string | null;
  recoveryCodesRemaining: number;
  /** false en cuentas creadas con Google, que no tienen contraseña local. */
  requiresPassword?: boolean;
}

export interface TwoFactorSetup {
  secret: string;
  otpauthUri: string;
  qrSvg: string;
}

/** Respuesta de login cuando la cuenta exige un segundo factor: no hay sesión hasta completar `verifyLogin`. */
export interface TwoFactorChallenge {
  requiresTwoFactor: true;
  challengeToken: string;
}

export function isTwoFactorChallenge(response: unknown): response is TwoFactorChallenge {
  return !!response && (response as TwoFactorChallenge).requiresTwoFactor === true && typeof (response as TwoFactorChallenge).challengeToken === 'string';
}

@Injectable({ providedIn: 'root' })
// Cliente de la verificación en dos pasos (TOTP). La sesión sigue en cookie HttpOnly; aquí nunca se maneja ningún token de sesión.
export class TwoFactorService {
  private readonly baseUrl = '/api/auth';

  constructor(private http: HttpClient) { }

  verifyLogin(challengeToken: string, code: string): Observable<AuthUser> {
    return this.http.post<AuthUser>(`${this.baseUrl}/login/2fa`, { challengeToken, code });
  }

  status(): Observable<TwoFactorStatus> {
    return this.http.get<TwoFactorStatus>(`${this.baseUrl}/2fa/status`);
  }

  setup(): Observable<TwoFactorSetup> {
    return this.http.post<TwoFactorSetup>(`${this.baseUrl}/2fa/setup`, {});
  }

  enable(code: string): Observable<{ recoveryCodes: string[] }> {
    return this.http.post<{ recoveryCodes: string[] }>(`${this.baseUrl}/2fa/enable`, { code });
  }

  disable(password: string, code: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/2fa/disable`, { password, code });
  }

  regenerateRecoveryCodes(code: string): Observable<{ recoveryCodes: string[] }> {
    return this.http.post<{ recoveryCodes: string[] }>(`${this.baseUrl}/2fa/recovery-codes`, { code });
  }
}
