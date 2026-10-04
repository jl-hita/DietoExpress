import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, BehaviorSubject } from 'rxjs';
import { tap } from 'rxjs/operators';
import { environment } from '../../environments/environments';
import { Profile, UpdateProfile } from '../modelos/profile';

@Injectable({ providedIn: 'root' })
// Mantiene el perfil en memoria para que distintas vistas compartan el mismo estado sin repetir consultas al backend.
export class ProfileService {
  private base = environment.apiUrl;
  private profileSubject = new BehaviorSubject<Profile | null>(null);
  readonly profile$ = this.profileSubject.asObservable();

  constructor(private http: HttpClient) { }

  getProfile(): Observable<Profile> {
    return this.http.get<Profile>(`${this.base}/profile`).pipe(
      tap(profile => this.profileSubject.next(profile))
    );
  }

  updateProfile(dto: UpdateProfile): Observable<Profile> {
    return this.http.put<Profile>(`${this.base}/profile`, dto).pipe(
      tap(profile => this.profileSubject.next(profile))
    );
  }
}
