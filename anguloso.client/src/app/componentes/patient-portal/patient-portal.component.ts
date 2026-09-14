import { Component, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ClientService } from '../../servicios/client.service';
import { DietService } from '../../servicios/diet.service';
import { FoodService } from '../../servicios/food.service';
import { SumPipe } from '../../shared/pipes/sum.pipe';

type ActiveTab = 'today' | 'shopping' | 'progress';

@Component({
  selector: 'app-patient-portal',
  standalone: true,
  imports: [CommonModule, DatePipe, DecimalPipe, MatIconModule, MatProgressSpinnerModule, SumPipe],
  templateUrl: './patient-portal.component.html',
  styleUrls: ['./patient-portal.component.css']
})
export class PatientPortalComponent implements OnInit {
  clientId!: number;
  profile: any = null;
  activeDiet: any = null;
  shoppingList: any[] = [];
  loading = true;
  activeTab: ActiveTab = 'today';
  today = new Date();

  // Equivalencias de intercambios expandidas
  expandedExchangeId: string | null = null;
  exchangeFoodsCache: Record<number, any[]> = {};
  exchangeLoading = false;

  // Shopping list: persisted state via localStorage
  checkedItems: Record<string, boolean> = {};

  constructor(
    private route: ActivatedRoute,
    private clientService: ClientService,
    private dietService: DietService,
    private foodService: FoodService
  ) {}

  ngOnInit(): void {
    this.clientId = +this.route.snapshot.paramMap.get('clientId')!;
    this.loadData();
    this.loadCheckedItems();
  }

  loadData(): void {
    this.loading = true;
    this.clientService.getPatientProfile(this.clientId).subscribe({
      next: (p) => {
        this.profile = p;
        if (p.hasActiveDiet) {
          this.loadActiveDiet();
          this.loadShoppingList();
        } else {
          this.loading = false;
        }
      },
      error: () => { this.loading = false; }
    });
  }

  loadActiveDiet(): void {
    this.dietService.getActiveDiet(this.clientId).subscribe({
      next: (d) => {
        this.activeDiet = d;
        this.loading = false;
      },
      error: () => { this.loading = false; }
    });
  }

  loadShoppingList(): void {
    this.clientService.getActiveShoppingList(this.clientId).subscribe({
      next: (s) => { this.shoppingList = s; },
      error: () => {}
    });
  }

  setTab(tab: ActiveTab): void {
    this.activeTab = tab;
  }

  // Devuelve el día de la dieta que corresponde al día de la semana actual
  get todayDayData(): any | null {
    if (!this.activeDiet?.days?.length) return null;
    const dayOfWeek = this.today.getDay(); // 0=Dom, 1=Lun...
    const idx = dayOfWeek === 0 ? 6 : dayOfWeek - 1; // Ajustar a Lunes=0
    const day = this.activeDiet.days.find((d: any) => d.dayIndex === idx);
    return day ?? this.activeDiet.days[0]; // fallback al primer día
  }

  get dayName(): string {
    const names = ['Lunes','Martes','Miércoles','Jueves','Viernes','Sábado','Domingo'];
    const dow = this.today.getDay();
    return names[dow === 0 ? 6 : dow - 1];
  }

  getMealIcon(mealName: string): string {
    const n = mealName.toLowerCase();
    if (n.includes('desayuno')) return 'free_breakfast';
    if (n.includes('almuerzo') || n.includes('media')) return 'lunch_dining';
    if (n.includes('comida')) return 'restaurant';
    if (n.includes('merienda')) return 'cookie';
    if (n.includes('cena')) return 'dinner_dining';
    return 'food_bank';
  }

  getShoppingIcon(category: string): string {
    const c = (category ?? '').toLowerCase();
    if (c.includes('fruta') || c.includes('verdura') || c.includes('vegetal')) return 'eco';
    if (c.includes('carne') || c.includes('pescado') || c.includes('proteína')) return 'set_meal';
    if (c.includes('lácteo') || c.includes('lacteo') || c.includes('leche')) return 'local_cafe';
    if (c.includes('cereal') || c.includes('harina') || c.includes('pan')) return 'grain';
    return 'shopping_basket';
  }

  toggleItem(key: string): void {
    this.checkedItems[key] = !this.checkedItems[key];
    this.saveCheckedItems();
  }

  isChecked(key: string): boolean {
    return !!this.checkedItems[key];
  }

  private loadCheckedItems(): void {
    const saved = localStorage.getItem(`shopping_${this.clientId}`);
    if (saved) {
      try { this.checkedItems = JSON.parse(saved); } catch {}
    }
  }

  private saveCheckedItems(): void {
    localStorage.setItem(`shopping_${this.clientId}`, JSON.stringify(this.checkedItems));
  }

  get bmi(): string | null {
    if (!this.profile?.currentWeight || !this.profile?.currentHeight) return null;
    const bmi = this.profile.currentWeight / Math.pow(this.profile.currentHeight / 100, 2);
    return bmi.toFixed(1);
  }

  get bmiCategory(): string {
    const b = parseFloat(this.bmi ?? '0');
    if (b < 18.5) return 'Bajo peso';
    if (b < 25) return 'Normopeso';
    if (b < 30) return 'Sobrepeso';
    return 'Obesidad';
  }

  get bmiColor(): string {
    const b = parseFloat(this.bmi ?? '0');
    if (b < 18.5) return '#3B82F6';
    if (b < 25) return '#22C55E';
    if (b < 30) return '#F59E0B';
    return '#EF4444';
  }

  getWeightBarHeight(weight: number, history: any[]): number {
    if (!history?.length) return 0;
    const min = Math.min(...history.map((h: any) => h.weight));
    const max = Math.max(...history.map((h: any) => h.weight));
    if (max === min) return 60;
    return Math.round(((weight - min) / (max - min)) * 85 + 15);
  }

  toggleExchangeEquivalencies(uniqueKey: string, groupId?: number): void {
    if (this.expandedExchangeId === uniqueKey) {
      this.expandedExchangeId = null;
      return;
    }

    this.expandedExchangeId = uniqueKey;

    if (groupId && !this.exchangeFoodsCache[groupId]) {
      this.exchangeLoading = true;
      this.foodService.getFoodsInExchangeGroup(groupId).subscribe({
        next: (foods) => {
          this.exchangeFoodsCache[groupId] = foods || [];
          this.exchangeLoading = false;
        },
        error: () => {
          this.exchangeLoading = false;
        }
      });
    }
  }

  getExchangeFoods(groupId?: number): any[] {
    return groupId ? (this.exchangeFoodsCache[groupId] || []) : [];
  }
}
