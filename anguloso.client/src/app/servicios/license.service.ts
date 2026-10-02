import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface LicenseStatus {
  tenantId: number;
  planCode: string;
  planName: string;
  status: string;
  expiresAt?: string | null;
  currentPeriodStart?: string | null;
  currentPeriodEnd?: string | null;
  billingInterval?: 'monthly' | 'yearly' | null;
  cancelAtPeriodEnd: boolean;
  nutritionists: number;
  clients: number;
  maxNutritionists?: number | null;
  maxClientsPerNutritionist?: number | null;
  maxTotalClients?: number | null;
  features: string[];
}

@Injectable({
  providedIn: 'root'
})
// La licencia se consulta mediante el backend para que el frontend solo refleje el estado autorizado y no sea la fuente de verdad.
export class LicenseService {
  constructor(private http: HttpClient) {}

  getLicense(): Observable<LicenseStatus> {
    return this.http.get<LicenseStatus>('/api/license');
  }
}
