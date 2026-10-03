import { Component, OnInit, OnDestroy } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ClientService } from '../../servicios/client.service';
import { FoodService } from '../../servicios/food.service';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ClientDetail, Biometric, ClientDiet } from '../../modelos/client';
import { FoodInExchangeGroup } from '../../modelos/food-exchange-group';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatTabChangeEvent } from '@angular/material/tabs';
import Chart from 'chart.js/auto';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { RouterLink } from '@angular/router';
import { DietSelectDialogComponent } from './diet-select-dialog.component';
import { BioimpedanceImportDialogComponent } from './bioimpedance-import-dialog.component';

// Angular Material
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCardModule } from '@angular/material/card';
import { PatientCheckin, PatientPortalService, ClientPortalAccess } from '../../servicios/patient-portal.service';
import { Subscription } from 'rxjs';
import { debounceTime, filter, switchMap } from 'rxjs/operators';

@Component({
  selector: 'app-client-detail',
  templateUrl: './client-detail.component.html',
  styleUrls: ['./client-detail.component.css'],

  // ⬇⬇⬇ AQUÍ IMPORTAS TODO LO QUE LA PLANTILLA NECESITE ⬇⬇⬇
  imports: [
    CommonModule,
    ReactiveFormsModule,
    FormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatDatepickerModule,
    MatSelectModule,
    MatTabsModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
    MatCheckboxModule,
    MatProgressSpinnerModule,
    MatCardModule,
    MatDialogModule,
    RouterLink
  ],
  standalone: true
})
export class ClientDetailComponent implements OnInit, OnDestroy {
  clientId?: number;
  client?: ClientDetail;
  loading = false;
  clientSaveState: 'idle' | 'saving' | 'saved' | 'error' = 'idle';
  private clientAutosaveSubscription?: Subscription;
  private biometricAutosaveSubscription?: Subscription;
  biometricSaveState: 'idle' | 'saving' | 'saved' | 'error' = 'idle';

  clientForm!: FormGroup;
  biometrics: Biometric[] = [];
  showBiometricForm = false;
  biometricForm!: FormGroup;
  editingBiometricId: number | null = null;

  evolutionData: Biometric[] = [];
  selectedMetric = 'weight';
  chart: Chart | null = null;
  weightChart: Chart | null = null;
  bodyFatChart: Chart | null = null;
  checkinChart: Chart | null = null;
  checkins: PatientCheckin[] = [];
  selectedFollowupMetric = 'adherence';
  dietsHistory: ClientDiet[] = [];
  energyReq: any = null;
  selectedActivity = 'Moderado';
  energyErrorMessage = '';

  // Análisis antropométrico
  selectedAnalysisBiometricId: number | null = null;
  showAdvancedSkinfolds = false;

  // Exchange group interactive viewer: itemKey -> { foods, selectedFoodId }
  exchangeFoodsCache: { [key: string]: FoodInExchangeGroup[] } = {};
  exchangeSelectedFood: { [key: string]: number | null } = {};

  // Portal del Paciente
  portalAccess: ClientPortalAccess | null = null;
  loadingPortalAccess = false;
  regeneratingToken = false;
  settingPasscode = false;
  newPasscode = '';
  communicationPreferences = { inAppEnabled: true, emailEnabled: true, pushEnabled: true };
  savingCommunicationPreferences = false;

  constructor(
    private fb: FormBuilder,
    private svc: ClientService,
    private foodService: FoodService,
    private portalService: PatientPortalService,
    private route: ActivatedRoute,
    private router: Router,
    private snack: MatSnackBar,
    private dialog: MatDialog
  ) { }

  // Inicializa el formulario y los autosaves; las cargas independientes se lanzan después para que una sección no bloquee las demás.
  ngOnInit(): void {
    this.clientId = Number(this.route.snapshot.paramMap.get('id'));
    this.buildForms();
    this.clientAutosaveSubscription = this.clientForm.valueChanges.pipe(
      debounceTime(800),
      filter(() => !!this.clientId && this.clientForm.valid),
      switchMap(() => {
        this.clientSaveState = 'saving';
        return this.svc.updateClient(this.clientId!, this.getClientPayload());
      })
    ).subscribe({
      next: () => this.clientSaveState = 'saved',
      error: () => this.clientSaveState = 'error'
    });

    if (this.clientId) {
      this.loadClient();
      this.loadBiometrics();
      this.loadDietsHistory();
      this.loadEnergyRequirements();
      this.loadPortalAccess();
      this.loadCommunicationPreferences();
      this.loadPatientCheckins();
    }
  }

  // Los dos formularios comparten el mismo patrón: cambios válidos se persisten con debounce para evitar una petición por pulsación.
  buildForms() {
    this.clientForm = this.fb.group({
      fullName: ['', Validators.required],
      email: [''],
      phone: [''],
      birthDate: [''],
      gender: [''],
      notes: [''],
      medicalHistory: this.fb.group({
        diabetes: [false],
        hypertension: [false],
        hypothyroidism: [false],
        surgeries: [''],
        routineMedication: [''],
        otherPathologies: ['']
      }),
      digestiveHealth: this.fb.group({
        intestinalHabits: [''],
        bloating: [false],
        heartburn: [false],
        glutenIntolerance: [false],
        lactoseIntolerance: [false],
        fodmapsIntolerance: [false],
        otherIntolerances: [''],
        notes: ['']
      }),
      foodPreferences: this.fb.group({
        preferredFoods: [''],
        dislikedFoods: [''],
        allergies: ['']
      }),
      lifestyleHistory: this.fb.group({
        workSchedule: [''],
        sleepHabits: [''],
        waterConsumption: [''],
        alcoholConsumption: [''],
        tobaccoConsumption: ['']
      })
    });

    this.biometricForm = this.fb.group({
      measurementDate: [new Date().toISOString().slice(0, 10), Validators.required],
      weight: [null],
      height: [null],
      bodyFat: [null],
      muscleMass: [null],
      visceralFat: [null],
      waist: [null],
      hip: [null],
      neck: [null],
      // Pliegues básicos (mm)
      triceps: [null],
      abdomen: [null],
      thigh: [null],
      subscapular: [null],
      suprailiac: [null],
      // Pliegues avanzados (mm)
      biceps: [null],
      chest: [null],
      axilla: [null],
      calfSkinfold: [null],
      // Perímetros (cm)
      armPerimeter: [null],
      calfPerimeter: [null],
      // Diámetros óseos (cm)
      wristDiameter: [null],
      femurDiameter: [null],
      humerusDiameter: [null],
      notes: ['']
    });

    this.biometricAutosaveSubscription = this.biometricForm.valueChanges.pipe(
      debounceTime(800),
      filter(() => !!this.clientId && !!this.editingBiometricId && this.biometricForm.valid),
      switchMap(() => {
        this.biometricSaveState = 'saving';
        return this.svc.updateBiometric(this.clientId!, this.editingBiometricId!, { ...this.biometricForm.value });
      })
    ).subscribe({
      next: () => {
        this.biometricSaveState = 'saved';
        this.loadBiometrics();
        this.loadEvolution();
        this.loadEnergyRequirements();
      },
      error: () => this.biometricSaveState = 'error'
    });
  }

  /** Normaliza una fecha ISO/DateTime a yyyy-MM-dd para inputs type="date". */
  toDateInputValue(value?: string | null): string {
    if (!value) return '';
    return value.slice(0, 10);
  }

  /** Muestra fechas que conceptualmente son solo fecha, sin hora. */
  formatDateOnly(value?: string | null): string {
    const date = this.toDateInputValue(value);
    if (!date) return '';
    const parts = date.split('-');
    return parts[2] + '/' + parts[1] + '/' + parts[0];
  }
  private normalizeGender(value?: string | null): string {
    const normalized = (value || '').trim().toLowerCase();
    return ['male', 'female', 'other'].includes(normalized) ? normalized : '';
  }

  loadCommunicationPreferences(): void {
    if (!this.clientId) return;
    this.portalService.getCommunicationPreferences(this.clientId).subscribe({
      next: preferences => this.communicationPreferences = preferences,
      error: () => this.snack.open('No se han podido cargar las preferencias de comunicación', 'Cerrar', { duration: 3000 })
    });
  }

  updateCommunicationPreferences(): void {
    if (!this.clientId) return;
    this.savingCommunicationPreferences = true;
    this.portalService.updateCommunicationPreferences(this.clientId, this.communicationPreferences).subscribe({
      next: () => {
        this.savingCommunicationPreferences = false;
        this.snack.open('Preferencias de comunicación guardadas', 'Cerrar', { duration: 1800 });
      },
      error: () => {
        this.savingCommunicationPreferences = false;
        this.snack.open('No se han podido guardar las preferencias', 'Cerrar', { duration: 3000 });
      }
    });
  }

  loadClient() {
    this.svc.getClient(this.clientId!).subscribe({
      next: (c) => {
        this.client = c;
        this.clientForm.patchValue({
          fullName: c.fullName,
          email: c.email,
          phone: c.phone,
          birthDate: this.toDateInputValue(c.birthDate),
          gender: this.normalizeGender(c.gender),
          notes: c.notes,
          medicalHistory: c.medicalHistory || {},
          digestiveHealth: c.digestiveHealth || {},
          foodPreferences: c.foodPreferences || {},
          lifestyleHistory: c.lifestyleHistory || {}
        }, { emitEvent: false });
        this.clientSaveState = 'saved';
        this.biometrics = c.biometrics || [];
      },
      error: () => this.snack.open('Error cargando cliente', 'Cerrar', { duration: 3000 })
    });
  }

  loadBiometrics() {
    if (!this.clientId) return;
    this.svc.getBiometrics(this.clientId).subscribe({
      next: (b) => {
        this.biometrics = b;
        // Auto-seleccionar el primer registro para el panel de análisis
        if (!this.selectedAnalysisBiometricId && b.length > 0) {
          this.selectedAnalysisBiometricId = b[0].id ?? null;
        }
      },
      error: () => this.snack.open('Error cargando biometrías', 'Cerrar', { duration: 3000 })
    });
  }

  private getClientPayload() {
    return {
      fullName: this.clientForm.value.fullName,
      email: this.clientForm.value.email,
      phone: this.clientForm.value.phone,
      birthDate: this.clientForm.value.birthDate,
      gender: this.clientForm.value.gender,
      notes: this.clientForm.value.notes,
      medicalHistory: this.clientForm.value.medicalHistory,
      digestiveHealth: this.clientForm.value.digestiveHealth,
      foodPreferences: this.clientForm.value.foodPreferences,
      lifestyleHistory: this.clientForm.value.lifestyleHistory
    };
  }

  saveClient() {
    const payload = this.getClientPayload();

    if (this.clientId) {
      this.clientSaveState = 'saving';
      this.svc.updateClient(this.clientId, payload).subscribe({
        next: () => this.clientSaveState = 'saved',
        error: () => this.clientSaveState = 'error'
      });
    } else {
      this.svc.createClient(payload).subscribe({
        next: (res: any) => {
          const newId = res?.id;
          this.snack.open('Cliente creado', 'Cerrar', { duration: 2000 });
          if (newId) this.router.navigate(['/clients', newId]);
        },
        error: () => this.snack.open('Error al crear', 'Cerrar', { duration: 3000 })
      });
    }
  }

  // Biometrics
  openNewBiometric() {
    this.showBiometricForm = true;
    this.editingBiometricId = null;
    this.biometricForm.reset({ measurementDate: new Date().toISOString().slice(0, 10) });
  }

  openBioimpedanceImport() {
    if (!this.clientId) return;
    const ref = this.dialog.open(BioimpedanceImportDialogComponent, {
      width: '900px',
      maxWidth: '95vw',
      data: {
        clientId: this.clientId,
        clientName: this.client?.fullName || 'Paciente'
      }
    });

    ref.afterClosed().subscribe(success => {
      if (success) {
        this.loadBiometrics();
        this.loadEvolution();
        this.loadEnergyRequirements();
      }
    });
  }

  editBiometric(b: Biometric) {
    this.showBiometricForm = true;
    this.editingBiometricId = b.id ?? null;
    this.biometricForm.patchValue({
      measurementDate: b.measurementDate,
      weight: b.weight,
      height: b.height,
      bodyFat: b.bodyFat,
      muscleMass: b.muscleMass,
      visceralFat: b.visceralFat,
      waist: b.waist,
      hip: b.hip,
      neck: b.neck,
      triceps: b.triceps,
      abdomen: b.abdomen,
      thigh: b.thigh,
      subscapular: b.subscapular,
      suprailiac: b.suprailiac,
      biceps: b.biceps,
      chest: b.chest,
      axilla: b.axilla,
      calfSkinfold: b.calfSkinfold,
      armPerimeter: b.armPerimeter,
      calfPerimeter: b.calfPerimeter,
      wristDiameter: b.wristDiameter,
      femurDiameter: b.femurDiameter,
      humerusDiameter: b.humerusDiameter,
      notes: b.notes
    }, { emitEvent: false });
    this.biometricSaveState = 'saved';
  }

  saveBiometric() {
    if (!this.clientId) return;
    const payload = { ...this.biometricForm.value };

    if (this.editingBiometricId) {
      // Las ediciones existentes se guardan automáticamente con debounce.
      this.biometricSaveState = 'saved';
      return;
      /*
      this.svc.updateBiometric(this.clientId, this.editingBiometricId, payload).subscribe({
        next: () => {
          this.snack.open('Biometría actualizada', 'Cerrar', { duration: 2000 });
          this.loadBiometrics();
          this.loadEvolution();
          this.loadEnergyRequirements();
          this.showBiometricForm = false;
        },
        error: () => this.snack.open('Error actualizando biometría', 'Cerrar', { duration: 3000 })
      });
      */
    } else {
      this.svc.createBiometric(this.clientId, payload).subscribe({
        next: () => {
          this.snack.open('Biometría creada', 'Cerrar', { duration: 2000 });
          this.loadBiometrics();
          this.loadEvolution();
          this.loadEnergyRequirements();
          this.showBiometricForm = false;
        },
        error: () => this.snack.open('Error creando biometría', 'Cerrar', { duration: 3000 })
      });
    }
  }

  deleteBiometric(id?: number) {
    if (!id || !this.clientId) return;
    if (!confirm('Eliminar registro biométrico?')) return;
    this.svc.deleteBiometric(this.clientId, id).subscribe({
      next: () => {
        this.loadBiometrics();
        this.loadEvolution();
        this.loadEnergyRequirements();
        this.snack.open('Eliminado', 'Cerrar', { duration: 2000 });
      },
      error: () => this.snack.open('Error eliminando', 'Cerrar', { duration: 3000 })
    });
  }

  ngOnDestroy() {
    this.clientAutosaveSubscription?.unsubscribe();
    this.biometricAutosaveSubscription?.unsubscribe();
    if (this.chart) this.chart.destroy();
    if (this.weightChart) this.weightChart.destroy();
    if (this.bodyFatChart) this.bodyFatChart.destroy();
    if (this.checkinChart) this.checkinChart.destroy();
  }

  onTabChange(event: MatTabChangeEvent) {
    if (event.tab.textLabel === 'Evolución') {
      this.loadEvolution();
    } else if (event.tab.textLabel === 'Dietas') {
      this.loadDietsHistory();
    } else if (event.tab.textLabel === 'Cálculo de Calorías') {
      this.loadEnergyRequirements();
    } else if (event.tab.textLabel === 'Análisis Antropométrico') {
      this.loadBiometrics();
      if (!this.selectedAnalysisBiometricId && this.biometrics.length > 0) {
        this.selectedAnalysisBiometricId = this.biometrics[0].id ?? null;
      }
    }
  }

  get selectedBiometricForAnalysis(): Biometric | undefined {
    return this.biometrics.find(b => b.id === this.selectedAnalysisBiometricId);
  }

  selectBiometricForAnalysis(id: number) {
    this.selectedAnalysisBiometricId = id;
  }

  getSomatochartPath(endo: number, meso: number, ecto: number): string {
    // Centro de la somatocarta SVG en 200,200, escala: 1 unidad = 30px
    const cx = 200 + ((ecto - endo) * 30);
    const cy = 200 - ((2 * meso - endo - ecto) * 30);
    return `${cx},${cy}`;
  }

  getSomatochartX(endo: number, ecto: number): number {
    return 200 + ((ecto - endo) * 30);
  }

  getSomatochartY(endo: number, meso: number, ecto: number): number {
    return 200 - ((2 * meso - endo - ecto) * 30);
  }

  getFatCategory(pct?: number): string {
    if (pct === undefined || pct === null) return 'Sin datos';
    if (pct < 6) return 'Esencial';
    if (pct < 14) return 'Atleta';
    if (pct < 18) return 'Fitness';
    if (pct < 25) return 'Aceptable';
    return 'Obesidad';
  }

  getFatCategoryClass(pct?: number): string {
    if (pct === undefined || pct === null) return '';
    if (pct < 6) return 'cat-essential';
    if (pct < 14) return 'cat-athlete';
    if (pct < 18) return 'cat-fitness';
    if (pct < 25) return 'cat-acceptable';
    return 'cat-obese';
  }

  loadEnergyRequirements() {
    if (!this.clientId) return;
    this.svc.getEnergyRequirements(this.clientId).subscribe({
      next: (data) => {
        this.energyReq = data;
        this.energyErrorMessage = '';
      },
      error: (err) => {
        this.energyReq = null;
        this.energyErrorMessage = err.error || 'No se han podido calcular las necesidades energéticas. Asegúrate de registrar fecha de nacimiento y al menos un control biométrico con peso y altura.';
      }
    });
  }

  loadDietsHistory() {
    if (!this.clientId) return;
    this.svc.getClientDiets(this.clientId).subscribe({
      next: (history) => this.dietsHistory = history,
      error: () => this.snack.open('Error cargando historial de dietas', 'Cerrar', { duration: 3000 })
    });
  }

  downloadPdf(assignmentId?: number) {
    if (!this.clientId || !assignmentId) return;
    this.svc.downloadDietPdf(this.clientId, assignmentId).subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        const sanitizedClientName = (this.client?.fullName || 'Plan').replace(/\s+/g, '_');
        a.download = `Plan_Nutricional_${sanitizedClientName}.pdf`;
        a.click();
        window.URL.revokeObjectURL(url);
      },
      error: () => this.snack.open('Error al descargar el PDF de la dieta', 'Cerrar', { duration: 3000 })
    });
  }

  downloadConsultationPdf(): void {
    if (!this.clientId) return;

    const date = new Date().toISOString().slice(0, 10);
    this.svc.downloadConsultationPdf(this.clientId, date).subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        const sanitizedClientName = (this.client?.fullName || 'Paciente').replace(/\\s+/g, '_');
        a.download = `Informe_Consulta_${sanitizedClientName}_${date}.pdf`;
        a.click();
        window.URL.revokeObjectURL(url);
      },
      error: () => this.snack.open('Error al generar el informe de consulta', 'Cerrar', { duration: 3000 })
    });
  }

  // Exchange viewer helpers
  getExchangeItemKey(dayIndex: number, mealIndex: number, itemIndex: number): string {
    return `${dayIndex}-${mealIndex}-${itemIndex}`;
  }

  loadExchangeFoods(groupId: number, key: string): void {
    if (this.exchangeFoodsCache[key]) return; // already loaded
    this.foodService.getFoodsInExchangeGroup(groupId).subscribe({
      next: (foods) => {
        this.exchangeFoodsCache[key] = foods;
        if (foods.length > 0 && this.exchangeSelectedFood[key] == null) {
          this.exchangeSelectedFood[key] = foods[0].id;
        }
      },
      error: () => console.warn('No se pudieron cargar alimentos del grupo', groupId)
    });
  }

  getSelectedFoodGrams(key: string, exchangeCount: number): number | null {
    const foods = this.exchangeFoodsCache[key];
    const selectedId = this.exchangeSelectedFood[key];
    if (!foods || selectedId == null) return null;
    const food = foods.find(f => f.id === selectedId);
    if (!food) return null;
    return +(food.gramsPerExchange * exchangeCount).toFixed(0);
  }

  loadEvolution() {
    if (!this.clientId) return;
    this.svc.getEvolution(this.clientId).subscribe({
      next: (data) => {
        this.evolutionData = data;
        setTimeout(() => this.renderEvolutionCharts(), 0);
      },
      error: () => this.snack.open('Error cargando histórico de evolución', 'Cerrar', { duration: 3000 })
    });
  }

  onMetricChange(metric: string) {
    this.selectedMetric = metric;
    this.renderChart();
  }

  getLatestBiometric(): Biometric | undefined {
    return this.evolutionData.length ? this.evolutionData[this.evolutionData.length - 1] : undefined;
  }

  getPreviousBiometric(): Biometric | undefined {
    return this.evolutionData.length > 1 ? this.evolutionData[this.evolutionData.length - 2] : undefined;
  }

  getPreviousBiometricFor(b: Biometric): Biometric | undefined {
    const currentTime = new Date(b.measurementDate).getTime();
    return this.evolutionData
      .filter(item => item !== b && new Date(item.measurementDate).getTime() < currentTime)
      .sort((a, z) => new Date(z.measurementDate).getTime() - new Date(a.measurementDate).getTime())[0];
  }

  getBodyFatValue(b: Biometric | undefined): number | null {
    if (!b) return null;
    return b.bodyFat ??
      b.analysis?.bodyFatPercentageJacksonPollock4 ??
      b.analysis?.bodyFatPercentageJacksonPollock3 ??
      b.analysis?.bodyFatPercentageJacksonPollock7 ??
      b.analysis?.bodyFatPercentageFaulkner ??
      null;
  }

  getBodyFatSource(b: Biometric | undefined): string {
    if (!b) return '';
    if (b.bodyFat != null) return 'Medido';
    if (b.analysis?.bodyFatPercentageJacksonPollock4 != null) return 'Jackson-Pollock 4';
    if (b.analysis?.bodyFatPercentageJacksonPollock3 != null) return 'Jackson-Pollock 3';
    if (b.analysis?.bodyFatPercentageJacksonPollock7 != null) return 'Jackson-Pollock 7';
    if (b.analysis?.bodyFatPercentageFaulkner != null) return 'Faulkner';
    return '';
  }

  getFatMassValue(b: Biometric | undefined): number | null {
    if (!b) return null;
    if (b.analysis?.fatMassKg != null) return b.analysis.fatMassKg;
    const fatPct = this.getBodyFatValue(b);
    if (b.weight != null && fatPct != null) return b.weight * fatPct / 100;
    return null;
  }

  getMuscleMassValue(b: Biometric | undefined): number | null {
    if (!b) return null;
    return b.muscleMass ?? b.analysis?.muscleMassKg ?? null;
  }

  getMetricDelta(b: Biometric, metric: 'weight' | 'waist' | 'bodyFat' | 'fatMass' | 'muscleMass'): number | null {
    const previous = this.getPreviousBiometricFor(b);
    if (!previous) return null;

    const currentValue = metric === 'weight' ? b.weight :
      metric === 'waist' ? b.waist :
      metric === 'bodyFat' ? this.getBodyFatValue(b) :
      metric === 'fatMass' ? this.getFatMassValue(b) :
      this.getMuscleMassValue(b);

    const previousValue = metric === 'weight' ? previous.weight :
      metric === 'waist' ? previous.waist :
      metric === 'bodyFat' ? this.getBodyFatValue(previous) :
      metric === 'fatMass' ? this.getFatMassValue(previous) :
      this.getMuscleMassValue(previous);

    return this.getDelta(currentValue, previousValue);
  }

  formatDelta(delta: number | null): string {
    if (delta == null) return '';
    const sign = delta > 0 ? '+' : '';
    return sign + delta.toFixed(1);
  }

  getDeltaClass(delta: number | null, metric?: 'weight' | 'waist' | 'bodyFat' | 'fatMass' | 'muscleMass'): string {
    if (delta == null || delta === 0) return 'delta-neutral';

    // En masa muscular, aumentar es favorable; en cintura/grasa, reducir suele
    // ser el cambio esperado. El peso se mantiene neutro porque su interpretación
    // depende del objetivo del paciente.
    if (metric === 'muscleMass') return delta > 0 ? 'delta-negative' : 'delta-positive';
    if (metric === 'waist' || metric === 'bodyFat' || metric === 'fatMass') {
      return delta < 0 ? 'delta-negative' : 'delta-positive';
    }

    return 'delta-neutral';
  }

  getDeltaDirectionIcon(delta: number | null): string {
    if (delta == null || delta === 0) return 'remove';
    return delta > 0 ? 'arrow_upward' : 'arrow_downward';
  }

  getDelta(current?: number | null, previous?: number | null): number | null {
    if (current == null || previous == null) return null;
    return Math.round((current - previous) * 10) / 10;
  }

  getWeightDelta(): number | null {
    return this.getDelta(this.getLatestBiometric()?.weight, this.getPreviousBiometric()?.weight);
  }

  getBodyFatDelta(): number | null {
    return this.getDelta(this.getBodyFatValue(this.getLatestBiometric()), this.getBodyFatValue(this.getPreviousBiometric()));
  }

  loadPatientCheckins(): void {
    if (!this.clientId) return;
    this.portalService.getCheckins(this.clientId).subscribe({
      next: checkins => {
        this.checkins = [...checkins].sort((a, b) =>
          new Date(a.submitted_at).getTime() - new Date(b.submitted_at).getTime());
        setTimeout(() => this.renderCheckinChart(), 0);
      },
      error: () => this.checkins = []
    });
  }

  onFollowupMetricChange(metric: string): void {
    this.selectedFollowupMetric = metric;
    this.renderCheckinChart();
  }

  private getFollowupMetricValue(checkin: PatientCheckin): number | null {
    switch (this.selectedFollowupMetric) {
      case 'adherence': return checkin.adherence ?? null;
      case 'hunger': return checkin.hunger ?? null;
      case 'energy': return checkin.energy ?? null;
      case 'sleep_quality': return checkin.sleep_quality ?? null;
      case 'sleep_hours': return checkin.sleep_hours ?? null;
      case 'training': return checkin.training ?? null;
      case 'weight': return checkin.weight ?? null;
      default: return null;
    }
  }

  getFollowupMetricLabel(): string {
    switch (this.selectedFollowupMetric) {
      case 'adherence': return 'Adherencia (%)';
      case 'hunger': return 'Hambre (0–10)';
      case 'energy': return 'Energía (0–10)';
      case 'sleep_quality': return 'Calidad del sueño (0–10)';
      case 'sleep_hours': return 'Horas de sueño';
      case 'training': return 'Entrenamiento (0–10)';
      case 'weight': return 'Peso del check-in (kg)';
      default: return '';
    }
  }

  private renderCheckinChart(): void {
    const canvas = document.getElementById('checkinEvolutionChart') as HTMLCanvasElement | null;
    if (!canvas) return;
    if (this.checkinChart) this.checkinChart.destroy();
    if (this.checkins.length < 1) return;
    this.checkinChart = new Chart(canvas, {
      type: 'line',
      data: {
        labels: this.checkins.map(c => {
          const d = new Date(c.submitted_at);
          return Number.isNaN(d.getTime()) ? c.week_start : d.toLocaleDateString('es-ES', { day: '2-digit', month: '2-digit' });
        }),
        datasets: [{
          label: this.getFollowupMetricLabel(),
          data: this.checkins.map(c => this.getFollowupMetricValue(c)),
          borderWidth: 3, tension: 0.3, fill: false, spanGaps: true,
          pointRadius: 4, pointHoverRadius: 6
        }]
      },
      options: {
        responsive: true, maintainAspectRatio: false,
        plugins: { legend: { display: true, position: 'top' } },
        scales: { y: { beginAtZero: false }, x: { grid: { display: false } } }
      }
    });
  }

  renderEvolutionCharts() {
    this.renderWeightChart();
    this.renderBodyFatChart();
    this.renderCheckinChart();
    if (this.evolutionData.length >= 2) this.renderChart();
  }

  private getChartLabels(): string[] {
    return this.evolutionData.map(b => {
      if (!b.measurementDate) return '';
      const d = new Date(b.measurementDate);
      return Number.isNaN(d.getTime()) ? b.measurementDate : d.toLocaleDateString('es-ES', { day: '2-digit', month: '2-digit', year: 'numeric' });
    });
  }

  private renderWeightChart() {
    const canvas = document.getElementById('weightEvolutionChart') as HTMLCanvasElement | null;
    if (!canvas) return;
    if (this.weightChart) this.weightChart.destroy();
    this.weightChart = new Chart(canvas, {
      type: 'line',
      data: {
        labels: this.getChartLabels(),
        datasets: [{
          label: 'Peso (kg)',
          data: this.evolutionData.map(b => b.weight ?? null),
          borderColor: '#2563eb', backgroundColor: 'rgba(37,99,235,0.10)',
          borderWidth: 3, tension: 0.3, fill: true, spanGaps: true,
          pointRadius: 4, pointHoverRadius: 6
        }]
      },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: false }, x: { grid: { display: false } } } }
    });
  }

  private renderBodyFatChart() {
    const canvas = document.getElementById('bodyFatEvolutionChart') as HTMLCanvasElement | null;
    if (!canvas) return;
    if (this.bodyFatChart) this.bodyFatChart.destroy();
    this.bodyFatChart = new Chart(canvas, {
      type: 'line',
      data: {
        labels: this.getChartLabels(),
        datasets: [{
          label: '% grasa corporal',
          data: this.evolutionData.map(b => this.getBodyFatValue(b)),
          borderColor: '#16a34a', backgroundColor: 'rgba(22,163,74,0.10)',
          borderWidth: 3, tension: 0.3, fill: true, spanGaps: true,
          pointRadius: 4, pointHoverRadius: 6
        }]
      },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: false } }, scales: { y: { beginAtZero: false, ticks: { callback: (value) => value + '%' } }, x: { grid: { display: false } } } }
    });
  }

  renderChart() {
    const ctx = document.getElementById('evolutionChart') as HTMLCanvasElement;
    if (!ctx) return;
    if (this.chart) this.chart.destroy();
    const labels = this.getChartLabels();
    const dataPoints = this.evolutionData.map(b => {
      switch (this.selectedMetric) {
        case 'weight': return b.weight ?? null;
        case 'bodyFat': return b.bodyFat ?? null;
        case 'muscleMass': return b.muscleMass ?? null;
        case 'bmi': return b.bmi ?? null;
        case 'waist': return b.waist ?? null;
        case 'hip': return b.hip ?? null;
        case 'jp3': return b.analysis?.bodyFatPercentageJacksonPollock3 ?? null;
        case 'jp4': return b.analysis?.bodyFatPercentageJacksonPollock4 ?? null;
        case 'faulkner': return b.analysis?.bodyFatPercentageFaulkner ?? null;
        default: return null;
      }
    });
    this.chart = new Chart(ctx, {
      type: 'line',
      data: { labels, datasets: [{ label: this.getMetricLabel(this.selectedMetric), data: dataPoints, borderColor: '#6366f1', backgroundColor: 'rgba(99,102,241,0.08)', borderWidth: 3, tension: 0.3, fill: true, spanGaps: true, pointRadius: 4, pointHoverRadius: 6 }] },
      options: { responsive: true, maintainAspectRatio: false, plugins: { legend: { display: true, position: 'top' } }, scales: { y: { beginAtZero: false }, x: { grid: { display: false } } } }
    });
  }

  getMetricLabel(metric: string): string {
    switch (metric) {
      case 'weight': return 'Peso (kg)';
      case 'bodyFat': return '% Grasa Corporal (manual)';
      case 'muscleMass': return 'Masa Muscular (kg)';
      case 'bmi': return 'Índice de Masa Corporal (IMC)';
      case 'waist': return 'Cintura (cm)';
      case 'hip': return 'Cadera (cm)';
      case 'jp3': return '% Grasa Jackson-Pollock 3';
      case 'jp4': return '% Grasa Jackson-Pollock 4';
      case 'faulkner': return '% Grasa Faulkner';
      default: return '';
    }
  }

  createDietForClient(): void {
    if (!this.clientId) return;
    this.router.navigate(['/diets/nuevo'], { queryParams: { clientId: this.clientId } });
  }

  openAssignDietDialog() {
    if (!this.clientId) return;
    const dialogRef = this.dialog.open(DietSelectDialogComponent, {
      width: '500px',
      data: { clientId: this.clientId }
    });

    dialogRef.afterClosed().subscribe(result => {
      if (result) {
        this.svc.assignDiet(this.clientId!, result).subscribe({
          next: () => {
            this.snack.open('Dieta asignada con éxito', 'Cerrar', { duration: 3000 });
            this.loadDietsHistory();
          },
          error: (err) => {
            console.error('Error al asignar dieta', err);
            this.snack.open('Error al asignar la dieta', 'Cerrar', { duration: 3000 });
          }
        });
      }
    });
  }

  deactivateDiet(assignmentId?: number) {
    if (!this.clientId || !assignmentId) return;
    if (!confirm('¿Estás seguro de que deseas desactivar esta dieta activa?')) return;
    this.svc.deactivateClientDiet(this.clientId, assignmentId).subscribe({
      next: () => {
        this.snack.open('Dieta desactivada', 'Cerrar', { duration: 3000 });
        this.loadDietsHistory();
      },
      error: () => this.snack.open('Error al desactivar la dieta', 'Cerrar', { duration: 3000 })
    });
  }

  deleteDietAssignment(assignmentId?: number) {
    if (!this.clientId || !assignmentId) return;
    if (!confirm('¿Estás seguro de que deseas eliminar este registro de asignación? (No eliminará la plantilla de dieta, solo la asignación a este paciente)')) return;
    this.svc.deleteClientDiet(this.clientId, assignmentId).subscribe({
      next: () => {
        this.snack.open('Asignación eliminada', 'Cerrar', { duration: 3000 });
        this.loadDietsHistory();
      },
      error: () => this.snack.open('Error al eliminar la asignación', 'Cerrar', { duration: 3000 })
    });
  }

  loadPortalAccess(): void {
    if (!this.clientId) return;
    this.loadingPortalAccess = true;
    this.portalService.getClientPortalAccess(this.clientId).subscribe({
      next: (access) => {
        this.portalAccess = access;
        this.loadingPortalAccess = false;
      },
      error: () => {
        this.loadingPortalAccess = false;
      }
    });
  }

  regeneratePortalToken(): void {
    if (!this.clientId) return;
    if (!confirm('¿Deseas regenerar el enlace mágico? El enlace anterior dejará de funcionar de inmediato.')) return;
    this.regeneratingToken = true;
    this.portalService.regenerateClientToken(this.clientId).subscribe({
      next: (access) => {
        this.portalAccess = access;
        this.regeneratingToken = false;
        this.snack.open('Enlace mágico regenerado con éxito', 'Cerrar', { duration: 3000 });
      },
      error: () => {
        this.regeneratingToken = false;
        this.snack.open('Error al regenerar el enlace', 'Cerrar', { duration: 3000 });
      }
    });
  }

  copyPortalLink(): void {
    if (!this.portalAccess?.magicLink) return;
    navigator.clipboard.writeText(this.portalAccess.magicLink).then(() => {
      this.snack.open('Enlace copiado al portapapeles', 'Cerrar', { duration: 2500 });
    }).catch(() => {
      this.snack.open('No se pudo copiar automáticamente', 'Cerrar', { duration: 2500 });
    });
  }

  setPatientPasscode(): void {
    const passcode = this.newPasscode.trim();
    if (!this.clientId || !/^\d{6}$/.test(passcode)) {
      this.snack.open('El PIN debe tener exactamente 6 dígitos', 'Cerrar', { duration: 3000 });
      return;
    }
    this.settingPasscode = true;
    this.portalService.setClientPasscode(this.clientId, passcode).subscribe({
      next: () => {
        this.settingPasscode = false;
        if (this.portalAccess) this.portalAccess.hasPasscode = true;
        this.newPasscode = '';
        this.snack.open('PIN de acceso actualizado correctamente', 'Cerrar', { duration: 3000 });
      },
      error: () => {
        this.settingPasscode = false;
        this.snack.open('Error al guardar el PIN', 'Cerrar', { duration: 3000 });
      }
    });
  }
}

