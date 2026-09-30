import { Component, OnInit, OnDestroy } from '@angular/core';
import { CommonModule } from '@angular/common'; import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card'; import { MatButtonModule } from '@angular/material/button'; import { MatIconModule } from '@angular/material/icon'; import { MatFormFieldModule } from '@angular/material/form-field'; import { MatInputModule } from '@angular/material/input'; import { MatCheckboxModule } from '@angular/material/checkbox'; import { MatSlideToggleModule } from '@angular/material/slide-toggle'; import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AdminService, AdminPlan } from '../../servicios/admin.service';
@Component({selector:'app-admin-plans',standalone:true,imports:[CommonModule,FormsModule,MatCardModule,MatButtonModule,MatIconModule,MatFormFieldModule,MatInputModule,MatCheckboxModule,MatSlideToggleModule,MatSnackBarModule],templateUrl:'./admin-plans.component.html',styleUrls:['./admin-plans.component.css']})
export class AdminPlansComponent implements OnInit, OnDestroy {
  plans: AdminPlan[] = [];
  private timers = new Map<number, ReturnType<typeof setTimeout>>();
  saveState = new Map<number, 'idle' | 'pending' | 'saving' | 'saved' | 'error'>();

  constructor(private admin: AdminService, private snack: MatSnackBar) {}

  ngOnInit() { this.load(); }

  load() {
    this.admin.getPlans().subscribe({
      next: p => {
        this.plans = p;
        p.forEach(plan => this.saveState.set(plan.id, 'saved'));
      },
      error: () => this.snack.open('No se pudieron cargar los planes', 'Cerrar', {duration:3000})
    });
  }

  scheduleSave(p: AdminPlan, immediate = false): void {
    this.saveState.set(p.id, 'pending');
    const previous = this.timers.get(p.id);
    if (previous) clearTimeout(previous);
    const delay = immediate ? 0 : 700;
    const timer = setTimeout(() => {
      this.timers.delete(p.id);
      this.save(p);
    }, delay);
    this.timers.set(p.id, timer);
  }

  save(p: AdminPlan): void {
    this.saveState.set(p.id, 'saving');
    const dto = {
      name:p.name,
      description:p.description,
      monthlyPrice:p.monthly_price,
      yearlyPrice:p.yearly_price,
      maxNutritionists:p.max_nutritionists,
      maxClientsPerNutritionist:p.max_clients_per_nutritionist,
      maxTotalClients:p.max_total_clients,
      trialDays:p.trial_days,
      active:p.active
    };
    this.admin.updatePlan(p.id,dto).subscribe({
      next: () => this.admin.updatePlanFeatures(p.id,p.features).subscribe({
        next: () => this.saveState.set(p.id, 'saved'),
        error: () => this.saveState.set(p.id, 'error')
      }),
      error: () => this.saveState.set(p.id, 'error')
    });
  }

  status(p: AdminPlan): string {
    const state = this.saveState.get(p.id) || 'idle';
    return state === 'saving' ? 'Guardando...' :
      state === 'pending' ? 'Pendiente...' :
      state === 'error' ? '⚠ Error al guardar' :
      state === 'saved' ? '✓ Guardado' : '';
  }

  ngOnDestroy() {
    this.timers.forEach(timer => clearTimeout(timer));
    this.timers.clear();
  }

  featureName(code:string) {
    const map:any={CLIENT_PORTAL:'Portal cliente',PDF_EXPORT:'Exportar PDF',PDF_BRANDING:'PDF con marca',GOOGLE_LOGIN:'Login Google',RECIPES:'Recetas',DIET_TEMPLATES:'Plantillas de dietas',SHARED_DIETS:'Dietas compartidas',MULTI_NUTRITIONIST:'Varios nutricionistas',CLINIC_DASHBOARD:'Dashboard clínica',CLIENT_ASSIGNMENT:'Reasignar clientes',AUDIT_LOGS:'Auditoría'};
    return map[code]||code;
  }
}