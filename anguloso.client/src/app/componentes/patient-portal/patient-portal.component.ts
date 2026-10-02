import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PatientPortalService } from '../../servicios/patient-portal.service';
import { FoodService } from '../../servicios/food.service';
import { SumPipe } from '../../shared/pipes/sum.pipe';
import { PatientCheckin, PatientCheckinRequest } from '../../servicios/patient-portal.service';

type ActiveTab = 'today' | 'shopping' | 'progress';

@Component({
  selector: 'app-patient-portal',
  standalone: true,
  imports: [CommonModule, FormsModule, DatePipe, DecimalPipe, MatIconModule, MatProgressSpinnerModule, SumPipe],
  templateUrl: './patient-portal.component.html',
  styleUrls: ['./patient-portal.component.css']
})
export class PatientPortalComponent implements OnInit {
  clientId?: number;
  profile: any = null;
  activeDiet: any = null;
  shoppingList: any[] = [];
  loading = true;
  authError: string | null = null;
  showLogin = false;
  portalDataError: string | null = null;
  dietDataError: string | null = null;
  shoppingDataError: string | null = null;
  dietLoading = false;
  shoppingLoading = false;

  // Login form model (PIN/phone)
  emailOrPhone = '';
  passcode = '';
  submittingLogin = false;
  accessLinkEmail = '';
  requestingAccessLink = false;
  accessLinkMessage: string | null = null;
  accessLinkError: string | null = null;

  activeTab: ActiveTab = 'today';
  today = new Date();

  // Equivalencias de intercambios expandidas
  expandedExchangeId: string | null = null;
  exchangeFoodsCache: Record<number, any[]> = {};
  exchangeLoading = false;

  // Shopping list: persisted state via localStorage
  checkedItems: Record<string, boolean> = {};
  completedMeals: Record<string, boolean> = {};

  // Revisión semanal persistente en backend
  currentCheckin: PatientCheckin | null = null;
  checkinHistory: PatientCheckin[] = [];
  checkinLoading = false;
  checkinSaving = false;
  checkinError: string | null = null;
  checkinSuccess = false;
  checkinWeight: number | null = null;
  checkinAdherence: number | null = null;
  checkinHunger: number | null = null;
  checkinDifficulties = '';
  checkinNotes = '';

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private portalService: PatientPortalService,
    private foodService: FoodService
  ) {}

  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      const token = params['token'];
      const clientIdParam = params['clientId'] ? +params['clientId'] : undefined;

      if (token) {
        this.authenticateWithToken(token);
      } else if (clientIdParam) {
        // Modo vista previa nutricionista si hay sesión iniciada
        this.clientId = clientIdParam;
        this.loadData(clientIdParam);
      } else {
        this.loading = false;
        this.showLogin = true;
      }
    });

  }

  authenticateWithToken(token: string): void {
    this.loading = true;
    this.authError = null;
    this.portalService.authenticate({ token }).subscribe({
      next: (res) => {
        this.clientId = res.clientId;
        this.showLogin = false;
        // El token mágico no debe permanecer en el historial del navegador.
        this.router.navigate([], { queryParams: {}, replaceUrl: true });
        this.loadData();
      },
      error: (err) => {
        this.loading = false;
        this.authError = err?.error?.message || 'El enlace de acceso ha expirado o no es válido.';
        this.showLogin = true;
      }
    });
  }

  requestNewAccessLink(): void {
    const email = this.accessLinkEmail.trim().toLowerCase();
    this.accessLinkMessage = null;
    this.accessLinkError = null;
    if (!email || !email.includes('@')) {
      this.accessLinkError = 'Introduce el email con el que estás registrado.';
      return;
    }

    this.requestingAccessLink = true;
    this.portalService.requestAccessLink(email).subscribe({
      next: (response) => {
        this.requestingAccessLink = false;
        this.accessLinkMessage = response?.message || 'Si el email corresponde a un paciente, recibirás un nuevo enlace de acceso.';
      },
      error: (err) => {
        this.requestingAccessLink = false;
        this.accessLinkError = err?.error?.message || 'No hemos podido enviar el enlace. Inténtalo de nuevo más tarde.';
      }
    });
  }

  submitPinLogin(): void {
    if (!this.emailOrPhone || !this.passcode) {
      this.authError = 'Por favor, introduce tu email/teléfono y tu PIN.';
      return;
    }

    this.submittingLogin = true;
    this.authError = null;
    this.portalService.authenticate({
      emailOrPhone: this.emailOrPhone,
      passcode: this.passcode
    }).subscribe({
      next: (res) => {
        this.clientId = res.clientId;
        this.submittingLogin = false;
        this.showLogin = false;
        this.loadData();
      },
      error: (err) => {
        this.submittingLogin = false;
        this.authError = err?.error?.message || 'Credenciales incorrectas.';
      }
    });
  }

  logoutPatient(): void {
    this.portalService.clearPatientSession().subscribe({
      next: () => this.resetPatientView(),
      error: () => this.resetPatientView()
    });
  }

  private resetPatientView(): void {
    this.profile = null;
    this.activeDiet = null;
    this.shoppingList = [];
    this.dietDataError = null;
    this.shoppingDataError = null;
    this.dietLoading = false;
    this.shoppingLoading = false;
    this.portalDataError = null;
    this.currentCheckin = null;
    this.checkinHistory = [];
    this.checkinLoading = false;
    this.checkinSaving = false;
    this.checkinError = null;
    this.checkinSuccess = false;
    this.showLogin = true;
  }

  loadData(clientIdParam?: number): void {
    this.loading = true;
    this.portalService.getMyProfile(clientIdParam).subscribe({
      next: (p) => {
        this.profile = p;
        this.portalDataError = null;
        if (p.id) this.clientId = p.id;
        this.loadCheckedItems();
        this.loadCompletedMeals();
        // La vista previa del profesional usa clientId y no tiene sesión de paciente.
        if (!clientIdParam) {
          this.loadCurrentCheckin();
          this.loadCheckinHistory();
        }
        if (p.hasActiveDiet) {
          this.loadActiveDiet(clientIdParam);
          this.loadShoppingList(clientIdParam);
        } else {
          this.activeDiet = null;
          this.shoppingList = [];
          this.loading = false;
        }
      },
      error: (err) => {
        this.loading = false;
        this.portalDataError = err?.error?.message || 'No hemos podido cargar tus datos. Inténtalo de nuevo.';
        this.showLogin = false;
      }
    });
  }

  retryLoadData(): void {
    this.portalDataError = null;
    this.loadData(this.clientId);
  }

  loadActiveDiet(clientIdParam?: number): void {
    this.dietLoading = true;
    this.dietDataError = null;
    this.portalService.getMyActiveDiet(clientIdParam).subscribe({
      next: (d) => {
        this.activeDiet = d;
        this.dietLoading = false;
        this.loading = false;
      },
      error: (err) => {
        this.activeDiet = null;
        this.dietLoading = false;
        this.loading = false;
        this.dietDataError = err?.error?.message || 'No hemos podido cargar tu plan alimentario.';
      }
    });
  }

  loadShoppingList(clientIdParam?: number): void {
    this.shoppingLoading = true;
    this.shoppingDataError = null;
    this.portalService.getMyShoppingList(clientIdParam).subscribe({
      next: (s) => {
        this.shoppingList = s || [];
        this.shoppingLoading = false;
      },
      error: (err) => {
        this.shoppingList = [];
        this.shoppingLoading = false;
        this.shoppingDataError = err?.error?.message || 'No hemos podido cargar tu lista de la compra.';
      }
    });
  }

  loadCurrentCheckin(): void {
    this.checkinLoading = true;
    this.checkinError = null;
    this.checkinSuccess = false;
    this.portalService.getCurrentCheckin().subscribe({
      next: (checkin) => {
        this.currentCheckin = checkin;
        this.checkinLoading = false;
        if (checkin) {
          this.checkinWeight = checkin.weight ?? null;
          this.checkinAdherence = checkin.adherence ?? null;
          this.checkinHunger = checkin.hunger ?? null;
          this.checkinDifficulties = checkin.difficulties ?? '';
          this.checkinNotes = checkin.notes ?? '';
        }
      },
      error: (err) => {
        this.currentCheckin = null;
        this.checkinLoading = false;
        this.checkinError = err?.error?.message || 'No hemos podido cargar tu revisión semanal.';
      }
    });
  }

  loadCheckinHistory(): void {
    this.portalService.getCheckinHistory().subscribe({
      next: (history) => this.checkinHistory = history || [],
      error: () => this.checkinHistory = []
    });
  }

  saveCurrentCheckin(): void {
    this.checkinSaving = true;
    this.checkinError = null;
    this.checkinSuccess = false;

    const request: PatientCheckinRequest = {
      weight: this.checkinWeight,
      adherence: this.checkinAdherence,
      hunger: this.checkinHunger,
      difficulties: this.checkinDifficulties.trim() || null,
      notes: this.checkinNotes.trim() || null
    };

    this.portalService.saveCheckin(request).subscribe({
      next: (checkin) => {
        this.currentCheckin = checkin;
        this.checkinSaving = false;
        this.checkinSuccess = true;
        this.checkinWeight = checkin.weight ?? null;
        this.checkinAdherence = checkin.adherence ?? null;
        this.checkinHunger = checkin.hunger ?? null;
        this.checkinDifficulties = checkin.difficulties ?? '';
        this.checkinNotes = checkin.notes ?? '';
      },
      error: (err) => {
        this.checkinSaving = false;
        this.checkinError = err?.error?.message || 'No hemos podido guardar tu revisión semanal. Inténtalo de nuevo.';
      }
    });
  }

  retryDietLoad(): void {
    this.loadActiveDiet(this.clientId);
  }

  retryShoppingLoad(): void {
    this.loadShoppingList(this.clientId);
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

  get shoppingItemCount(): number {
    return this.shoppingList.reduce(
      (total: number, category: any) => total + (category.items?.length ?? 0),
      0
    );
  }

  get checkedShoppingItemCount(): number {
    return this.shoppingList.reduce(
      (total: number, category: any) => total + (category.items?.filter((item: any) =>
        this.isChecked(category.category + '_' + item.foodId)
      ).length ?? 0),
      0
    );
  }

  get shoppingProgressPercent(): number {
    return this.shoppingItemCount
      ? Math.round((this.checkedShoppingItemCount / this.shoppingItemCount) * 100)
      : 0;
  }

  clearShoppingChecks(): void {
    this.checkedItems = {};
    this.saveCheckedItems();
  }

  mealKey(meal: any): string {
    return `${this.clientId ?? 'preview'}_${this.today.toISOString().slice(0, 10)}_${meal.mealIndex}`;
  }

  isMealCompleted(meal: any): boolean {
    return !!this.completedMeals[this.mealKey(meal)];
  }

  toggleMealCompleted(meal: any): void {
    const key = this.mealKey(meal);
    this.completedMeals[key] = !this.completedMeals[key];
    this.saveCompletedMeals();
  }

  get completedMealCount(): number {
    return this.todayDayData?.meals?.filter((meal: any) => this.isMealCompleted(meal)).length ?? 0;
  }

  get todayMealCount(): number {
    return this.todayDayData?.meals?.length ?? 0;
  }

  get todayProgressPercent(): number {
    return this.todayMealCount ? Math.round((this.completedMealCount / this.todayMealCount) * 100) : 0;
  }

  get todayHasMeals(): boolean {
    return (this.todayDayData?.meals?.length ?? 0) > 0;
  }

  get todayKcal(): number {
    return this.todayDayData?.meals?.reduce((total: number, meal: any) =>
      total + (meal.items?.reduce((mealTotal: number, item: any) => mealTotal + Number(item.kcal || 0), 0) || 0), 0) ?? 0;
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

  private loadCompletedMeals(): void {
    const saved = localStorage.getItem(`completed_meals_${this.clientId}_${this.today.toISOString().slice(0, 10)}`);
    if (saved) {
      try { this.completedMeals = JSON.parse(saved); } catch { this.completedMeals = {}; }
    }
  }

  private saveCompletedMeals(): void {
    localStorage.setItem(
      `completed_meals_${this.clientId}_${this.today.toISOString().slice(0, 10)}`,
      JSON.stringify(this.completedMeals)
    );
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

  get weightHistory(): any[] {
    return this.profile?.weightHistory ?? [];
  }

  get firstRecordedWeight(): number | null {
    return this.weightHistory.length ? Number(this.weightHistory[0].weight) : null;
  }

  get latestRecordedWeight(): number | null {
    return this.weightHistory.length ? Number(this.weightHistory[this.weightHistory.length - 1].weight) : null;
  }

  get weightChange(): number | null {
    if (this.firstRecordedWeight === null || this.latestRecordedWeight === null || this.weightHistory.length < 2) return null;
    return Number((this.latestRecordedWeight - this.firstRecordedWeight).toFixed(1));
  }

  get weightChangeLabel(): string {
    return this.weightChange === null ? 'Sin historial suficiente' : 'Desde el primer registro';
  }

  get weightChangeIcon(): string {
    if (this.weightChange === null || this.weightChange === 0) return 'horizontal_rule';
    return this.weightChange < 0 ? 'south_east' : 'north_east';
  }

  get weightChangeText(): string {
    if (this.weightChange === null) return '—';
    if (this.weightChange === 0) return '0 kg';
    return Math.abs(this.weightChange).toFixed(1) + ' kg';
  }

  formatWeightDate(dateValue: string | Date): string {
    const date = new Date(dateValue);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat('es-ES', { day: '2-digit', month: 'short' }).format(date);
  }

  getWeightBarHeight(weight: number, history: any[]): number {
    if (!history?.length) return 0;
    const min = Math.min(...history.map((h: any) => h.weight));
    const max = Math.max(...history.map((h: any) => h.weight));
    if (max === min) return 60;
    return Math.round(((weight - min) / (max - min)) * 85 + 15);
  }

  get hasShoppingItems(): boolean {
    return this.shoppingItemCount > 0;
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
