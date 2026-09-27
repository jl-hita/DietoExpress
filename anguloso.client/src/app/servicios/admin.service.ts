import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface SetupStatusResponse {
  isConfigured: boolean;
}

export interface SetupInitRequest {
  username: string;
  fullName: string;
  email: string;
  password: string;
}

export interface AdminStats {
  totalUsers: number;
  totalClients: number;
  totalDiets: number;
  licensesExpiringSoon: number;
  activeSubscriptions: number;
}

export interface AdminUser {
  id: number;
  username: string;
  fullName?: string;
  email?: string;
  emailConfirmed: boolean;
  role?: string;
  clinicName?: string;
  createdAt?: string;
  lastLogin?: string;
  subscriptionPlan: string;
  subscriptionStatus: string;
  licenseExpiresAt?: string;
  maxClientsAllowed: number;
  clientCount: number;
}

export interface UpdateLicenseDto {
  subscriptionPlan: string;
  subscriptionStatus: string;
  licenseExpiresAt?: string | null;
  maxClientsAllowed: number;
}

export interface AdminConfig {
  id: number;
  nombre: string;
  valor: string;
}

@Injectable({
  providedIn: 'root'
})
export class AdminService {
  private readonly setupUrl = '/api/setup';
  private readonly adminUrl = '/api/admin';

  constructor(private http: HttpClient) {}

  getSetupStatus(): Observable<SetupStatusResponse> {
    return this.http.get<SetupStatusResponse>(`${this.setupUrl}/status`);
  }

  initSuperAdmin(data: SetupInitRequest): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.setupUrl}/init`, data);
  }

  getStats(): Observable<AdminStats> {
    return this.http.get<AdminStats>(`${this.adminUrl}/stats`);
  }

  getConfig(): Observable<AdminConfig[]> {
    return this.http.get<AdminConfig[]>(`${this.adminUrl}/config`);
  }

  updateConfig(id: number, valor: string): Observable<AdminConfig> {
    return this.http.put<AdminConfig>(`${this.adminUrl}/config/${id}`, { valor });
  }

  getUsers(search?: string, status?: string, plan?: string): Observable<AdminUser[]> {
    let params: any = {};
    if (search) params.search = search;
    if (status) params.status = status;
    if (plan) params.plan = plan;
    return this.http.get<AdminUser[]>(`${this.adminUrl}/users`, { params });
  }

  updateLicense(id: number, data: UpdateLicenseDto): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.adminUrl}/users/${id}/license`, data);
  }

  suspendUser(id: number): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.adminUrl}/users/${id}/suspend`, {});
  }

  activateUser(id: number): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.adminUrl}/users/${id}/activate`, {});
  }

  resetUserPassword(id: number, newPassword: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.adminUrl}/users/${id}/reset-password`, { newPassword });
  }
}
