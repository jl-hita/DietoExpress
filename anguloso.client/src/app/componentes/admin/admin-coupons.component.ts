import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';

interface CouponRow {
  id: number;
  campaignId: number | null;
  code: string;
  discountPercent: number;
  allowedPlanCodes: string[];
  allowedIntervals: string[];
  startsAt: string | null;
  expiresAt: string | null;
  isActive: boolean;
  createdAt: string;
  redeemedCount: number;
  lastRedeemedAt: string | null;
}

@Component({
  standalone: true,
  selector: 'app-admin-coupons',
  imports: [CommonModule],
  template: `
    <main class="coupon-admin">
      <header>
        <div>
          <p class="eyebrow">SuperAdmin</p>
          <h1>Cupones promocionales</h1>
          <p>Descuentos del 1 al 100 % aplicables solo a la primera factura.</p>
        </div>
        <button type="button" (click)="load()" [disabled]="loading">Actualizar</button>
      </header>

      <section class="panel">
        <h2>Crear código individual</h2>
        <div class="fields">
          <label>Código <input maxlength="64" [value]="code" (input)="code = valueOf($event)" placeholder="LANZAMIENTO50"></label>
          <label>Descuento (%) <input type="number" min="1" max="100" [value]="discount" (input)="discount = numberOf($event, 50)"></label>
          <label>Plan
            <select [value]="plans" (change)="plans = valueOf($event)">
              <option value="both">Nutricionista y clínica</option>
              <option value="nutri_full">Solo nutricionista</option>
              <option value="clinic_full">Solo clínica</option>
            </select>
          </label>
          <label>Facturación
            <select [value]="intervals" (change)="intervals = valueOf($event)">
              <option value="both">Mensual y anual</option>
              <option value="monthly">Solo mensual</option>
              <option value="yearly">Solo anual</option>
            </select>
          </label>
          <label>Caduca (opcional) <input type="date" [value]="expiresAt" (input)="expiresAt = valueOf($event)"></label>
        </div>
        <button type="button" (click)="createIndividual()" [disabled]="saving">Crear código</button>
      </section>

      <section class="panel">
        <h2>Generar campaña</h2>
        <div class="fields">
          <label>Nombre de campaña <input maxlength="120" [value]="campaignName" (input)="campaignName = valueOf($event)" placeholder="Lanzamiento otoño"></label>
          <label>Descuento (%) <input type="number" min="1" max="100" [value]="campaignDiscount" (input)="campaignDiscount = numberOf($event, 50)"></label>
          <label>Número de códigos <input type="number" min="1" max="10000" [value]="codeCount" (input)="codeCount = numberOf($event, 25)"></label>
          <label>Plan
            <select [value]="campaignPlans" (change)="campaignPlans = valueOf($event)">
              <option value="both">Nutricionista y clínica</option>
              <option value="nutri_full">Solo nutricionista</option>
              <option value="clinic_full">Solo clínica</option>
            </select>
          </label>
          <label>Facturación
            <select [value]="campaignIntervals" (change)="campaignIntervals = valueOf($event)">
              <option value="both">Mensual y anual</option>
              <option value="monthly">Solo mensual</option>
              <option value="yearly">Solo anual</option>
            </select>
          </label>
          <label>Caduca (opcional) <input type="date" [value]="campaignExpiresAt" (input)="campaignExpiresAt = valueOf($event)"></label>
        </div>
        <button type="button" (click)="createCampaign()" [disabled]="saving">Generar códigos</button>
        <p *ngIf="generatedCodes.length" class="success">Se han generado {{ generatedCodes.length }} códigos. Cópialos ahora; se muestran también en el listado.</p>
        <textarea *ngIf="generatedCodes.length" readonly rows="5" [value]="generatedCodes.join('\n')"></textarea>
      </section>

      <p *ngIf="message" class="message" role="status">{{ message }}</p>
      <p *ngIf="loading">Cargando cupones…</p>
      <p *ngIf="listError" class="message" role="alert">{{ listError }}</p>
      <section class="panel" *ngIf="!loading">
        <h2>Códigos existentes (máximo 500)</h2>
        <div class="table-wrap">
          <table>
            <thead><tr><th>Código</th><th>Descuento</th><th>Planes</th><th>Vigencia</th><th>Canjes</th><th>Estado</th><th></th></tr></thead>
            <tbody>
              <tr *ngFor="let coupon of coupons">
                <td><code>{{ coupon.code }}</code></td>
                <td>{{ coupon.discountPercent }} %</td>
                <td>{{ coupon.allowedPlanCodes.join(', ') }}</td>
                <td>{{ coupon.expiresAt ? (coupon.expiresAt | date:'d/M/y') : 'Sin caducidad' }}</td>
                <td>{{ coupon.redeemedCount }}</td>
                <td>{{ coupon.isActive ? 'Activo' : 'Inactivo' }}</td>
                <td><button type="button" (click)="setActive(coupon, !coupon.isActive)">{{ coupon.isActive ? 'Desactivar' : 'Activar' }}</button></td>
              </tr>
              <tr *ngIf="!coupons.length"><td colspan="7">Todavía no hay cupones.</td></tr>
            </tbody>
          </table>
        </div>
      </section>
    </main>
  `,
  styles: [`
    :host { display:block; }
    .coupon-admin { max-width:1200px; margin:0 auto; padding:28px 24px 48px; color:#0f172a; }
    header { display:flex; justify-content:space-between; align-items:center; gap:16px; margin-bottom:24px; }
    h1 { margin:4px 0 8px; font-size:30px; } h2 { margin:0 0 18px; font-size:19px; }
    .eyebrow { margin:0; color:#0f766e; text-transform:uppercase; font-weight:700; letter-spacing:1px; font-size:12px; }
    .panel { background:white; border:1px solid #e2e8f0; border-radius:14px; padding:22px; margin:18px 0; }
    .fields { display:grid; grid-template-columns:repeat(auto-fit,minmax(190px,1fr)); gap:14px; margin-bottom:18px; }
    label { display:grid; gap:7px; color:#334155; font-size:13px; font-weight:600; }
    input,select,textarea { box-sizing:border-box; width:100%; border:1px solid #cbd5e1; border-radius:7px; padding:10px; font:inherit; background:white; }
    button { border:1px solid #cbd5e1; border-radius:7px; padding:9px 13px; background:#f8fafc; color:#0f172a; cursor:pointer; }
    button:disabled { opacity:.55; cursor:wait; } .table-wrap { overflow-x:auto; }
    table { width:100%; border-collapse:collapse; font-size:13px; } th,td { padding:11px 9px; border-bottom:1px solid #e2e8f0; text-align:left; }
    th { color:#475569; background:#f8fafc; } code { font-weight:700; } .success { color:#166534; } .message { margin:16px 0; color:#334155; }
    @media(max-width:600px) { .coupon-admin { padding:20px 12px; } header { align-items:flex-start; flex-direction:column; } .panel { padding:16px; } }
  `]
})
export class AdminCouponsComponent implements OnInit {
  coupons: CouponRow[] = [];
  loading = false;
  saving = false;
  message = '';
  listError = '';
  code = '';
  discount = 50;
  plans = 'both';
  intervals = 'both';
  expiresAt = '';
  campaignName = '';
  campaignDiscount = 50;
  codeCount = 25;
  campaignPlans = 'both';
  campaignIntervals = 'both';
  campaignExpiresAt = '';
  generatedCodes: string[] = [];

  constructor(private readonly http: HttpClient) {}
  ngOnInit(): void { this.load(); }

  valueOf(event: Event): string { return (event.target as HTMLInputElement).value; }
  numberOf(event: Event, fallback: number): number {
    const value = Number(this.valueOf(event));
    return Number.isFinite(value) ? value : fallback;
  }
  private planCodes(value: string): string[] { return value === 'both' ? ['nutri_full', 'clinic_full'] : [value]; }
  private intervalsFor(value: string): string[] { return value === 'both' ? ['monthly', 'yearly'] : [value]; }
  private expiry(value: string): string | null { return value ? new Date(`${value}T23:59:59.000Z`).toISOString() : null; }

  load(): void {
    this.loading = true;
    this.listError = '';
    this.http.get<CouponRow[]>('/api/admin/coupons').subscribe({
      next: rows => { this.coupons = rows; this.loading = false; },
      error: error => {
        this.loading = false;
        this.listError = 'No se ha podido actualizar el listado: ' + this.errorMessage(error);
      }
    });
  }

  createIndividual(): void {
    if (!this.code.trim()) { this.message = 'Introduce un código para crear el cupón.'; return; }
    this.saving = true; this.message = '';
    this.http.post('/api/admin/coupons', {
      code: this.code, discountPercent: this.discount,
      allowedPlanCodes: this.planCodes(this.plans), allowedIntervals: this.intervalsFor(this.intervals),
      startsAt: null, expiresAt: this.expiry(this.expiresAt)
    }).subscribe({
      next: () => { this.saving = false; this.message = 'Cupón creado.'; this.code = ''; this.load(); },
      error: error => { this.saving = false; this.message = this.errorMessage(error); }
    });
  }

  createCampaign(): void {
    if (!this.campaignName.trim()) { this.message = 'Introduce un nombre para la campaña.'; return; }
    if (this.codeCount < 1 || this.codeCount > 10000) { this.message = 'La campaña debe generar entre 1 y 10.000 códigos.'; return; }
    this.saving = true; this.message = ''; this.generatedCodes = [];
    this.http.post<{campaignId:number; count:number; codes:string[]}>('/api/admin/coupons/campaigns', {
      name: this.campaignName, discountPercent: this.campaignDiscount, codeCount: this.codeCount,
      allowedPlanCodes: this.planCodes(this.campaignPlans), allowedIntervals: this.intervalsFor(this.campaignIntervals),
      startsAt: null, expiresAt: this.expiry(this.campaignExpiresAt)
    }).subscribe({
      next: result => { this.saving = false; this.generatedCodes = result.codes; this.message = `Campaña creada: ${result.count} códigos únicos.`; this.load(); },
      error: error => { this.saving = false; this.message = this.errorMessage(error); }
    });
  }

  setActive(coupon: CouponRow, isActive: boolean): void {
    this.http.put<void>(`/api/admin/coupons/${coupon.id}/active`, { isActive }).subscribe({
      next: () => { this.message = `Cupón ${coupon.code} actualizado.`; this.load(); },
      error: error => { this.message = this.errorMessage(error); }
    });
  }

  private errorMessage(error: any): string {
    return error?.error?.message || (typeof error?.error === 'string' ? error.error : null) || 'No se ha podido completar la operación.';
  }
}
