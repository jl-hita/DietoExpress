import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { FoodProduct } from '../modelos/food-product';
import { FoodExchangeGroup, FoodInExchangeGroup } from '../modelos/food-exchange-group';
import { environment } from '../../environments/environments';


@Injectable({
  providedIn: 'root'
})
// Encapsula las consultas de alimentos y mantiene en un único punto la construcción de filtros y parámetros del backend.
export class FoodService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) { }

  getProduct(barcode: string): Observable<FoodProduct> {
    return this.http.get<FoodProduct>(`${this.baseUrl}/food/barcode/${barcode}`);
  }

  searchFoods(term: string): Observable<FoodProduct[]> {
    return this.http.get<FoodProduct[]>(`${this.baseUrl}/food/search/${term}`);
  }

  getFoodCount(): Observable<{ count: number }> {
    return this.http.get<{ count: number }>(`${this.baseUrl}/food/count`);
  }

  getSameGroupSubstitutions(foodId: number, clientId?: number | null, limit = 15): Observable<any[]> {
    const params: Record<string, string> = { target: 'balanced', sameGroupOnly: 'true', limit: String(limit) };
    if (clientId) params['clientId'] = String(clientId);
    return this.http.get<any[]>(`${this.baseUrl}/food/${foodId}/substitutions`, { params });
  }

  getExchangeGroups(): Observable<FoodExchangeGroup[]> {
    return this.http.get<FoodExchangeGroup[]>(`${this.baseUrl}/food-exchange-groups`);
  }

  getFoodsInExchangeGroup(groupId: number): Observable<FoodInExchangeGroup[]> {
    return this.http.get<FoodInExchangeGroup[]>(`${this.baseUrl}/food-exchange-groups/${groupId}/foods`);
  }
}
