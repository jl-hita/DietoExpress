import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environments';
import { DietListItem, DietDetail, CreateDietRequest, UpdateDietRequest } from '../modelos/diet';

@Injectable({ providedIn: 'root' })
export class DietService {
  private base = `${environment.apiUrl}/dietas`;

  constructor(private http: HttpClient) {}

  getDiets(): Observable<DietListItem[]> {
    return this.http.get<DietListItem[]>(this.base);
  }

  getDiet(id: number): Observable<DietDetail> {
    return this.http.get<DietDetail>(`${this.base}/${id}`);
  }

  createDiet(dto: CreateDietRequest): Observable<DietListItem> {
    return this.http.post<DietListItem>(this.base, dto);
  }

  updateDiet(id: number, dto: UpdateDietRequest): Observable<any> {
    return this.http.put<any>(`${this.base}/${id}`, dto);
  }

  deleteDiet(id: number): Observable<any> {
    return this.http.delete<any>(`${this.base}/${id}`);
  }
}
