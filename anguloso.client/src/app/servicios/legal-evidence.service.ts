import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface LegalAcceptanceEvidence { key:string; version:number; sha256:string; acceptedAt:string; context:string; }
export interface PrivacyRequestEvidence { id:number; requesterType:string; clientId?:number; rightType:string; status:string; receivedAt:string; dueAt?:string; resolvedAt?:string; }
export interface PrivacyIncidentEvidence { id:number; status:string; detectedAt:string; description:string; closedAt?:string; }

@Injectable({providedIn:'root'})
export class LegalEvidenceService {
 constructor(private http:HttpClient){}
 acceptances():Observable<LegalAcceptanceEvidence[]>{return this.http.get<LegalAcceptanceEvidence[]>('/api/legal-documents/acceptances');}
 requests():Observable<PrivacyRequestEvidence[]>{return this.http.get<PrivacyRequestEvidence[]>('/api/privacy/requests');}
 incidents():Observable<PrivacyIncidentEvidence[]>{return this.http.get<PrivacyIncidentEvidence[]>('/api/privacy/incidents');}
}