import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export interface PublicPlan {
  code: string;
  name: string;
  description?: string | null;
  monthlyPrice: number;
  yearlyPrice: number;
  maxNutritionists?: number | null;
  maxClientsPerNutritionist?: number | null;
  maxTotalClients?: number | null;
  /** Días de prueba gratuita del plan; ausente si el plan no tiene prueba (p. ej. clínica). */
  trialDays?: number | null;
  features: string[];
}

export interface PublicPlansResponse {
  plans: PublicPlan[];
}

/** Descuento anual respecto a pagar doce mensualidades, redondeado a un entero. 0 si no hay ahorro. */
export function yearlySavingsPercent(plan: Pick<PublicPlan, 'monthlyPrice' | 'yearlyPrice'>): number {
  const twelveMonths = plan.monthlyPrice * 12;
  if (twelveMonths <= 0 || plan.yearlyPrice <= 0 || plan.yearlyPrice >= twelveMonths) return 0;
  return Math.round((1 - plan.yearlyPrice / twelveMonths) * 100);
}

/** Etiquetas comerciales de las funcionalidades de un plan, en el orden en que se muestran. Los códigos desconocidos se omiten. */
export const PLAN_FEATURE_LABELS: ReadonlyArray<{ code: string; label: string }> = [
  { code: 'CLIENT_PORTAL', label: 'Portal del paciente y app Android' },
  { code: 'PDF_EXPORT', label: 'Dietas y documentos en PDF' },
  { code: 'PDF_BRANDING', label: 'PDF con tu logo y tu marca' },
  { code: 'RECIPES', label: 'Recetario propio' },
  { code: 'DIET_TEMPLATES', label: 'Plantillas de dietas' },
  { code: 'SHARED_DIETS', label: 'Dietas y recursos compartidos en equipo' },
  { code: 'MULTI_NUTRITIONIST', label: 'Varios nutricionistas en una misma cuenta' },
  { code: 'CLINIC_DASHBOARD', label: 'Panel de clínica' },
  { code: 'CLIENT_ASSIGNMENT', label: 'Asignación de pacientes al equipo' },
  { code: 'AUDIT_LOGS', label: 'Registro de auditoría de accesos' }
];

export function planFeatureLabels(plan: Pick<PublicPlan, 'features'>): string[] {
  const enabled = new Set(plan.features);
  return PLAN_FEATURE_LABELS.filter(f => enabled.has(f.code)).map(f => f.label);
}

@Injectable({ providedIn: 'root' })
// Planes y precios públicos: se leen del mismo catálogo que el cobro para que la web nunca anuncie un precio distinto.
export class PublicPlansService {
  constructor(private http: HttpClient) { }

  getPlans(): Observable<PublicPlansResponse> {
    return this.http.get<PublicPlansResponse>('/api/public/plans');
  }
}
