import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environments';
import { DietListItem, DietDetail, CreateDietRequest, UpdateDietRequest } from '../modelos/diet';

@Injectable({ providedIn: 'root' })
export class DietService {
  private base = `${environment.apiUrl}/dietas`;

  constructor(private http: HttpClient) {}

  getDiets(page = 1, pageSize = 20, search = '', includeAll = false): Observable<{ items: DietListItem[]; totalCount: number; page: number; pageSize: number }> {
    const params = new URLSearchParams({
      page: String(page),
      pageSize: String(pageSize),
      ...(search.trim() ? { search: search.trim() } : {}),
      ...(includeAll ? { includeAll: 'true' } : {})
    });
    return this.http.get<{ items: DietListItem[]; totalCount: number; page: number; pageSize: number } | DietListItem[]>(`${this.base}?${params.toString()}`).pipe(
      map(response => Array.isArray(response)
        ? { items: response, totalCount: response.length, page, pageSize }
        : {
            items: Array.isArray(response?.items) ? response.items : [],
            totalCount: Number(response?.totalCount ?? 0),
            page: Number(response?.page ?? page),
            pageSize: Number(response?.pageSize ?? pageSize)
          })
    );
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

  getActiveDiet(clientId: number): Observable<any> {
    return this.http.get<any>(`${environment.apiUrl}/clients/${clientId}/diets/active`);
  }

  generateDiet(request: any): Observable<DietDetail> {
    return this.http.post<DietDetail>(`${this.base}/generate`, request);
  }

  validateDiet(clientId: number, diet: any): Observable<any[]> {
    return this.http.post<any[]>(`${this.base}/validate`, { clientId, diet });
  }

  getDietShoppingList(dietId: number): Observable<any[]> {
    return this.http.get<any[]>(`${this.base}/${dietId}/shopping-list`);
  }
}


