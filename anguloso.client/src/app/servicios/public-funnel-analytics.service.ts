import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environments';

@Injectable({ providedIn: 'root' })
export class PublicFunnelAnalyticsService {
  private readonly endpoint = environment.apiUrl + '/public-funnel/events';

  constructor(private readonly http: HttpClient) {}

  track(eventName: 'directory_view' | 'profile_view' | 'booking_started', professionalSlug?: string): void {
    const key = 'dietexpress-funnel:' + eventName + ':' + (professionalSlug || 'directory');
    try {
      if (sessionStorage.getItem(key) === '1') return;
      sessionStorage.setItem(key, '1');
    } catch {
      // Si el almacenamiento de sesión no está disponible, el evento sigue siendo útil.
    }

    this.http.post(this.endpoint, { eventName, professionalSlug }).subscribe({
      error: () => { /* La analítica nunca debe afectar al flujo público. */ }
    });
  }
}
