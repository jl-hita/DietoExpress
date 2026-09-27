import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, BehaviorSubject } from 'rxjs';
import { tap } from 'rxjs/operators';
import { environment } from '../../environments/environments';
import { Profile, UpdateProfile } from '../modelos/profile';

@Injectable({ providedIn: 'root' })
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

  updateProfile(dto: UpdateProfile): Observable<void> {
    return this.http.put<void>(`${this.base}/profile`, dto).pipe(
      tap(() => {
        const current = this.profileSubject.value;
        if (current) {
          this.profileSubject.next({
            ...current,
            fullName: dto.fullName ?? current.fullName,
            clinicName: dto.clinicName,
            clinicAddress: dto.clinicAddress,
            clinicPhone: dto.clinicPhone,
            clinicLogo: dto.clinicLogo
          });
        }
      })
    );
  }
}
