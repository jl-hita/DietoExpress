import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpParams } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

interface NutrientField {
  key: string;
  label: string;
  unit: string;
  dtoKey: string;
}

interface FoodDataQualityItem {
  id: number;
  name: string;
  brands?: string | null;
  source?: string | null;
  category?: string | null;
  usageCount: number;
  missingFields: string[];
  [key: string]: any;
}

@Component({
  standalone: true,
  selector: 'app-admin-food-data-quality',
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatIconModule, MatSnackBarModule],
  template: `
    <main class="quality-page">
      <header class="page-header">
        <div>
          <p class="eyebrow">MANTENIMIENTO DEL CATÁLOGO</p>
          <h1>Calidad de datos de alimentos</h1>
          <p>Prioriza los alimentos más utilizados y completa su información nutricional para mejorar los cálculos de las dietas.</p>
        </div>
        <button mat-stroked-button (click)="load()" [disabled]="loading"><mat-icon>refresh</mat-icon> Actualizar</button>
      </header>

      <section class="summary" *ngIf="!loading">
        <div class="summary-icon"><mat-icon>analytics</mat-icon></div>
        <div><strong>{{ items.length }}</strong><span>alimentos pendientes en esta consulta</span></div>
        <small>Ordenados por uso en dietas y recetas</small>
      </section>

      <section class="search-bar">
        <mat-form-field appearance="outline">
          <mat-label>Buscar alimento</mat-label>
          <input matInput [(ngModel)]="search" (keyup.enter)="load()" placeholder="Nombre o marca" />
          <button *ngIf="search" matSuffix mat-icon-button aria-label="Limpiar búsqueda" (click)="search='';load()"><mat-icon>close</mat-icon></button>
        </mat-form-field>
        <button mat-flat-button color="primary" (click)="load()" [disabled]="loading"><mat-icon>search</mat-icon> Buscar</button>
      </section>

      <div class="state" *ngIf="loading"><mat-icon>hourglass_top</mat-icon> Cargando alimentos pendientes…</div>
      <div class="state error" *ngIf="!loading && error">{{ error }}</div>
      <div class="state empty" *ngIf="!loading && !error && items.length===0">
        <mat-icon>task_alt</mat-icon><strong>¡Catálogo al día!</strong><span>No hay alimentos usados con datos nutricionales pendientes para esta búsqueda.</span>
      </div>

      <section class="food-list" *ngIf="!loading">
        <mat-card class="food-card" *ngFor="let item of items; trackBy: trackById">
          <div class="food-heading">
            <div class="food-title">
              <span class="usage"><mat-icon>trending_up</mat-icon>{{ item.usageCount }} {{ item.usageCount===1 ? 'uso' : 'usos' }}</span>
              <h2>{{ item.name }}</h2>
              <p>{{ item.brands || 'Sin marca' }}<span *ngIf="item.category"> · {{ item.category }}</span><span *ngIf="item.source"> · {{ item.source }}</span></p>
            </div>
            <span class="missing-count">{{ item.missingFields.length }} {{ item.missingFields.length===1 ? 'dato pendiente' : 'datos pendientes' }}</span>
          </div>

          <div class="nutrient-grid">
            <label class="nutrient-field" *ngFor="let field of getMissingFields(item)">
              <span>{{ field.label }}</span>
              <div class="input-with-unit">
                <input type="number" min="0" step="any" [(ngModel)]="item[field.key]" [name]="'food-'+item.id+'-'+field.key" [attr.aria-label]="field.label + ' por 100 g'" placeholder="Añadir valor" />
                <small>{{ field.unit }}</small>
              </div>
            </label>
          </div>

          <footer class="food-actions">
            <span class="hint">Valores por 100 g · Introduce solo datos contrastados</span>
            <div>
              <button mat-button color="warn" (click)="ignore(item)" [disabled]="savingIds.has(item.id)"><mat-icon>visibility_off</mat-icon> Ignorar 30 días</button>
              <button mat-flat-button color="primary" (click)="save(item)" [disabled]="savingIds.has(item.id) || !hasValuesToSave(item)"><mat-icon>save</mat-icon> Guardar datos</button>
            </div>
          </footer>
        </mat-card>
      </section>
    </main>
  `,
  styles: [`
    .quality-page{max-width:1180px;margin:0 auto;padding:28px 24px 48px;color:#172033}
    .page-header{display:flex;justify-content:space-between;align-items:flex-start;gap:20px;margin-bottom:22px}
    .page-header h1{margin:3px 0 7px;font-size:27px;letter-spacing:-.035em;font-weight:800}
    .page-header p:not(.eyebrow){max-width:690px;margin:0;color:#64748b;line-height:1.5}
    .eyebrow{margin:0;color:#0f766e;font-size:10px;font-weight:800;letter-spacing:.14em}
    .summary{display:flex;align-items:center;gap:13px;padding:15px 18px;margin-bottom:20px;border:1px solid #dbeafe;border-radius:15px;background:linear-gradient(110deg,#f0fdfa,#eff6ff)}
    .summary-icon{display:grid;place-items:center;width:42px;height:42px;border-radius:12px;background:#fff;color:#0f766e}
    .summary strong{display:block;font-size:21px;line-height:1.1}.summary span{color:#475569;font-size:12px}
    .summary small{margin-left:auto;color:#64748b}
    .search-bar{display:flex;align-items:center;gap:10px;margin-bottom:15px}.search-bar mat-form-field{width:min(420px,100%)}
    .food-list{display:grid;gap:13px}.food-card{padding:17px 19px;border:1px solid #e2e8f0;border-radius:15px;box-shadow:0 2px 10px #0f172a08}
    .food-heading{display:flex;justify-content:space-between;gap:14px;align-items:flex-start;margin-bottom:14px}
    .usage{display:inline-flex;align-items:center;gap:4px;color:#0f766e;font-size:10px;font-weight:800}.usage mat-icon{width:14px;height:14px;font-size:14px}
    .food-title h2{margin:3px 0;font-size:17px;font-weight:750}.food-title p{margin:0;color:#94a3b8;font-size:11px}
    .missing-count{padding:5px 9px;border-radius:999px;background:#fff7ed;color:#c2410c;font-size:10px;font-weight:750;white-space:nowrap}
    .nutrient-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px}
    .nutrient-field{display:flex;flex-direction:column;gap:5px;color:#475569;font-size:11px;font-weight:650}
    .input-with-unit{display:flex;align-items:center;border:1px solid #cbd5e1;border-radius:8px;background:#fff}
    .input-with-unit:focus-within{border-color:#0f766e;box-shadow:0 0 0 2px #0f766e18}
    .input-with-unit input{width:100%;min-width:0;padding:8px 9px;border:0;outline:0;background:transparent;color:#0f172a;font:inherit;font-size:13px}
    .input-with-unit small{padding:0 8px;color:#94a3b8;font-size:10px;white-space:nowrap}
    .food-actions{display:flex;justify-content:space-between;align-items:center;gap:12px;margin-top:15px;padding-top:12px;border-top:1px solid #f1f5f9}
    .food-actions>div{display:flex;gap:6px}.hint{color:#94a3b8;font-size:10px}
    .state{display:flex;align-items:center;justify-content:center;gap:9px;padding:35px;color:#64748b}
    .state.error{color:#b91c1c}.state.empty{flex-direction:column;text-align:center}.state.empty mat-icon{font-size:32px;width:32px;height:32px;color:#0f766e}
    @media(max-width:900px){.nutrient-grid{grid-template-columns:repeat(3,minmax(0,1fr))}}
    @media(max-width:650px){.quality-page{padding:18px 13px 35px}.page-header{flex-direction:column}.page-header h1{font-size:23px}.summary{flex-wrap:wrap}.summary small{width:100%;margin:0 0 0 55px}.nutrient-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.food-heading{flex-direction:column}.food-actions{align-items:flex-start;flex-direction:column}.food-actions>div{width:100%;justify-content:flex-end}.hint{line-height:1.4}}
  `]
})
export class AdminFoodDataQualityComponent implements OnInit {
  readonly fields: NutrientField[] = [
    { key: 'kcal', label: 'Energía', unit: 'kcal', dtoKey: 'kcal' },
    { key: 'protein', label: 'Proteínas', unit: 'g', dtoKey: 'protein' },
    { key: 'carbs', label: 'Hidratos', unit: 'g', dtoKey: 'carbs' },
    { key: 'fat', label: 'Grasas totales', unit: 'g', dtoKey: 'fat' },
    { key: 'sugar', label: 'Azúcares', unit: 'g', dtoKey: 'sugar' },
    { key: 'saturated_fat', label: 'Saturadas', unit: 'g', dtoKey: 'saturatedFat' },
    { key: 'monounsaturated_fat', label: 'Monoinsaturadas', unit: 'g', dtoKey: 'monounsaturatedFat' },
    { key: 'polyunsaturated_fat', label: 'Poliinsaturadas', unit: 'g', dtoKey: 'polyunsaturatedFat' },
    { key: 'fiber', label: 'Fibra', unit: 'g', dtoKey: 'fiber' },
    { key: 'salt', label: 'Sal', unit: 'g', dtoKey: 'salt' },
    { key: 'vitamin_a_ug', label: 'Vitamina A', unit: 'µg', dtoKey: 'vitaminAUg' },
    { key: 'vitamin_c_mg', label: 'Vitamina C', unit: 'mg', dtoKey: 'vitaminCMg' },
    { key: 'vitamin_d_ug', label: 'Vitamina D', unit: 'µg', dtoKey: 'vitaminDUg' },
    { key: 'vitamin_e_mg', label: 'Vitamina E', unit: 'mg', dtoKey: 'vitaminEMg' },
    { key: 'vitamin_b12_ug', label: 'Vitamina B12', unit: 'µg', dtoKey: 'vitaminB12Ug' },
    { key: 'folate_ug', label: 'Folato', unit: 'µg', dtoKey: 'folateUg' },
    { key: 'calcium_mg', label: 'Calcio', unit: 'mg', dtoKey: 'calciumMg' },
    { key: 'iron_mg', label: 'Hierro', unit: 'mg', dtoKey: 'ironMg' },
    { key: 'magnesium_mg', label: 'Magnesio', unit: 'mg', dtoKey: 'magnesiumMg' },
    { key: 'potassium_mg', label: 'Potasio', unit: 'mg', dtoKey: 'potassiumMg' },
    { key: 'zinc_mg', label: 'Zinc', unit: 'mg', dtoKey: 'zincMg' }
  ];

  items: FoodDataQualityItem[] = [];
  search = '';
  loading = false;
  error = '';
  savingIds = new Set<number>();

  constructor(
    private http: HttpClient,
    private snackBar: MatSnackBar,
    private changeDetector: ChangeDetectorRef
  ) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading = true;
    this.error = '';
    let params = new HttpParams().set('limit', '150');
    if (this.search.trim()) params = params.set('search', this.search.trim());
    this.http.get<FoodDataQualityItem[]>('/api/foods/admin/data-quality', { params }).subscribe({
      next: items => {
        this.items = items || [];
        this.loading = false;
        // Force this view to refresh when the HTTP callback is delivered outside
        // the normal Angular change-detection turn (e.g. after lazy navigation).
        this.changeDetector.detectChanges();
      },
      error: err => {
        this.loading = false;
        this.error = err?.error?.detail || err?.error || 'No se pudo cargar la revisión de alimentos.';
        this.changeDetector.detectChanges();
      }
    });
  }

  getMissingFields(item: FoodDataQualityItem): NutrientField[] {
    return this.fields.filter(field => item.missingFields.includes(field.key));
  }

  hasValuesToSave(item: FoodDataQualityItem): boolean {
    return this.getMissingFields(item).some(field => item[field.key] !== null && item[field.key] !== undefined && item[field.key] !== '');
  }

  save(item: FoodDataQualityItem): void {
    const payload: Record<string, number> = {};
    for (const field of this.getMissingFields(item)) {
      const value = item[field.key];
      if (value !== null && value !== undefined && value !== '' && Number.isFinite(Number(value)) && Number(value) >= 0) {
        payload[field.dtoKey] = Number(value);
      }
    }
    if (!Object.keys(payload).length) return;

    this.savingIds.add(item.id);
    this.http.put<void>(`/api/foods/admin/data-quality/${item.id}`, payload).subscribe({
      next: () => {
        this.savingIds.delete(item.id);
        this.changeDetector.detectChanges();
        this.snackBar.open('Datos nutricionales guardados.', 'Cerrar', { duration: 2500 });
        this.load();
      },
      error: () => {
        this.savingIds.delete(item.id);
        this.changeDetector.detectChanges();
        this.snackBar.open('No se pudieron guardar los datos del alimento.', 'Cerrar', { duration: 3500 });
      }
    });
  }

  ignore(item: FoodDataQualityItem): void {
    this.savingIds.add(item.id);
    this.http.post<void>(`/api/foods/admin/data-quality/${item.id}/ignore`, {}).subscribe({
      next: () => {
        this.savingIds.delete(item.id);
        this.items = this.items.filter(food => food.id !== item.id);
        this.changeDetector.detectChanges();
        this.snackBar.open('Alimento oculto durante 30 días.', 'Cerrar', { duration: 2500 });
      },
      error: () => {
        this.savingIds.delete(item.id);
        this.changeDetector.detectChanges();
        this.snackBar.open('No se pudo ignorar el alimento.', 'Cerrar', { duration: 3500 });
      }
    });
  }

  trackById(_index: number, item: FoodDataQualityItem): number { return item.id; }
}
