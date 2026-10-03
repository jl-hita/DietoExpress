import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface LegalDocument {
  key: string;
  version: number;
  title: string;
  documentType: string;
  content: string;
  effectiveFrom?: string | null;
  sha256: string;
  publishedAt?: string | null;
}

export interface LegalAcceptance {
  key: string;
  version: number;
  sha256: string;
  acceptedAt: string;
  context: string;
}

@Injectable({ providedIn: 'root' })
export class LegalService {
  private readonly baseUrl = '/api/legal-documents';

  constructor(private http: HttpClient) {}

  getCurrent(): Observable<LegalDocument[]> {
    return this.http.get<LegalDocument[]>(`${this.baseUrl}/current`);
  }

  accept(documentKey: string, version?: number, context = 'user_action'): Observable<LegalAcceptance> {
    return this.http.post<LegalAcceptance>(`${this.baseUrl}/accept`, {
      documentKey,
      version,
      context
    });
  }

  getAcceptances(): Observable<LegalAcceptance[]> {
    return this.http.get<LegalAcceptance[]>(`${this.baseUrl}/acceptances`);
  }
}
