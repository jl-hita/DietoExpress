import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environments';

export interface PatientAuthRequest { token?: string; emailOrPhone?: string; passcode?: string; }
export interface PatientAuthResponse { token?: string; clientId: number; fullName: string; clinicName?: string; clinicLogo?: string; }
export interface ClientPortalAccess { clientId: number; accessToken: string; magicLink: string; hasPasscode: boolean; lastPortalAccess?: string; }

export interface AppointmentSlot {
  startsAt: string;
  endsAt: string;
  nutritionistId: number;
  nutritionistName?: string | null;
}

export interface AvailabilityRule {
  id: number;
  dayOfWeek: number;
  startTime: string;
  endTime: string;
  slotMinutes: number;
  isActive: boolean;
}

export interface AvailabilityRequest {
  dayOfWeek: number;
  startTime: string;
  endTime: string;
  slotMinutes: number;
  isActive: boolean;
}

export interface PatientAppointment {
  id: number;
  startsAt: string;
  endsAt: string;
  status: string;
  patientNotes?: string | null;
  professionalNotes?: string | null;
  clientId: number;
  clientName?: string | null;
  nutritionistId: number;
  nutritionistName?: string | null;
}

export interface PatientCheckin { id: number; week_start: string; submitted_at: string; weight?: number | null; adherence?: number | null; hunger?: number | null; energy?: number | null; sleep_quality?: number | null; sleep_hours?: number | null; training?: number | null; difficulties?: string | null; notes?: string | null; reviewed_at?: string | null; reviewed_by_user_id?: number | null; }
export interface PatientCheckinRequest { weight?: number | null; adherence?: number | null; hunger?: number | null; energy?: number | null; sleep_quality?: number | null; sleep_hours?: number | null; training?: number | null; difficulties?: string | null; notes?: string | null; }
export interface GoogleCalendarStatus { connected: boolean; email: string; calendarId: string; lastSyncedAt?: string | null; }

export interface PatientNotification { id: number; type: string; title: string; message: string; actionUrl?: string | null; createdAt: string; readAt?: string | null; }
export interface PatientCommunicationPreferences { inAppEnabled: boolean; emailEnabled: boolean; pushEnabled: boolean; }

export interface ProfessionalDocumentSummary {
  total: number;
  required: number;
  accepted: number;
  pending: number;
  active: number;
  allRequiredComplete: boolean;
}

export interface PatientDocument {
  id: number;
  name: string;
  documentType: string;
  status: string;
  version: number;
  requiresSignature: boolean;
  signedAt?: string | null;
  viewedAt?: string | null;
  originalFileName?: string | null;
  mimeType: string;
  fileSize: number;
  sha256: string;
  createdAt: string;
  updatedAt: string;
}


export interface ProfessionalTask {
  id: number;
  tenantId: number;
  clientId?: number | null;
  assignedUserId?: number | null;
  title: string;
  description?: string | null;
  dueAt?: string | null;
  priority: string;
  status: string;
  source: string;
  createdAt: string;
  completedAt?: string | null;
}

export interface FollowupSettings {
  selectedMetrics: string[];
  periodWeeks: number;
  thresholds: Record<string, { low?: number; high?: number; drop?: number; rise?: number }>;
  updatedAt?: string | null;
}

@Injectable({ providedIn: 'root' })
  // Centraliza autenticación y operaciones del portal para que los componentes no dupliquen rutas ni gestionen directamente el transporte HTTP.
export class PatientPortalService {
  private base = environment.apiUrl;
  constructor(private http: HttpClient) {}
  authenticate(req: PatientAuthRequest): Observable<PatientAuthResponse> { return this.http.post<PatientAuthResponse>(`${this.base}/portal/auth`, req); }
  clearPatientSession(): Observable<void> { return this.http.post<void>(`${this.base}/portal/logout`, {}); }
  getMyProfile(clientIdParam?: number): Observable<any> { const params: any = {}; if (clientIdParam) params.clientId = clientIdParam; return this.http.get<any>(`${this.base}/portal/profile`, { params }); }
  getMyActiveDiet(clientIdParam?: number): Observable<any> { const params: any = {}; if (clientIdParam) params.clientId = clientIdParam; return this.http.get<any>(`${this.base}/portal/diet`, { params }); }
  getMyShoppingList(clientIdParam?: number): Observable<any[]> { const params: any = {}; if (clientIdParam) params.clientId = clientIdParam; return this.http.get<any[]>(`${this.base}/portal/shopping-list`, { params }); }
  requestAccessLink(email: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.base}/portal/request-access-link`, { email });
  }

  getAppointmentSlots(days = 30): Observable<AppointmentSlot[]> {
    return this.http.get<AppointmentSlot[]>(this.base + '/appointments/slots?days=' + days);
  }

  getMyAppointments(): Observable<PatientAppointment[]> {
    return this.http.get<PatientAppointment[]>(this.base + '/appointments/mine');
  }

  requestAppointment(startsAt: string, durationMinutes = 30, patientNotes?: string | null): Observable<PatientAppointment> {
    return this.http.post<PatientAppointment>(this.base + '/appointments', { startsAt, durationMinutes, patientNotes });
  }

  cancelAppointment(id: number): Observable<void> {
    return this.http.post<void>(this.base + '/appointments/' + id + '/cancel', {});
  }

  getProfessionalTasks(status = 'open', limit = 100): Observable<ProfessionalTask[]> {
    return this.http.get<ProfessionalTask[]>(this.base + '/professional/tasks', {
      params: { status, limit: String(limit) }
    });
  }

  getConsultationSlots(clientId: number, durationMinutes = 30, days = 30): Observable<AppointmentSlot[]> {
    return this.http.get<AppointmentSlot[]>(this.base + '/professional/consultation-actions/slots', {
      params: { clientId: String(clientId), durationMinutes: String(durationMinutes), days: String(days) }
    });
  }

  createConsultationAppointment(request: { clientId: number; startsAt: string; durationMinutes: number; notes?: string | null }): Observable<PatientAppointment> {
    return this.http.post<PatientAppointment>(this.base + '/professional/consultation-actions/appointment', request);
  }

  createProfessionalTask(request: {
    clientId?: number | null;
    title: string;
    description?: string | null;
    dueAt?: string | null;
    priority?: string;
  }): Observable<{ id: number }> {
    return this.http.post<{ id: number }>(this.base + '/professional/tasks', request);
  }

  getProfessionalAppointments(from?: string, to?: string): Observable<PatientAppointment[]> {
    let url = this.base + '/appointments/professional';
    const params: string[] = [];
    if (from) params.push('from=' + encodeURIComponent(from));
    if (to) params.push('to=' + encodeURIComponent(to));
    if (params.length) url += '?' + params.join('&');
    return this.http.get<PatientAppointment[]>(url);
  }

  getAvailability(): Observable<AvailabilityRule[]> {
    return this.http.get<AvailabilityRule[]>(this.base + '/appointments/availability');
  }

  saveAvailability(request: AvailabilityRequest): Observable<AvailabilityRule> {
    return this.http.post<AvailabilityRule>(this.base + '/appointments/availability', request);
  }

  updateAvailability(id: number, request: AvailabilityRequest): Observable<AvailabilityRule> {
    return this.http.put<AvailabilityRule>(this.base + '/appointments/availability/' + id, request);
  }

  deleteAvailability(id: number): Observable<void> {
    return this.http.delete<void>(this.base + '/appointments/availability/' + id);
  }

  updateAppointmentStatus(id: number, status: string, professionalNotes?: string | null): Observable<PatientAppointment> {
    return this.http.patch<PatientAppointment>(this.base + '/appointments/' + id + '/status', {
      status,
      professionalNotes
    });
  }

  getGoogleCalendarStatus(): Observable<GoogleCalendarStatus> { return this.http.get<GoogleCalendarStatus>(this.base + '/google-calendar/status'); }
  connectGoogleCalendar(): void { window.location.href = this.base + '/google-calendar/connect'; }
  disconnectGoogleCalendar(): Observable<void> { return this.http.post<void>(this.base + '/google-calendar/disconnect', {}); }
  syncGoogleCalendar(): Observable<void> { return this.http.post<void>(this.base + '/google-calendar/sync', {}); }

  getMyDocuments(): Observable<PatientDocument[]> {
    return this.http.get<PatientDocument[]>(this.base + '/portal/documents');
  }

  downloadMyDocument(id: number): Observable<Blob> {
    return this.http.get(this.base + '/portal/documents/' + id, { responseType: 'blob' });
  }

  acceptDocument(id: number): Observable<void> {
    return this.http.post<void>(this.base + '/portal/documents/' + id + '/accept', {});
  }

  getProfessionalDocuments(clientId: number): Observable<PatientDocument[]> {
    return this.http.get<PatientDocument[]>(`${this.base}/clients/${clientId}/documents`);
  }

  getProfessionalDocumentSummary(clientId: number): Observable<ProfessionalDocumentSummary> {
    return this.http.get<ProfessionalDocumentSummary>(`${this.base}/clients/${clientId}/documents/summary`);
  }

  downloadProfessionalDocument(clientId: number, documentId: number): Observable<Blob> {
    return this.http.get(`${this.base}/clients/${clientId}/documents/${documentId}`, { responseType: 'blob' });
  }

  getNotifications(): Observable<PatientNotification[]> { return this.http.get<PatientNotification[]>(this.base + '/portal/notifications'); }
  markNotificationRead(id: number): Observable<void> { return this.http.patch<void>(this.base + '/portal/notifications/' + id + '/read', {}); }
  getVapidPublicKey(): Observable<{ publicKey: string }> { return this.http.get<{ publicKey: string }>(this.base + '/portal/push/vapid-public-key'); }
  registerPushSubscription(subscription: { endpoint: string; p256dh: string; auth: string }): Observable<void> { return this.http.post<void>(this.base + '/portal/push-subscriptions', subscription); }

  getCurrentCheckin(): Observable<PatientCheckin | null> { return this.http.get<PatientCheckin | null>(`${this.base}/portal/check-ins/current`); }
  getCheckinHistory(): Observable<PatientCheckin[]> { return this.http.get<PatientCheckin[]>(`${this.base}/portal/check-ins`); }
  getCheckins(clientId: number): Observable<PatientCheckin[]> {
    return this.http.get<PatientCheckin[]>(`${this.base}/professional/check-ins`, {
      params: { pendingOnly: 'false', clientId: clientId.toString(), limit: '200' }
    });
  }
  reviewCheckin(id: number): Observable<void> { return this.http.post<void>(`${this.base}/professional/check-ins/${id}/review`, {}); }
  createFollowUpTask(id: number): Observable<{ id: number }> { return this.http.post<{ id: number }>(`${this.base}/professional/check-ins/${id}/follow-up-task`, {}); }
  saveCheckin(request: PatientCheckinRequest): Observable<PatientCheckin> { return this.http.post<PatientCheckin>(`${this.base}/portal/check-ins`, request); }
  getClientPortalAccess(clientId: number): Observable<ClientPortalAccess> { return this.http.get<ClientPortalAccess>(`${this.base}/clients/${clientId}/portal-access`); }
  regenerateClientToken(clientId: number): Observable<ClientPortalAccess> { return this.http.post<ClientPortalAccess>(`${this.base}/clients/${clientId}/portal-access/regenerate-token`, {}); }
  setClientPasscode(clientId: number, passcode: string): Observable<{ message: string }> { return this.http.post<{ message: string }>(`${this.base}/clients/${clientId}/portal-access/passcode`, { passcode }); }
  getGuidedConsultation(appointmentId: number): Observable<any> {
    return this.http.get<any>(`${this.base}/professional/consultations/appointment/${appointmentId}`);
  }

  startGuidedConsultation(appointmentId: number, consultationType?: string): Observable<any> {
    return this.http.post<any>(`${this.base}/professional/consultations/appointment/${appointmentId}/start`, {
      consultationType
    });
  }

  updateGuidedConsultationProgress(appointmentId: number, request: {
    currentStep: string;
    completedSteps: string[];
    progress: Record<string, unknown>;
  }): Observable<any> {
    return this.http.put<any>(`${this.base}/professional/consultations/appointment/${appointmentId}/progress`, request);
  }

  completeGuidedConsultation(appointmentId: number): Observable<any> {
    return this.http.post<any>(`${this.base}/professional/consultations/appointment/${appointmentId}/complete`, {});
  }

  getFollowupSettings(): Observable<FollowupSettings> { return this.http.get<FollowupSettings>(this.base + '/professional/automation/followup-settings'); }
  updateFollowupSettings(settings: FollowupSettings): Observable<void> { return this.http.put<void>(this.base + '/professional/automation/followup-settings', settings); }

  getCommunicationPreferences(clientId: number): Observable<PatientCommunicationPreferences> { return this.http.get<PatientCommunicationPreferences>(`${this.base}/professional/automation/clients/${clientId}/communication-preferences`); }
  updateCommunicationPreferences(clientId: number, preferences: PatientCommunicationPreferences): Observable<void> { return this.http.put<void>(`${this.base}/professional/automation/clients/${clientId}/communication-preferences`, preferences); }
}
