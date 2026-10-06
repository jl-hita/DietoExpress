import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environments';
import { DirectoryProfile, PublicAvailabilitySlot } from './directory.models';

@Injectable({ providedIn: 'root' })
export class DirectoryService {
  private readonly apiUrl = environment.apiUrl;

  constructor(private readonly http: HttpClient) {}

  search(city?: string, province?: string, speciality?: string, online?: boolean): Observable<DirectoryProfile[]> {
    let params = new HttpParams();

    if (city?.trim()) params = params.set('city', city.trim());
    if (province?.trim()) params = params.set('province', province.trim());
    if (speciality?.trim()) params = params.set('speciality', speciality.trim());
    if (online) params = params.set('online', 'true');

    return this.http.get<DirectoryProfile[]>(`${this.apiUrl}/api/directory/professionals`, { params });
  }

  getAvailability(slug: string, days = 30): Observable<PublicAvailabilitySlot[]> {
    const params = new HttpParams().set('days', String(days));
    return this.http.get<PublicAvailabilitySlot[]>(
      `${this.apiUrl}/api/directory/professionals/${encodeURIComponent(slug)}/availability`,
      { params }
    );
  }

  requestAppointment(
    slug: string,
    request: {
      startsAt: string;
      durationMinutes: number;
      fullName: string;
      email: string;
      phone?: string;
      patientNotes?: string;
    }
  ): Observable<{ startsAt: string; endsAt: string; status: string; nutritionistName: string }> {
    return this.http.post<{ startsAt: string; endsAt: string; status: string; nutritionistName: string }>(
      this.apiUrl + '/api/directory/professionals/' + encodeURIComponent(slug) + '/appointments',
      request
    );
  }

  getBySlug(slug: string): Observable<DirectoryProfile> {
    return this.http.get<DirectoryProfile>(
      `${this.apiUrl}/api/directory/professionals/${encodeURIComponent(slug)}`
    );
  }
}
