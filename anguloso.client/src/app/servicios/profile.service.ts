import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environments';
import { Profile, UpdateProfile } from '../modelos/profile';

@Injectable({ providedIn: 'root' })
export class ProfileService {
  private base = environment.apiUrl;

  constructor(private http: HttpClient) { }

  getProfile(): Observable<Profile> {
    return this.http.get<Profile>(`${this.base}/profile`);
  }

  updateProfile(dto: UpdateProfile): Observable<void> {
    return this.http.put<void>(`${this.base}/profile`, dto);
  }
}
