import { Component, OnInit, OnDestroy } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ClientService } from '../../servicios/client.service';
import { FoodService } from '../../servicios/food.service';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Client, Biometric, ClientDiet } from '../../modelos/client';
import { FoodInExchangeGroup } from '../../modelos/food-exchange-group';
import { CommonModule } from '@angular/common';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatTabChangeEvent } from '@angular/material/tabs';
import Chart from 'chart.js/auto';

// Angular Material
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatSelectModule } from '@angular/material/select';
import { MatTabsModule } from '@angular/material/tabs';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';

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
    MatButtonModule
  ],
  standalone: true
})
export class ClientDetailComponent implements OnInit, OnDestroy {
  clientId?: number;
  client?: Client;
  loading = false;

  clientForm!: FormGroup;
  biometrics: Biometric[] = [];
  showBiometricForm = false;
  biometricForm!: FormGroup;
  editingBiometricId: number | null = null;

  evolutionData: Biometric[] = [];
  selectedMetric = 'weight';
  chart: Chart | null = null;
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

  constructor(
    private fb: FormBuilder,
    private svc: ClientService,
    private foodService: FoodService,
    private route: ActivatedRoute,
    private router: Router,
    private snack: MatSnackBar
  ) { }

  ngOnInit(): void {
    this.clientId = Number(this.route.snapshot.paramMap.get('id'));
    this.buildForms();

    if (this.clientId) {
      this.loadClient();
      this.loadBiometrics();
      this.loadDietsHistory();
      this.loadEnergyRequirements();
    }
  }

  buildForms() {
    this.clientForm = this.fb.group({
      fullName: ['', Validators.required],
      email: [''],
      phone: [''],
      birthDate: [''],
      gender: [''],
      notes: ['']
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
  }

  loadClient() {
    this.svc.getClient(this.clientId!).subscribe({
      next: (c) => {
        this.client = c;
        this.clientForm.patchValue({
          fullName: c.fullName,
          email: c.email,
          phone: c.phone,
          birthDate: c.birthDate,
          gender: c.gender,
          notes: c.notes
        });
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

  saveClient() {
    const payload = {
      fullName: this.clientForm.value.fullName,
      email: this.clientForm.value.email,
      phone: this.clientForm.value.phone,
      birthDate: this.clientForm.value.birthDate,
      gender: this.clientForm.value.gender,
      notes: this.clientForm.value.notes
    };

    if (this.clientId) {
      this.svc.updateClient(this.clientId, payload).subscribe({
        next: () => this.snack.open('Cliente actualizado', 'Cerrar', { duration: 2000 }),
        error: () => this.snack.open('Error al actualizar', 'Cerrar', { duration: 3000 })
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
    });
  }

  saveBiometric() {
    if (!this.clientId) return;
    const payload = { ...this.biometricForm.value };

    if (this.editingBiometricId) {
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
    if (this.chart) {
      this.chart.destroy();
    }
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
        setTimeout(() => this.renderChart(), 0);
      },
      error: () => this.snack.open('Error cargando histórico de evolución', 'Cerrar', { duration: 3000 })
    });
  }

  onMetricChange(metric: string) {
    this.selectedMetric = metric;
    this.renderChart();
  }

  renderChart() {
    const ctx = document.getElementById('evolutionChart') as HTMLCanvasElement;
    if (!ctx) return;

    if (this.chart) {
      this.chart.destroy();
    }

    const labels = this.evolutionData.map(b => {
      if (!b.measurementDate) return '';
      // Intentamos formatear la fecha a dd/mm/aaaa
      try {
        const d = new Date(b.measurementDate);
        return d.toLocaleDateString('es-ES', { day: '2-digit', month: '2-digit', year: 'numeric' });
      } catch {
        return b.measurementDate;
      }
    });

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

    const metricLabel = this.getMetricLabel(this.selectedMetric);

    this.chart = new Chart(ctx, {
      type: 'line',
      data: {
        labels: labels,
        datasets: [{
          label: metricLabel,
          data: dataPoints,
          borderColor: '#3f51b5',
          backgroundColor: 'rgba(63, 81, 181, 0.1)',
          borderWidth: 3,
          tension: 0.3,
          fill: true,
          pointBackgroundColor: '#3f51b5',
          pointRadius: 5,
          pointHoverRadius: 7
        }]
      },
      options: {
        responsive: true,
        maintainAspectRatio: false,
        plugins: {
          legend: {
            display: true,
            position: 'top'
          }
        },
        scales: {
          y: {
            beginAtZero: false,
            grid: {
              color: 'rgba(0,0,0,0.05)'
            }
          },
          x: {
            grid: {
              display: false
            }
          }
        }
      }
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
}

