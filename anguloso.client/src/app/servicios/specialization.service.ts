import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environments';

export interface Specialization {
  id: number;
  code: string;
  name: string;
  category: string;
  description?: string | null;
  enabled: boolean;
}

export interface ClientSpecialization {
  specializationId: number;
  code: string;
  name: string;
  category: string;
  description?: string | null;
  notes?: string | null;
}

export interface SportsNutritionProfile {
  discipline: string;
  trainingGoal: string;
  sessionsPerWeek: number;
  sessionMinutes: number;
  proteinGPerKg: number;
  carbsGPerKg: number;
  hydrationMlPerKg: number;
}

export interface WeightManagementProfile {
  goal: string;
  targetWeightKg: number | null;
  targetRateKgPerWeek: number;
  deficitPercent: number;
  minimumKcal: number;
  proteinGPerKg: number;
  reviewWeeks: number;
}

export interface ClientSpecializationItem {
  specializationId: number;
  notes?: string | null;
}

@Injectable({ providedIn: 'root' })
export class SpecializationService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/specializations`;

  getCatalog(): Observable<Specialization[]> {
    return this.http.get<Specialization[]>(this.baseUrl);
  }

  setTenantEnabled(specializationId: number, enabled: boolean): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${specializationId}/tenant`, { enabled });
  }

  getClientSpecializations(clientId: number): Observable<ClientSpecialization[]> {
    return this.http.get<ClientSpecialization[]>(`${this.baseUrl}/clients/${clientId}`);
  }

  setClientSpecializations(clientId: number, items: ClientSpecializationItem[]): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/clients/${clientId}`, { items });
  }

  getClientSpecializationProfile<T>(clientId: number, code: string): Observable<{ code: string; configuration: T }> {
    return this.http.get<{ code: string; configuration: T }>(this.baseUrl + '/clients/' + clientId + '/' + code + '/profile');
  }

  setClientSpecializationProfile<T>(clientId: number, code: string, configuration: T): Observable<void> {
    return this.http.put<void>(this.baseUrl + '/clients/' + clientId + '/' + code + '/profile', { configuration });
  }
}
