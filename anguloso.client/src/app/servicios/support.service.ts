import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export type SupportCategory = 'problem' | 'billing' | 'account' | 'question' | 'suggestion' | 'other';
export type SupportPriority = 'low' | 'normal' | 'high' | 'urgent';
export type SupportStatus = 'open' | 'in_progress' | 'waiting_user' | 'resolved' | 'closed';

export interface SupportTicketSummary {
  id: number;
  tenantId: number;
  subject: string;
  category: SupportCategory;
  priority: SupportPriority;
  status: SupportStatus;
  createdByUserId: number;
  createdByUsername: string;
  assignedToUserId?: number | null;
  assignedToUsername?: string | null;
  createdAt: string;
  updatedAt: string;
  closedAt?: string | null;
  messageCount: number;
}

export interface SupportMessage {
  id: number;
  authorUserId: number;
  authorUsername: string;
  body: string;
  createdAt: string;
  internal: boolean;
}

export interface SupportTicket extends SupportTicketSummary {
  messages: SupportMessage[];
}

@Injectable({ providedIn: 'root' })
export class SupportService {
  private readonly base = '/api/support';

  constructor(private readonly http: HttpClient) {}

  getTickets(filters: { status?: string; category?: string; priority?: string; search?: string; assignedToUserId?: number; from?: string; to?: string; tenantId?: number } = {}): Observable<SupportTicketSummary[]> {
    let params = new HttpParams();
    if (filters.status) params = params.set('status', filters.status);
    if (filters.category) params = params.set('category', filters.category);
    if (filters.priority) params = params.set('priority', filters.priority);
    if (filters.search) params = params.set('search', filters.search);
    if (filters.assignedToUserId) params = params.set('assignedToUserId', filters.assignedToUserId);
    if (filters.from) params = params.set('from', filters.from);
    if (filters.to) params = params.set('to', filters.to);
    if (filters.tenantId) params = params.set('tenantId', filters.tenantId);
    return this.http.get<SupportTicketSummary[]>(this.base + '/tickets', { params });
  }

  getTicket(id: number): Observable<SupportTicket> {
    return this.http.get<SupportTicket>(this.base + '/tickets/' + id);
  }

  createTicket(request: { subject: string; category: SupportCategory; priority: SupportPriority; body: string }): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(this.base + '/tickets', request);
  }

  addMessage(id: number, body: string, internal = false): Observable<void> {
    return this.http.post<void>(this.base + '/tickets/' + id + '/messages', { body, internal });
  }

  updateTicket(id: number, request: { status?: SupportStatus; priority?: SupportPriority; assignedToUserId?: number | null }): Observable<void> { return this.http.patch<void>(this.base + '/tickets/' + id, request); }
  reopenTicket(id:number):Observable<void>{return this.http.post<void>(this.base+'/tickets/'+id+'/reopen',{});}
  getAssignees():Observable<{id:number;name:string}[]>{return this.http.get<{id:number;name:string}[]>(this.base+'/assignees');}
  getNotifications():Observable<any[]>{return this.http.get<any[]>(this.base+'/notifications');}
  getUnreadNotificationCount():Observable<number>{return this.http.get<number>(this.base+'/notifications/unread-count');}
  markNotificationRead(id:number):Observable<void>{return this.http.post<void>(this.base+'/notifications/'+id+'/read',{});}
  getAudit(id:number):Observable<any[]>{return this.http.get<any[]>(this.base+'/tickets/'+id+'/audit');}
}
