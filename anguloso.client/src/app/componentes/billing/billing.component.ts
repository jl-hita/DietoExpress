import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { BillingPlan, BillingService } from '../../servicios/billing.service';

@Component({
  selector: 'app-billing',
  standalone: true,
  imports: [
    CommonModule,
    MatButtonModule,
    MatCardModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatSnackBarModule
  ],
  template: `
    <div class="billing-page">
      <section class="hero">
        <div>
          <span class="eyebrow">DietoExpress</span>
          <h1>Elige tu plan</h1>
          <p>Amplía tu cuenta cuando necesites más capacidad para gestionar tus clientes y nutricionistas.</p>
        </div>

        <div class="interval-toggle" role="group" aria-label="Periodo de facturación">
          <button
            mat-stroked-button
            [class.selected]="billingInterval === 'monthly'"
            (click)="billingInterval = 'monthly'">
            Mensual
          </button>
          <button
            mat-stroked-button
            [class.selected]="billingInterval === 'yearly'"
            (click)="billingInterval = 'yearly'">
            Anual
            <span class="save-label">Ahorra</span>
          </button>
        </div>
      </section>

      <div *ngIf="checkoutResult === 'success'" class="result success">
        <mat-icon>check_circle</mat-icon>
        <div>
          <strong>Pago iniciado correctamente</strong>
          <span>Estamos esperando la confirmación de Stripe. Tu suscripción se activará cuando recibamos el webhook de pago.</span>
        </div>
      </div>

      <div *ngIf="checkoutResult === 'cancelled'" class="result cancelled">
        <mat-icon>info</mat-icon>
        <div>
          <strong>Pago cancelado</strong>
          <span>No se ha realizado ningún cambio en tu suscripción.</span>
        </div>
      </div>

      <div *ngIf="expiredNotice" class="result cancelled">
        <mat-icon>schedule</mat-icon>
        <div>
          <strong>Tu periodo gratuito ha terminado</strong>
          <span>Elige un plan para continuar utilizando DietoExpress.</span>
        </div>
      </div>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="42"></mat-spinner>
        <span>Cargando planes...</span>
      </div>

      <div *ngIf="!loading && plans.length" class="plans-grid">
        <mat-card *ngFor="let plan of plans" class="plan-card" [class.featured]="plan.code === 'nutri_full'">
          <div class="featured-label" *ngIf="plan.code === 'nutri_full'">Para nutricionistas</div>

          <div class="plan-header">
            <h2>{{ plan.name }}</h2>
            <p>{{ plan.description }}</p>
          </div>

          <div class="price">
            <span class="amount">
              {{ (billingInterval === 'monthly' ? plan.monthlyPrice : plan.yearlyPrice) | number:'1.2-2' }} €
            </span>
            <span class="period">/ {{ billingInterval === 'monthly' ? 'mes' : 'año' }}</span>
          </div>

          <div class="equivalent" *ngIf="billingInterval === 'yearly'">
            {{ plan.yearlyPrice / 12 | number:'1.2-2' }} €/mes de media
          </div>

          <ul class="features">
            <li *ngIf="plan.maxNutritionists">
              <mat-icon>groups</mat-icon>
              <span>{{ plan.maxNutritionists }} {{ plan.maxNutritionists === 1 ? 'nutricionista' : 'nutricionistas' }}</span>
            </li>
            <li *ngIf="plan.maxClientsPerNutritionist">
              <mat-icon>person</mat-icon>
              <span>Hasta {{ plan.maxClientsPerNutritionist }} clientes por nutricionista</span>
            </li>
            <li *ngIf="plan.maxTotalClients">
              <mat-icon>group</mat-icon>
              <span>Hasta {{ plan.maxTotalClients }} clientes totales</span>
            </li>
          </ul>

          <button
            mat-flat-button
            color="primary"
            class="checkout-button"
            [disabled]="loadingCheckout || !hasPrice(plan)"
            (click)="startCheckout(plan)">
            <mat-spinner *ngIf="loadingCheckout && selectedPlanCode === plan.code" diameter="20"></mat-spinner>
            <span *ngIf="!(loadingCheckout && selectedPlanCode === plan.code)">Continuar con el pago</span>
          </button>

          <small *ngIf="!hasPrice(plan)" class="unavailable">
            Este plan todavía no está disponible para compra.
          </small>
        </mat-card>
      </div>

      <div *ngIf="!loading && !plans.length" class="empty">
        <mat-icon>sell</mat-icon>
        <h2>No hay planes disponibles</h2>
        <p>No hay planes comerciales configurados en este momento.</p>
      </div>

      <p class="secure-note">
        <mat-icon>lock</mat-icon>
        El pago se realiza de forma segura en Stripe. DietoExpress no almacena los datos de tu tarjeta.
      </p>
    </div>
  `,
  styles: [`
    .billing-page {
      max-width: 1180px;
      margin: 0 auto;
      padding: 32px 24px 48px;
    }

    .hero {
      display: flex;
      justify-content: space-between;
      align-items: end;
      gap: 24px;
      margin-bottom: 28px;
    }

    .eyebrow {
      color: #0f766e;
      font-size: 12px;
      font-weight: 700;
      letter-spacing: 1.4px;
      text-transform: uppercase;
    }

    h1 {
      margin: 5px 0 8px;
      color: #0f172a;
      font-size: 32px;
    }

    .hero p {
      margin: 0;
      color: #64748b;
      max-width: 650px;
    }

    .interval-toggle {
      display: flex;
      gap: 8px;
      flex-shrink: 0;
    }

    .interval-toggle button.selected {
      background: #0f766e;
      color: white;
      border-color: #0f766e;
    }

    .save-label {
      margin-left: 6px;
      font-size: 10px;
      font-weight: 700;
    }

    .plans-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
      gap: 22px;
    }

    .plan-card {
      position: relative;
      display: flex;
      flex-direction: column;
      padding: 28px;
      min-height: 470px;
      border-radius: 16px;
      overflow: hidden;
    }

    .plan-card.featured {
      border: 2px solid #0f766e;
    }

    .featured-label {
      position: absolute;
      top: 0;
      right: 0;
      padding: 7px 14px;
      background: #0f766e;
      color: white;
      font-size: 11px;
      font-weight: 700;
      border-radius: 0 0 0 10px;
    }

    .plan-header h2 {
      margin: 0 0 8px;
      color: #0f172a;
      font-size: 23px;
    }

    .plan-header p {
      min-height: 42px;
      margin: 0;
      color: #64748b;
      font-size: 14px;
    }

    .price {
      display: flex;
      align-items: baseline;
      gap: 5px;
      margin: 25px 0 3px;
    }

    .amount {
      color: #0f172a;
      font-size: 34px;
      font-weight: 800;
    }

    .period {
      color: #64748b;
    }

    .equivalent {
      color: #0f766e;
      font-size: 12px;
      font-weight: 600;
      min-height: 18px;
    }

    .features {
      flex: 1;
      padding: 20px 0 12px;
      margin: 0;
      list-style: none;
    }

    .features li {
      display: flex;
      align-items: center;
      gap: 10px;
      margin: 14px 0;
      color: #334155;
      font-size: 14px;
    }

    .features mat-icon {
      color: #0f766e;
      font-size: 20px;
      width: 20px;
      height: 20px;
    }

    .checkout-button {
      width: 100%;
      min-height: 44px;
      margin-top: auto;
    }

    .unavailable {
      display: block;
      margin-top: 8px;
      color: #b45309;
      text-align: center;
    }

    .loading {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      padding: 80px 0;
      color: #64748b;
    }

    .result {
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 15px 18px;
      margin-bottom: 22px;
      border-radius: 10px;
    }

    .result.success {
      background: #ecfdf5;
      color: #166534;
    }

    .result.cancelled {
      background: #f8fafc;
      color: #475569;
    }

    .result mat-icon {
      flex-shrink: 0;
    }

    .result div {
      display: flex;
      flex-direction: column;
      gap: 3px;
    }

    .result span {
      font-size: 13px;
    }

    .empty {
      padding: 80px 20px;
      text-align: center;
      color: #64748b;
    }

    .empty mat-icon {
      font-size: 48px;
      width: 48px;
      height: 48px;
    }

    .secure-note {
      display: flex;
      justify-content: center;
      align-items: center;
      gap: 7px;
      margin: 28px 0 0;
      color: #94a3b8;
      font-size: 12px;
    }

    .secure-note mat-icon {
      font-size: 17px;
      width: 17px;
      height: 17px;
    }

    @media (max-width: 760px) {
      .hero {
        align-items: stretch;
        flex-direction: column;
      }

      .interval-toggle {
        width: 100%;
      }

      .interval-toggle button {
        flex: 1;
      }

      .billing-page {
        padding: 24px 16px 40px;
      }
    }
  `]
})
export class BillingComponent implements OnInit {
  plans: BillingPlan[] = [];
  billingInterval: 'monthly' | 'yearly' = 'monthly';
  loading = true;
  loadingCheckout = false;
  selectedPlanCode: string | null = null;
  checkoutResult: 'success' | 'cancelled' | null = null;
  expiredNotice = false;

  constructor(
    private billingService: BillingService,
    private route: ActivatedRoute,
    private router: Router,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.expiredNotice = this.route.snapshot.queryParamMap.get('reason') === 'expired';
    const result = this.route.snapshot.queryParamMap.get('checkout');
    if (result === 'success' || result === 'cancelled') {
      this.checkoutResult = result;
    }

    this.billingService.getPlans().subscribe({
      next: plans => {
        this.plans = plans;
        this.loading = false;
      },
      error: err => {
        console.error('Error al cargar planes de facturación', err);
        this.loading = false;
        this.snackBar.open('No se han podido cargar los planes.', 'Cerrar', { duration: 5000 });
      }
    });
  }

  hasPrice(plan: BillingPlan): boolean {
    return this.billingInterval === 'monthly'
      ? plan.hasMonthlyStripePrice
      : plan.hasYearlyStripePrice;
  }

  startCheckout(plan: BillingPlan): void {
    if (this.loadingCheckout || !this.hasPrice(plan)) return;

    this.loadingCheckout = true;
    this.selectedPlanCode = plan.code;

    const origin = window.location.origin;

    this.billingService.createCheckout({
      planCode: plan.code,
      billingInterval: this.billingInterval,
      successUrl: `${origin}/billing?checkout=success`,
      cancelUrl: `${origin}/billing?checkout=cancelled`
    }).subscribe({
      next: response => {
        if (!response?.url) {
          this.loadingCheckout = false;
          this.selectedPlanCode = null;
          this.snackBar.open('Stripe no devolvió una URL de pago válida.', 'Cerrar', { duration: 5000 });
          return;
        }

        window.location.assign(response.url);
      },
      error: err => {
        this.loadingCheckout = false;
        this.selectedPlanCode = null;
        const message = err?.error?.message || err?.error || 'No se ha podido iniciar el pago.';
        this.snackBar.open(message, 'Cerrar', { duration: 5000 });
      }
    });
  }
}
