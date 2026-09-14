import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environments';

export interface PatientAuthRequest {
  token?: string;
  emailOrPhone?: string;
  passcode?: string;
}

export interface PatientAuthResponse {
  token: string;
  clientId: number;
  fullName: string;
  clinicName?: string;
  clinicLogo?: string;
}

export interface ClientPortalAccess {
  clientId: number;
  accessToken: string;
  magicLink: string;
  hasPasscode: boolean;
  lastPortalAccess?: string;
}

@Injectable({
  providedIn: 'root'
})
export class PatientPortalService {
  private base = environment.apiUrl;
  private readonly PATIENT_TOKEN_KEY = 'patient_auth_token';

  constructor(private http: HttpClient) {}

  // ─── Métodos para el Paciente ───

  authenticate(req: PatientAuthRequest): Observable<PatientAuthResponse> {
    return this.http.post<PatientAuthResponse>(`${this.base}/portal/auth`, req);
  }

  savePatientToken(token: string): void {
    localStorage.setItem(this.PATIENT_TOKEN_KEY, token);
  }

  getPatientToken(): string | null {
    return localStorage.getItem(this.PATIENT_TOKEN_KEY);
  }

  clearPatientToken(): void {
    localStorage.removeItem(this.PATIENT_TOKEN_KEY);
  }

  getMyProfile(clientIdParam?: number): Observable<any> {
    const params: any = {};
    if (clientIdParam) params.clientId = clientIdParam;
    return this.http.get<any>(`${this.base}/portal/profile`, { params });
  }

  getMyActiveDiet(clientIdParam?: number): Observable<any> {
    const params: any = {};
    if (clientIdParam) params.clientId = clientIdParam;
    return this.http.get<any>(`${this.base}/portal/diet`, { params });
  }

  getMyShoppingList(clientIdParam?: number): Observable<any[]> {
    const params: any = {};
    if (clientIdParam) params.clientId = clientIdParam;
    return this.http.get<any[]>(`${this.base}/portal/shopping-list`, { params });
  }

  // ─── Métodos para el Nutricionista ───

  getClientPortalAccess(clientId: number): Observable<ClientPortalAccess> {
    return this.http.get<ClientPortalAccess>(`${this.base}/clients/${clientId}/portal-access`);
  }

  regenerateClientToken(clientId: number): Observable<ClientPortalAccess> {
    return this.http.post<ClientPortalAccess>(`${this.base}/clients/${clientId}/portal-access/regenerate-token`, {});
  }

  setClientPasscode(clientId: number, passcode: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.base}/clients/${clientId}/portal-access/passcode`, { passcode });
  }
}
