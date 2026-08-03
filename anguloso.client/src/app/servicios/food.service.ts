import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { FoodProduct } from '../modelos/food-product';
import { FoodExchangeGroup, FoodInExchangeGroup } from '../modelos/food-exchange-group';
import { environment } from '../../environments/environments';


@Injectable({
  providedIn: 'root'
})
export class FoodService {
  private baseUrl = environment.apiUrl;

  constructor(private http: HttpClient) { }

  getProduct(barcode: string): Observable<FoodProduct> {
    return this.http.get<FoodProduct>(`${this.baseUrl}/food/barcode/${barcode}`);
  }

  searchFoods(term: string): Observable<FoodProduct[]> {
    return this.http.get<FoodProduct[]>(`${this.baseUrl}/food/search/${term}`);
  }

  getExchangeGroups(): Observable<FoodExchangeGroup[]> {
    return this.http.get<FoodExchangeGroup[]>(`${this.baseUrl}/food-exchange-groups`);
  }

  getFoodsInExchangeGroup(groupId: number): Observable<FoodInExchangeGroup[]> {
    return this.http.get<FoodInExchangeGroup[]>(`${this.baseUrl}/food-exchange-groups/${groupId}/foods`);
  }
}
