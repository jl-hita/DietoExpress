import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { BillingPlan, BillingService } from '../../servicios/billing.service';
import { LicenseService, LicenseStatus } from '../../servicios/license.service';
import { AuthService } from '../../servicios/auth.service';

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
          <h1>Suscripción</h1>
          <p>Consulta y gestiona tu plan, la facturación y la renovación automática.</p>
        </div>
      </section>

      <div *ngIf="checkoutResult === 'success'" class="result success">
        <mat-icon>check_circle</mat-icon>
        <div>
          <strong>Pago iniciado correctamente</strong>
          <span>Estamos esperando la confirmación de Stripe. Tu suscripción se activará cuando recibamos el webhook.</span>
        </div>
      </div>

      <div *ngIf="checkoutResult === 'cancelled'" class="result cancelled">
        <mat-icon>info</mat-icon>
        <div>
          <strong>Pago cancelado</strong>
          <span>No se ha realizado ningún cambio en tu suscripción.</span>
        </div>
      </div>

      <div *ngIf="expiredNotice || isTrialExpired" class="result warning">
        <mat-icon>schedule</mat-icon>
        <div>
          <strong>Tu periodo gratuito ha terminado</strong>
          <span>Elige un plan de pago para continuar utilizando DietoExpress.</span>
        </div>
      </div>

      <div *ngIf="isSuperAdmin" class="result cancelled">
        <mat-icon>admin_panel_settings</mat-icon>
        <div>
          <strong>La cuenta SuperAdmin no tiene una suscripción de organización</strong>
          <span>La facturación de las organizaciones se gestiona desde el panel de administración.</span>
        </div>
      </div>

      <div *ngIf="loading && !isSuperAdmin" class="loading">
        <mat-spinner diameter="42"></mat-spinner>
        <span>Cargando suscripción...</span>
      </div>

      <ng-container *ngIf="!loading && license">
        <mat-card class="current-card">
          <div class="current-top">
            <div>
              <span class="section-label">Tu suscripción</span>
              <div class="current-title-row">
                <h2>{{ license.planName }}</h2>
                <span class="status" [class.expiring]="license.cancelAtPeriodEnd" [class.expired]="isTrialExpired">
                  {{ currentStatusLabel }}
                </span>
              </div>
            </div>
            <div class="current-price" *ngIf="currentPaidPlan">
              <strong>{{ currentPlanPrice | number:'1.2-2' }} €</strong>
              <span>/ {{ license.billingInterval === 'yearly' ? 'año' : 'mes' }}</span>
            </div>
          </div>

          <div class="subscription-details">
            <div *ngIf="license.currentPeriodStart">
              <span>Inicio del periodo</span>
              <strong>{{ license.currentPeriodStart | date:'d MMM y' }}</strong>
            </div>
            <div *ngIf="license.currentPeriodEnd">
              <span>{{ license.cancelAtPeriodEnd ? 'Finaliza' : 'Próxima renovación' }}</span>
              <strong>{{ license.currentPeriodEnd | date:'d MMM y' }}</strong>
            </div>
            <div *ngIf="isTrial">
              <span>Periodo de prueba</span>
              <strong>{{ trialDaysRemaining }} {{ trialDaysRemaining === 1 ? 'día' : 'días' }} restantes</strong>
            </div>
            <div *ngIf="currentPaidPlan">
              <span>Renovación automática</span>
              <strong>{{ license.cancelAtPeriodEnd ? 'Desactivada' : 'Activada' }}</strong>
            </div>
          </div>

          <div *ngIf="currentPaidPlan && license.cancelAtPeriodEnd" class="scheduled-cancellation">
            <mat-icon>event</mat-icon>
            <span>La suscripción seguirá activa hasta {{ license.currentPeriodEnd | date:'d MMMM y' }} y no se realizará ningún cargo nuevo.</span>
          </div>

          <div class="current-actions" *ngIf="currentPaidPlan">
            <button
              *ngIf="!license.cancelAtPeriodEnd"
              mat-stroked-button
              [disabled]="actionLoading"
              (click)="cancelRenewal()">
              Cancelar renovación automática
            </button>
            <button
              *ngIf="license.cancelAtPeriodEnd"
              mat-flat-button
              color="primary"
              [disabled]="actionLoading"
              (click)="reactivateRenewal()">
              Reactivar renovación automática
            </button>
          </div>
        </mat-card>

        <section *ngIf="availablePlans.length || currentPaidPlan" class="plans-section">
          <div class="section-heading">
            <div>
              <span class="section-label">{{ currentPaidPlan ? 'Cambiar de plan o facturación' : 'Elige un plan' }}</span>
              <h2>{{ currentPaidPlan ? 'Opciones disponibles' : 'Continúa con DietoExpress' }}</h2>
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
          </div>

          <div class="plans-grid">
            <mat-card *ngFor="let plan of availablePlans" class="plan-card" [class.featured]="plan.code === 'nutri_full'">
              <div class="featured-label" *ngIf="plan.code === 'nutri_full'">Para nutricionistas</div>

              <div class="plan-header">
                <h3>{{ plan.name }}</h3>
                <p>{{ plan.description }}</p>
              </div>

              <div class="price">
                <span class="amount">{{ priceFor(plan) | number:'1.2-2' }} €</span>
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
                [disabled]="actionLoading || !hasPrice(plan)"
                (click)="selectPlan(plan)">
                <mat-spinner *ngIf="actionLoading && selectedPlanCode === plan.code" diameter="20"></mat-spinner>
                <span *ngIf="!(actionLoading && selectedPlanCode === plan.code)">
                  {{ currentPaidPlan ? actionLabel(plan) : 'Continuar con el pago' }}
                </span>
              </button>

              <small *ngIf="!hasPrice(plan)" class="unavailable">
                Este plan todavía no está disponible para compra.
              </small>
            </mat-card>
          </div>
        </section>

        <div *ngIf="currentPaidPlan && !availablePlans.length" class="no-upgrade">
          <mat-icon>check_circle</mat-icon>
          <div>
            <strong>Ya tienes el plan con mayor nivel de prestaciones.</strong>
            <span>Puedes cambiar entre facturación mensual y anual cuando quieras.</span>
          </div>
        </div>
      </ng-container>

      <p class="secure-note">
        <mat-icon>lock</mat-icon>
        El pago se realiza de forma segura en Stripe. DietoExpress no almacena los datos de tu tarjeta.
      </p>
    </div>
  `,
  styles: [`
    .billing-page { width: 100%; margin: 0; padding: 32px 24px 48px; }
    .hero { margin-bottom: 24px; }
    .eyebrow, .section-label { color: #0f766e; font-size: 12px; font-weight: 700; letter-spacing: 1.2px; text-transform: uppercase; }
    h1 { margin: 5px 0 8px; color: #0f172a; font-size: 32px; }
    .hero p { margin: 0; color: #64748b; }
    .section-label { display: block; margin-bottom: 6px; }
    .result { display: flex; align-items: flex-start; gap: 12px; padding: 15px 18px; margin-bottom: 22px; border-radius: 10px; }
    .result.success { background: #ecfdf5; color: #166534; }
    .result.cancelled { background: #f8fafc; color: #475569; }
    .result.warning { background: #fff7ed; color: #9a3412; }
    .result div { display: flex; flex-direction: column; gap: 3px; }
    .result span { font-size: 13px; }
    .current-card { padding: 26px; border-radius: 16px; }
    .current-top { display: flex; justify-content: space-between; align-items: flex-start; gap: 20px; }
    .current-title-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; }
    .current-title-row h2 { margin: 0; color: #0f172a; font-size: 26px; }
    .status { padding: 5px 10px; border-radius: 999px; background: #dcfce7; color: #166534; font-size: 12px; font-weight: 700; }
    .status.expiring { background: #fef3c7; color: #92400e; }
    .status.expired { background: #fee2e2; color: #991b1b; }
    .current-price { display: flex; align-items: baseline; gap: 5px; color: #64748b; }
    .current-price strong { color: #0f172a; font-size: 25px; }
    .subscription-details { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 18px; margin-top: 24px; padding-top: 22px; border-top: 1px solid #e2e8f0; }
    .subscription-details div { display: flex; flex-direction: column; gap: 5px; }
    .subscription-details span { color: #64748b; font-size: 12px; }
    .subscription-details strong { color: #334155; }
    .scheduled-cancellation { display: flex; align-items: center; gap: 9px; margin-top: 20px; padding: 12px 14px; background: #fffbeb; color: #92400e; border-radius: 9px; font-size: 13px; }
    .scheduled-cancellation mat-icon { flex-shrink: 0; }
    .current-actions { display: flex; justify-content: flex-end; margin-top: 20px; }
    .plans-section { margin-top: 34px; }
    .section-heading { display: flex; justify-content: space-between; align-items: end; gap: 20px; margin-bottom: 18px; }
    .section-heading h2 { margin: 0; color: #0f172a; font-size: 22px; }
    .interval-toggle { display: flex; gap: 8px; flex-shrink: 0; }
    .interval-toggle button.selected { background: #0f766e; color: white; border-color: #0f766e; }
    .save-label { margin-left: 6px; font-size: 10px; font-weight: 700; }
    .plans-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(290px, 1fr)); gap: 22px; }
    .plan-card { position: relative; display: flex; flex-direction: column; padding: 26px; min-height: 430px; border-radius: 16px; overflow: hidden; }
    .plan-card.featured { border: 2px solid #0f766e; }
    .featured-label { position: absolute; top: 0; right: 0; padding: 7px 14px; background: #0f766e; color: white; font-size: 11px; font-weight: 700; border-radius: 0 0 0 10px; }
    .plan-header h3 { margin: 0 0 8px; color: #0f172a; font-size: 22px; }
    .plan-header p { min-height: 42px; margin: 0; color: #64748b; font-size: 14px; }
    .price { display: flex; align-items: baseline; gap: 5px; margin: 24px 0 3px; }
    .amount { color: #0f172a; font-size: 32px; font-weight: 800; }
    .period { color: #64748b; }
    .equivalent { min-height: 18px; color: #0f766e; font-size: 12px; font-weight: 600; }
    .features { flex: 1; padding: 18px 0 10px; margin: 0; list-style: none; }
    .features li { display: flex; align-items: center; gap: 10px; margin: 13px 0; color: #334155; font-size: 14px; }
    .features mat-icon { color: #0f766e; font-size: 20px; width: 20px; height: 20px; }
    .checkout-button { width: 100%; min-height: 44px; margin-top: auto; }
    .unavailable { display: block; margin-top: 8px; color: #b45309; text-align: center; }
    .no-upgrade { display: flex; align-items: center; gap: 12px; margin-top: 28px; padding: 18px; background: #f8fafc; border-radius: 12px; color: #475569; }
    .no-upgrade mat-icon { color: #0f766e; }
    .no-upgrade div { display: flex; flex-direction: column; gap: 3px; }
    .no-upgrade span { font-size: 13px; }
    .loading { display: flex; flex-direction: column; align-items: center; gap: 12px; padding: 80px 0; color: #64748b; }
    .secure-note { display: flex; justify-content: center; align-items: center; gap: 7px; margin: 28px 0 0; color: #94a3b8; font-size: 12px; }
    .secure-note mat-icon { font-size: 17px; width: 17px; height: 17px; }
    @media (max-width: 760px) {
      .current-top, .section-heading { align-items: stretch; flex-direction: column; }
      .interval-toggle { width: 100%; }
      .interval-toggle button { flex: 1; }
      .billing-page { padding: 24px 16px 40px; }
    }
  `]
})
export class BillingComponent implements OnInit {
  plans: BillingPlan[] = [];
  license: LicenseStatus | null = null;
  billingInterval: 'monthly' | 'yearly' = 'monthly';
  loading = true;
  actionLoading = false;
  selectedPlanCode: string | null = null;
  checkoutResult: 'success' | 'cancelled' | null = null;
  expiredNotice = false;
  isSuperAdmin = false;

  constructor(
    private billingService: BillingService,
    private authService: AuthService,
    private licenseService: LicenseService,
    private route: ActivatedRoute,
    private snackBar: MatSnackBar
  ) {}

  // La página interpreta el resultado de Stripe desde la URL y, tras un checkout, espera a que el webhook active la suscripción.
  ngOnInit(): void {
    this.expiredNotice = this.route.snapshot.queryParamMap.get('reason') === 'expired';
    const result = this.route.snapshot.queryParamMap.get('checkout');
    if (result === 'success' || result === 'cancelled') this.checkoutResult = result;
    this.isSuperAdmin = this.authService.isSuperAdmin();
    if (!this.isSuperAdmin) this.loadData();
    else this.loading = false;

    if (this.checkoutResult === 'success') {
      this.waitForCheckoutActivation(0);
    }
  }

  get currentPaidPlan(): boolean {
    return !!this.license && this.license.planCode !== 'free' && !!this.license.billingInterval;
  }

  get isTrial(): boolean {
    return this.license?.planCode === 'free';
  }

  get isTrialExpired(): boolean {
    return this.isTrial && !!this.license?.expiresAt && new Date(this.license.expiresAt).getTime() <= Date.now();
  }

  get trialDaysRemaining(): number {
    if (!this.license?.expiresAt) return 0;
    return Math.max(0, Math.ceil((new Date(this.license.expiresAt).getTime() - Date.now()) / 86400000));
  }

  get currentStatusLabel(): string {
    if (this.isTrialExpired) return 'Finalizado';
    if (this.license?.cancelAtPeriodEnd) return 'Finaliza al terminar el periodo';
    if (this.license?.status === 'past_due') return 'Pago pendiente';
    if (this.isTrial) return 'Periodo de prueba';
    return 'Activa';
  }

  get currentPlanPrice(): number {
    if (!this.license) return 0;
    const plan = this.plans.find(p => p.code === this.license?.planCode);
    if (!plan) return 0;
    return this.license.billingInterval === 'yearly' ? plan.yearlyPrice : plan.monthlyPrice;
  }

  // El catálogo visible se filtra por nivel para no ofrecer descensos mientras una suscripción de mayor nivel sigue activa.
  get availablePlans(): BillingPlan[] {
    if (!this.license) return [];
    if (this.isTrial || this.isTrialExpired) return this.plans;

    // Solo se permiten cambios hacia un plan de nivel superior.
    // Enterprise es el nivel máximo, por lo que no se ofrece Professional
    // mientras la suscripción Enterprise siga activa.
    const planRank: Record<string, number> = {
      free: 0,
      nutri_full: 1,
      clinic_full: 2
    };
    const currentRank = planRank[this.license.planCode] ?? 0;

    return this.plans.filter(p => {
      const targetRank = planRank[p.code] ?? 0;
      return targetRank > currentRank;
    });
  }

  priceFor(plan: BillingPlan): number {
    return this.billingInterval === 'yearly' ? plan.yearlyPrice : plan.monthlyPrice;
  }

  hasPrice(plan: BillingPlan): boolean {
    return this.billingInterval === 'monthly' ? plan.hasMonthlyStripePrice : plan.hasYearlyStripePrice;
  }

  actionLabel(plan: BillingPlan): string {
    if (plan.code === this.license?.planCode)
      return this.billingInterval === this.license.billingInterval ? 'Plan actual' : 'Cambiar facturación';
    return 'Cambiar a este plan';
  }

  selectPlan(plan: BillingPlan): void {
    if (this.actionLoading || !this.hasPrice(plan)) return;

    if (!this.currentPaidPlan) {
      this.startCheckout(plan);
      return;
    }

    if (this.license?.cancelAtPeriodEnd) {
      this.snackBar.open('Reactiva primero la renovación automática para cambiar de plan.', 'Cerrar', { duration: 5000 });
      return;
    }

    if (plan.code === this.license?.planCode && this.billingInterval === this.license.billingInterval) return;

    const targetPrice = this.priceFor(plan);
    const targetPeriod = this.billingInterval === 'yearly' ? 'año' : 'mes';
    const message = plan.code === this.license?.planCode
      ? `Vas a cambiar la facturación a ${targetPrice.toFixed(2)} €/${targetPeriod}. Stripe aplicará el prorrateo correspondiente al periodo actual. ¿Continuar?`
      : `Vas a cambiar de ${this.license?.planName} a ${plan.name} (${targetPrice.toFixed(2)} €/${targetPeriod}). Stripe aplicará automáticamente el prorrateo del periodo actual y facturará la diferencia correspondiente. ¿Continuar?`;

    if (!window.confirm(message)) return;

    this.actionLoading = true;
    this.selectedPlanCode = plan.code;

    this.billingService.changeSubscription(plan.code, this.billingInterval).subscribe({
      next: response => {
        this.actionLoading = false;
        this.selectedPlanCode = null;
        this.snackBar.open(response.message || 'Cambio de suscripción enviado correctamente.', 'Cerrar', { duration: 6000 });
        this.loadData();
      },
      error: err => {
        this.actionLoading = false;
        this.selectedPlanCode = null;
        this.showError(err, 'No se ha podido cambiar la suscripción.');
      }
    });
  }

  cancelRenewal(): void {
    const endDate = this.license?.currentPeriodEnd
      ? new Date(this.license.currentPeriodEnd).toLocaleDateString('es-ES')
      : 'el final del periodo actual';
    if (!window.confirm(`La suscripción seguirá activa hasta el ${endDate}. Después no se realizará ningún cargo nuevo. ¿Quieres cancelar la renovación automática?`)) return;

    this.actionLoading = true;
    this.billingService.cancelRenewal().subscribe({
      next: response => {
        this.snackBar.open(response.message || 'Renovación automática cancelada.', 'Cerrar', { duration: 5000 });
        this.refreshSubscriptionAfterStripeChange(true);
      },
      error: err => {
        this.actionLoading = false;
        this.showError(err, 'No se ha podido cancelar la renovación automática.');
      }
    });
  }

  reactivateRenewal(): void {
    this.actionLoading = true;
    this.billingService.reactivateRenewal().subscribe({
      next: response => {
        this.snackBar.open(response.message || 'Renovación automática reactivada.', 'Cerrar', { duration: 5000 });
        this.refreshSubscriptionAfterStripeChange(false);
      },
      error: err => {
        this.actionLoading = false;
        this.showError(err, 'No se ha podido reactivar la renovación automática.');
      }
    });
  }

  private startCheckout(plan: BillingPlan): void {
    this.actionLoading = true;
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
          this.actionLoading = false;
          this.selectedPlanCode = null;
          this.snackBar.open('Stripe no devolvió una URL de pago válida.', 'Cerrar', { duration: 5000 });
          return;
        }
        window.location.assign(response.url);
      },
      error: err => {
        this.actionLoading = false;
        this.selectedPlanCode = null;
        this.showError(err, 'No se ha podido iniciar el pago.');
      }
    });
  }

  private waitForCheckoutActivation(attempt: number): void {
    if (attempt >= 10) return;

    setTimeout(() => {
      this.licenseService.getLicense().subscribe({
        next: license => {
          if (license.planCode !== 'free' && license.status === 'active') {
            this.authService.refreshSession().subscribe({
              next: session => {
                this.authService.login(session);
                window.location.reload();
              },
              error: () => this.waitForCheckoutActivation(attempt + 1)
            });
          } else {
            this.waitForCheckoutActivation(attempt + 1);
          }
        },
        error: () => this.waitForCheckoutActivation(attempt + 1)
      });
    }, attempt === 0 ? 1500 : 2000);
  }

  private refreshSubscriptionAfterStripeChange(expectedCancelAtPeriodEnd: boolean): void {
    let attempts = 0;

    const poll = () => {
      attempts++;
      this.licenseService.getLicense().subscribe({
        next: license => {
          this.license = license;
          if (license.billingInterval) this.billingInterval = license.billingInterval;

          if (license.cancelAtPeriodEnd === expectedCancelAtPeriodEnd || attempts >= 5) {
            this.actionLoading = false;
            return;
          }

          setTimeout(poll, 1000);
        },
        error: () => {
          if (attempts >= 5) {
            this.actionLoading = false;
            return;
          }
          setTimeout(poll, 1000);
        }
      });
    };

    poll();
  }

  private loadData(): void {
    this.loading = true;
    this.licenseService.getLicense().subscribe({
      next: license => {
        this.license = license;
        if (license.billingInterval) this.billingInterval = license.billingInterval;
        this.billingService.getPlans().subscribe({
          next: plans => {
            this.plans = plans;
            this.loading = false;
          },
          error: err => {
            this.loading = false;
            this.showError(err, 'No se han podido cargar los planes.');
          }
        });
      },
      error: err => {
        this.loading = false;
        this.showError(err, 'No se ha podido cargar la suscripción.');
      }
    });
  }

  private showError(err: any, fallback: string): void {
    const message = err?.error?.message || err?.error || fallback;
    this.snackBar.open(message, 'Cerrar', { duration: 6000 });
  }
}
