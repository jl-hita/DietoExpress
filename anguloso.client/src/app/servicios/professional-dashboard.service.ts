import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface DashboardAppointment { id:number; clientId:number; clientName:string; startsAt:string; endsAt:string; status:string; }
export interface DashboardPendingClient { clientId:number; clientName:string; pendingCount:number; }
export interface DashboardPendingDataClient { clientId:number; clientName:string; missingFields:string; }
export interface DashboardDocumentProvisioning { clientId:number; clientName:string; status:string; attempts:number; maxAttempts:number; lastError:string|null; updatedAt:string; }
export interface DashboardTask { id:number; clientId:number|null; clientName:string; title:string; dueAt:string|null; priority:string; status:string; }
export interface ProfessionalDashboard {
  openTaskCount:number;
  overdueTaskCount:number;
  unreadMessageCount:number;
  pendingDocumentCount:number;
  pendingPatientDataCount:number;
  documentProvisioningRetryCount:number;
  documentProvisioningFailedCount:number;
  documentProvisioningIncompleteCount:number;
  documentProvisioningIssues:DashboardDocumentProvisioning[];
  openTasks:DashboardTask[];
  todayAppointments:DashboardAppointment[];
  pendingDocuments:DashboardPendingClient[];
  pendingPatientData:DashboardPendingDataClient[];
}

@Injectable({providedIn:'root'})
export class ProfessionalDashboardService {
  constructor(private http:HttpClient){}
  get():Observable<ProfessionalDashboard>{ return this.http.get<ProfessionalDashboard>('/api/professional/dashboard'); }
}
