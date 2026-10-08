import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../environments/environments';

@Injectable({ providedIn: 'root' })
export class PublicFunnelAnalyticsService {
  private readonly endpoint = environment.apiUrl + '/public-funnel/events';

  constructor(private readonly http: HttpClient) {}

  track(eventName: 'directory_view' | 'profile_view' | 'profile_selected' | 'booking_started', professionalSlug?: string): void {
    // La analítica pública es server-side. No usamos cookies ni storage del navegador
    // para deduplicar eventos, evitando convertir una optimización de analítica en
    // una tecnología de almacenamiento del dispositivo.
    this.http.post(this.endpoint, { eventName, professionalSlug }).subscribe({
      error: () => { /* La analítica nunca debe afectar al flujo público. */ }
    });
  }
}
