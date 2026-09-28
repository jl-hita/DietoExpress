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

  cancelRenewal(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.baseUrl}/subscription/cancel-renewal`, {});
  }

  reactivateRenewal(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.baseUrl}/subscription/reactivate-renewal`, {});
  }

  changeSubscription(planCode: string, billingInterval: 'monthly' | 'yearly'): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${this.baseUrl}/subscription/change`, {
      planCode,
      billingInterval
    });
  }
}
