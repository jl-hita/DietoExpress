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

export interface LegalReadinessDocument { key:string; title:string; published:boolean; }
export interface LegalReadiness { ready:boolean; documents:LegalReadinessDocument[]; missingConfiguration:string[]; note:string; }

export interface LegalAcceptance {
  key: string;
  version: number;
  sha256: string;
  acceptedAt: string;
  context: string;
  action: "acceptance" | "acknowledgement";
}

@Injectable({ providedIn: 'root' })
export class LegalService {
  private readonly baseUrl = '/api/legal-documents';

  constructor(private http: HttpClient) {}

  getCurrent(): Observable<LegalDocument[]> {
    return this.http.get<LegalDocument[]>(`${this.baseUrl}/current`);
  }

  record(documentKey: string, version?: number, context = 'user_action', action: 'accept' | 'acknowledge' = 'accept'): Observable<LegalAcceptance> {
    return this.http.post<LegalAcceptance>(`${this.baseUrl}/accept`, {
      documentKey,
      version,
      context,
      action
    });
  }

  accept(documentKey: string, version?: number, context = 'user_action'): Observable<LegalAcceptance> {
    return this.record(documentKey, version, context, 'accept');
  }

  acknowledge(documentKey: string, version?: number, context = 'information'): Observable<LegalAcceptance> {
    return this.record(documentKey, version, context, 'acknowledge');
  }

  getAcceptances(): Observable<LegalAcceptance[]> {
    return this.http.get<LegalAcceptance[]>(`${this.baseUrl}/acceptances`);
  }

  getReadiness(): Observable<LegalReadiness> {
    return this.http.get<LegalReadiness>(`${this.baseUrl}/readiness`);
  }
}
