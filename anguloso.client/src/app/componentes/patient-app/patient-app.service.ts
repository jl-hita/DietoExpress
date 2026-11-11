import { HttpClient } from '@angular/common/http';
import { Injectable, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import type { PatientAppointment, PatientProfile, TrainingEnvelope } from './patient-app.models';

@Injectable({ providedIn: 'root' })
// Acceso a la API del portal del paciente para la nueva interfaz (paridad con la app Android). La sesión va en la cookie HttpOnly;
// aquí nunca se maneja ningún token.
export class PatientAppService {
  private readonly profileState = signal<PatientProfile | null>(null);
  /** Perfil de la sesión actual, o null mientras no se haya cargado. */
  readonly profile = this.profileState.asReadonly();

  constructor(private http: HttpClient) { }

  loadProfile(): Observable<PatientProfile> {
    return this.http.get<PatientProfile>('/api/portal/profile').pipe(tap(profile => this.profileState.set(profile)));
  }

  appointments(): Observable<PatientAppointment[]> {
    return this.http.get<PatientAppointment[]>('/api/appointments/mine');
  }

  trainingSessions(): Observable<TrainingEnvelope> {
    return this.http.get<TrainingEnvelope>('/api/portal/training/sessions');
  }

  logout(): Observable<void> {
    return this.http.post<void>('/api/portal/logout', {}).pipe(tap(() => this.profileState.set(null)));
  }
}
