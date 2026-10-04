import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environments';

export interface LegalSetting {
  key: string;
  value: string;
}

@Injectable({ providedIn: 'root' })
export class LegalConfigurationService {
  private readonly base = environment.apiUrl;

  constructor(private http: HttpClient) {}

  getProfessional(): Observable<LegalSetting[]> {
    return this.http.get<LegalSetting[]>(`${this.base}/legal-configuration`);
  }

  saveProfessional(values: Record<string, string>): Observable<void> {
    return this.http.put<void>(`${this.base}/legal-configuration`, { values });
  }

  getPlatform(): Observable<LegalSetting[]> {
    return this.http.get<LegalSetting[]>(`${this.base}/admin/legal-configuration`);
  }

  savePlatform(values: Record<string, string>): Observable<void> {
    return this.http.put<void>(`${this.base}/admin/legal-configuration`, { values });
  }
}
