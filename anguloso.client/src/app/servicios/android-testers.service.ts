import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environments';

export interface AndroidTesterStatus {
  googleEmail: string;
  status: 'pending' | 'invited';
  requestedAt: string;
  invitedAt: string | null;
}

export interface AndroidTesterAdminRow extends AndroidTesterStatus {
  id: number;
  clientName: string | null;
  requestedBy: string | null;
}

@Injectable({ providedIn: 'root' })
export class AndroidTestersService {
  private readonly http = inject(HttpClient);
  private readonly base = environment.apiUrl;

  getForClient(clientId: number): Observable<{ request: AndroidTesterStatus | null }> {
    return this.http.get<{ request: AndroidTesterStatus | null }>(`${this.base}/clients/${clientId}/android-tester`);
  }

  requestForClient(clientId: number, googleEmail: string, consentConfirmed: boolean): Observable<{ request: AndroidTesterStatus }> {
    return this.http.post<{ request: AndroidTesterStatus }>(`${this.base}/clients/${clientId}/android-tester`, { googleEmail, consentConfirmed });
  }

  list(): Observable<{ optInUrl: string; requests: AndroidTesterAdminRow[] }> {
    return this.http.get<{ optInUrl: string; requests: AndroidTesterAdminRow[] }>(`${this.base}/admin/android-testers`);
  }

  invite(ids: number[]): Observable<{ sent: number; failed: number }> {
    return this.http.post<{ sent: number; failed: number }>(`${this.base}/admin/android-testers/invite`, { ids });
  }
}
