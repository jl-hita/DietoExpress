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

  getTickets(filters: { status?: string; category?: string; priority?: string } = {}): Observable<SupportTicketSummary[]> {
    let params = new HttpParams();
    if (filters.status) params = params.set('status', filters.status);
    if (filters.category) params = params.set('category', filters.category);
    if (filters.priority) params = params.set('priority', filters.priority);
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
  getNotifications():Observable<any[]>{return this.http.get<any[]>(this.base+'/notifications');}
  getUnreadNotificationCount():Observable<number>{return this.http.get<number>(this.base+'/notifications/unread-count');}
  markNotificationRead(id:number):Observable<void>{return this.http.post<void>(this.base+'/notifications/'+id+'/read',{});}
}
