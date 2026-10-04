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

export interface AdminAlert {
  id: number;
  severity: 'critical' | 'error' | 'warning' | 'info';
  component: string;
  title: string;
  message: string;
  firstSeenAt: string;
  lastSeenAt: string;
  occurrences: number;
}

export interface AdminStats {
  totalUsers: number;
  totalClients: number;
  totalDiets: number;
  licensesExpiringSoon: number;
  activeSubscriptions: number;
}

export interface AdminUsersPage {
  items: AdminUser[];
  totalCount: number;
  page: number;
  pageSize: number;
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
  archivedAt?: string;
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
  esSecreta: boolean;
  tieneValor: boolean;
}

export interface AdminLog {
  date: string;
  exists: boolean;
  content: string;
  previousDate?: string;
  nextDate?: string;
}

@Injectable({
  providedIn: 'root'
})
  // Agrupa las operaciones administrativas; la autorización efectiva permanece en backend y no se confía en el estado del cliente.
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

  getAlerts(): Observable<AdminAlert[]> {
    return this.http.get<AdminAlert[]>(`${this.adminUrl}/alerts`);
  }

  resolveAlert(id: number): Observable<void> {
    return this.http.post<void>(`${this.adminUrl}/alerts/${id}/resolve`, {});
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

  getLog(date?: string): Observable<AdminLog> {
    let params: any = {};
    if (date) params.date = date;
    return this.http.get<AdminLog>(`${this.adminUrl}/logs`, { params });
  }

  getUsers(search?: string, status?: string, plan?: string, page = 1, pageSize = 25): Observable<AdminUsersPage> {
    let params: any = { page, pageSize };
    if (search) params.search = search;
    if (status) params.status = status;
    if (plan) params.plan = plan;
    return this.http.get<AdminUsersPage>(`${this.adminUrl}/users`, { params });
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

  getDeactivationPreview(id:number): Observable<any> {
    return this.http.get<any>(`${this.adminUrl}/users/${id}/deactivation-preview`);
  }

  deleteUser(id: number, data:any): Observable<{ message: string }> {
    return this.http.delete<{ message: string }>(`${this.adminUrl}/users/${id}`, { body: data });
  }

  resetUserPassword(id: number, newPassword: string): Observable<{ message: string }> {
    return this.http.put<{ message: string }>(`${this.adminUrl}/users/${id}/reset-password`, { newPassword });
  }

  getPlans(): Observable<AdminPlan[]> { return this.http.get<AdminPlan[]>(`${this.adminUrl}/plans`); }

  createAccount(data: CreateAdminAccountDto): Observable<any> {
    return this.http.post(`${this.adminUrl}/users/create-account`, data);
  }
  createPlan(plan: any): Observable<AdminPlan> { return this.http.post<AdminPlan>(`${this.adminUrl}/plans`, plan); }
  updatePlan(id: number, plan: any): Observable<AdminPlan> { return this.http.put<AdminPlan>(`${this.adminUrl}/plans/${id}`, plan); }
  updatePlanFeatures(id: number, features: AdminPlanFeature[]): Observable<any> { return this.http.put(`${this.adminUrl}/plans/${id}/features`, features); }
}


export interface CreateAdminAccountDto {
  accountType: 'nutritionist' | 'clinic';
  username: string;
  fullName: string;
  email: string;
  password: string;
  clinicName?: string;
  legalName?: string;
  cifNif?: string;
  clinicAddress?: string;
  clinicPhone?: string;
  subscriptionPlan: string;
  subscriptionStatus: string;
  licenseExpiresAt?: string | null;
  maxClientsAllowed?: number | null;
}

export interface AdminPlanFeature { id?:number; feature_code:string; enabled:boolean; }
export interface AdminPlan { id:number; code:string; name:string; description?:string; monthly_price:number; yearly_price:number; max_nutritionists?:number; max_clients_per_nutritionist?:number; max_total_clients?:number; trial_days?:number; active:boolean; features:AdminPlanFeature[]; }
