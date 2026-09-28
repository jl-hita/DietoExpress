import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface BillingPlan {
  id: number;
  code: string;
  name: string;
  description?: string | null;
  monthlyPrice: number;
  yearlyPrice: number;
  maxNutritionists?: number | null;
  maxClientsPerNutritionist?: number | null;
  maxTotalClients?: number | null;
  hasMonthlyStripePrice: boolean;
  hasYearlyStripePrice: boolean;
}

export interface CheckoutRequest {
  planCode: string;
  billingInterval: 'monthly' | 'yearly';
  successUrl: string;
  cancelUrl: string;
}

@Injectable({
  providedIn: 'root'
})
export class BillingService {
  private readonly baseUrl = '/api/billing';

  constructor(private http: HttpClient) {}

  getPlans(): Observable<BillingPlan[]> {
    return this.http.get<BillingPlan[]>(`${this.baseUrl}/plans`);
  }

  createCheckout(request: CheckoutRequest): Observable<{ url: string }> {
    return this.http.post<{ url: string }>(`${this.baseUrl}/checkout`, request);
  }
}
