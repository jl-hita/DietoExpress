import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { PatientCheckin, PatientPortalService } from '../../servicios/patient-portal.service';

interface ConsultationStep {
  key: string;
  label: string;
  icon: string;
  description: string;
}

interface GuidedConsultationResponse {
  appointment: {
    id: number;
    startsAt: string;
    endsAt: string;
    status: string;
    clientId: number;
    clientName?: string | null;
    nutritionistId: number;
  };
  suggestedConsultationType: 'first' | 'follow_up';
  consultation: any | null;
  latestCheckin: PatientCheckin | null;
}

@Component({
  selector: 'app-guided-consultation',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatSnackBarModule],
  template: `
    <div class="consultation-page" *ngIf="!loading && data">
      <header class="consultation-header">
        <div>
          <span class="eyebrow">CONSULTA GUIADA</span>
          <h1>{{ data.appointment.clientName || 'Paciente' }}</h1>
          <p>{{ data.suggestedConsultationType === 'first' ? 'Primera consulta' : 'Seguimiento' }} · {{ data.appointment.startsAt | date:'dd/MM/yyyy HH:mm' }}</p>
        </div>
        <div class="header-actions">
          <span class="status" [class.completed]="consultation?.status === 'completed'">
            {{ consultation?.status === 'completed' ? 'Completada' : 'En curso' }}
          </span>
          <button mat-stroked-button type="button" (click)="backToAgenda()"><mat-icon>arrow_back</mat-icon> Agenda</button>
        </div>
      </header>

      <div class="flow-layout">
        <aside class="stepper">
          <button type="button" *ngFor="let step of steps; let i = index"
            [class.active]="step.key === currentStep"
            [class.done]="isCompleted(step.key)"
            (click)="goToStep(step.key)">
            <span class="step-number"><mat-icon *ngIf="isCompleted(step.key)">check</mat-icon><span *ngIf="!isCompleted(step.key)">{{ i + 1 }}</span></span>
            <span><strong>{{ step.label }}</strong><small>{{ step.description }}</small></span>
          </button>
        </aside>

        <main class="step-content">
          <section class="card" *ngIf="currentStep === 'summary'">
            <div class="card-title"><mat-icon>person</mat-icon><div><h2>Resumen del paciente</h2><p>Información disponible antes de empezar la revisión clínica.</p></div></div>
            <div class="summary-grid">
              <div><span>Paciente</span><strong>{{ data.appointment.clientName || 'Paciente' }}</strong></div>
              <div><span>Tipo</span><strong>{{ data.suggestedConsultationType === 'first' ? 'Primera consulta' : 'Seguimiento' }}</strong></div>
              <div><span>Cita</span><strong>{{ data.appointment.startsAt | date:'dd/MM/yyyy HH:mm' }}</strong></div>
              <div><span>Check-in</span><strong>{{ data.latestCheckin ? (data.latestCheckin.submitted_at | date:'dd/MM/yyyy HH:mm') : 'Sin check-in' }}</strong></div>
            </div>
          </section>

          <section class="card" *ngIf="currentStep === 'evolution'">
            <div class="card-title"><mat-icon>show_chart</mat-icon><div><h2>Evolución</h2><p>La ficha del paciente conserva las gráficas y el histórico completo.</p></div></div>
            <p class="helper">Desde aquí puedes revisar el histórico de biometrías y seguimiento. El flow guarda este paso para que puedas continuar sin perder el contexto.</p>
            <button mat-stroked-button type="button" (click)="openPatient()"><mat-icon>open_in_new</mat-icon> Abrir ficha del paciente</button>
          </section>

          <section class="card" *ngIf="currentStep === 'checkin'">
            <div class="card-title"><mat-icon>fact_check</mat-icon><div><h2>Check-in y adherencia</h2><p>Últimos datos estructurados comunicados por el paciente.</p></div></div>
            <div *ngIf="data.latestCheckin; else noCheckin" class="metric-grid">
              <div><span>Adherencia</span><strong>{{ data.latestCheckin?.adherence ?? '—' }}/10</strong></div>
              <div><span>Hambre</span><strong>{{ data.latestCheckin?.hunger ?? '—' }}/10</strong></div>
              <div><span>Energía</span><strong>{{ data.latestCheckin?.energy ?? '—' }}/10</strong></div>
              <div><span>Sueño</span><strong>{{ data.latestCheckin?.sleep_quality ?? '—' }}/10 · {{ data.latestCheckin?.sleep_hours ?? '—' }} h</strong></div>
              <div><span>Entrenamiento</span><strong>{{ data.latestCheckin?.training ?? '—' }}/10</strong></div>
              <div><span>Peso</span><strong>{{ data.latestCheckin?.weight ?? '—' }} kg</strong></div>
            </div>
            <ng-template #noCheckin><div class="empty"><mat-icon>assignment_late</mat-icon><span>No hay check-in registrado.</span></div></ng-template>
            <div class="actions" *ngIf="data.latestCheckin">
              <button mat-stroked-button type="button" *ngIf="!data.latestCheckin?.reviewed_at" (click)="reviewCheckin()"><mat-icon>done</mat-icon> Marcar revisado</button>
              <span class="reviewed" *ngIf="data.latestCheckin?.reviewed_at"><mat-icon>check_circle</mat-icon> Revisado</span>
            </div>
          </section>

          <section class="card" *ngIf="currentStep === 'goals'">
            <div class="card-title"><mat-icon>flag</mat-icon><div><h2>Cambios y objetivos</h2><p>Define aquí los puntos que quieres trabajar en la consulta.</p></div></div>
            <div class="placeholder"><mat-icon>edit_note</mat-icon><span>El contenido de este paso se persistirá en el progreso de la consulta.</span></div>
          </section>

          <section class="card" *ngIf="currentStep === 'diet'">
            <div class="card-title"><mat-icon>restaurant</mat-icon><div><h2>Dieta</h2><p>Revisa o modifica la dieta activa sin perder el contexto de la consulta.</p></div></div>
            <button mat-stroked-button type="button" (click)="openPatientDiet()"><mat-icon>open_in_new</mat-icon> Ir a dietas</button>
          </section>

          <section class="card" *ngIf="currentStep === 'education'">
            <div class="card-title"><mat-icon>school</mat-icon><div><h2>Educación y recomendaciones</h2><p>Registra los puntos que quieres reforzar con el paciente.</p></div></div>
            <div class="placeholder"><mat-icon>lightbulb</mat-icon><span>Paso preparado para recomendaciones y educación personalizada.</span></div>
          </section>

          <section class="card" *ngIf="currentStep === 'tasks'">
            <div class="card-title"><mat-icon>task_alt</mat-icon><div><h2>Tareas</h2><p>Convierte las señales del seguimiento en acciones profesionales.</p></div></div>
            <button mat-stroked-button type="button" *ngIf="data.latestCheckin" (click)="createFollowUpTask()" [disabled]="taskCreating">
              <mat-icon>{{ taskCreating ? 'hourglass_top' : 'add_task' }}</mat-icon>
              {{ taskCreating ? 'Creando tarea…' : 'Crear tarea desde el seguimiento' }}
            </button>
            <div class="helper" *ngIf="!data.latestCheckin">No hay check-in del que generar una tarea.</div>
          </section>

          <section class="card" *ngIf="currentStep === 'next_appointment'">
            <div class="card-title"><mat-icon>event</mat-icon><div><h2>Próxima cita</h2><p>La reserva de la siguiente visita se gestiona desde la agenda.</p></div></div>
            <button mat-stroked-button type="button" (click)="backToAgenda()"><mat-icon>calendar_month</mat-icon> Volver a agenda</button>
          </section>

          <section class="card close-card" *ngIf="currentStep === 'close'">
            <div class="card-title"><mat-icon>check_circle</mat-icon><div><h2>Cerrar consulta</h2><p>Al cerrar se marca la consulta como completada y, si procede, la cita como completada.</p></div></div>
            <button mat-flat-button color="primary" type="button" (click)="completeConsultation()" [disabled]="consultation?.status === 'completed' || completing">
              <mat-icon>{{ completing ? 'hourglass_top' : 'check' }}</mat-icon>
              {{ completing ? 'Cerrando…' : 'Cerrar consulta' }}
            </button>
          </section>

          <footer class="navigation">
            <button mat-stroked-button type="button" (click)="previousStep()" [disabled]="stepIndex === 0"><mat-icon>arrow_back</mat-icon> Anterior</button>
            <button mat-flat-button color="primary" type="button" (click)="nextStep()" *ngIf="stepIndex < steps.length - 1">
              {{ isCompleted(currentStep) ? 'Siguiente' : 'Marcar paso y continuar' }} <mat-icon>arrow_forward</mat-icon>
            </button>
            <button mat-stroked-button type="button" *ngIf="stepIndex === steps.length - 1" (click)="goToStep('summary')"><mat-icon>replay</mat-icon> Volver al resumen</button>
          </footer>
        </main>
      </div>
    </div>

    <div class="loading" *ngIf="loading"><mat-spinner diameter="40"></mat-spinner><span>Preparando consulta…</span></div>
  `,
  styles: [`
    .consultation-page{width:100%;padding:28px 24px 44px;box-sizing:border-box}.consultation-header{display:flex;justify-content:space-between;gap:20px;align-items:flex-end;margin-bottom:22px}.eyebrow{font-size:11px;font-weight:800;letter-spacing:1.2px;color:#0f766e}.consultation-header h1{margin:5px 0 4px;font-size:30px;color:#0f172a}.consultation-header p{margin:0;color:#64748b}.header-actions{display:flex;align-items:center;gap:12px}.status{padding:7px 11px;border-radius:999px;background:#fef3c7;color:#92400e;font-size:12px;font-weight:700}.status.completed{background:#dcfce7;color:#166534}.flow-layout{display:grid;grid-template-columns:280px minmax(0,1fr);gap:20px;align-items:start}.stepper{position:sticky;top:18px;display:grid;gap:6px}.stepper button{border:1px solid transparent;background:#f8fafc;border-radius:11px;padding:10px;text-align:left;display:flex;gap:10px;align-items:center;cursor:pointer}.stepper button.active{background:#ecfeff;border-color:#99f6e4}.stepper button.done{background:#f0fdf4}.step-number{width:28px;height:28px;border-radius:50%;display:grid;place-items:center;background:#e2e8f0;color:#475569;font-size:12px;font-weight:700;flex:none}.stepper button.active .step-number{background:#0f766e;color:#fff}.stepper button.done .step-number{background:#16a34a;color:#fff}.stepper strong{display:block;font-size:12px;color:#0f172a}.stepper small{display:block;color:#64748b;font-size:10px;margin-top:2px}.step-content{min-width:0}.card{background:#fff;border:1px solid #e2e8f0;border-radius:15px;padding:22px;min-height:270px;box-sizing:border-box}.card-title{display:flex;gap:12px;align-items:flex-start;margin-bottom:20px}.card-title>mat-icon{color:#0f766e}.card-title h2{margin:0 0 4px;font-size:20px;color:#0f172a}.card-title p{margin:0;color:#64748b;font-size:12px}.summary-grid,.metric-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}.summary-grid>div,.metric-grid>div{padding:14px;border-radius:10px;background:#f8fafc}.summary-grid span,.metric-grid span{display:block;color:#64748b;font-size:11px}.summary-grid strong,.metric-grid strong{display:block;margin-top:4px;color:#0f172a;font-size:14px}.helper{padding:14px;border-radius:10px;background:#f8fafc;color:#64748b;font-size:13px;line-height:1.5;margin-bottom:15px}.placeholder{min-height:130px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:8px;color:#94a3b8;text-align:center}.placeholder mat-icon{font-size:38px;width:38px;height:38px}.actions{display:flex;align-items:center;gap:12px;margin-top:18px}.reviewed{display:flex;align-items:center;gap:6px;color:#15803d;font-size:12px}.navigation{display:flex;justify-content:space-between;gap:10px;margin-top:16px}.empty{min-height:120px;display:flex;align-items:center;justify-content:center;gap:8px;color:#94a3b8}.loading{min-height:60vh;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;color:#64748b}@media(max-width:850px){.consultation-page{padding:20px 14px 36px}.consultation-header{align-items:stretch;flex-direction:column}.header-actions{justify-content:space-between}.flow-layout{grid-template-columns:1fr}.stepper{position:static;display:flex;overflow:auto;padding-bottom:3px}.stepper button{min-width:190px}.summary-grid,.metric-grid{grid-template-columns:1fr}}
  `]
})
export class GuidedConsultationComponent implements OnInit {
  loading = true;
  taskCreating = false;
  completing = false;
  data: GuidedConsultationResponse | null = null;
  consultation: any = null;
  currentStep = 'summary';
  stepIndex = 0;

  readonly steps: ConsultationStep[] = [
    { key:'summary', label:'Resumen', icon:'person', description:'Contexto del paciente' },
    { key:'evolution', label:'Evolución', icon:'show_chart', description:'Cambios recientes' },
    { key:'checkin', label:'Check-in', icon:'fact_check', description:'Adherencia y señales' },
    { key:'goals', label:'Objetivos', icon:'flag', description:'Cambios y objetivos' },
    { key:'diet', label:'Dieta', icon:'restaurant', description:'Plan actual' },
    { key:'education', label:'Educación', icon:'school', description:'Recomendaciones' },
    { key:'tasks', label:'Tareas', icon:'task_alt', description:'Acciones posteriores' },
    { key:'next_appointment', label:'Próxima cita', icon:'event', description:'Continuidad' },
    { key:'close', label:'Cerrar', icon:'check_circle', description:'Finalizar consulta' }
  ];

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private portalService: PatientPortalService,
    private snack: MatSnackBar
  ) {}

  ngOnInit(): void {
    const appointmentId = Number(this.route.snapshot.paramMap.get('appointmentId'));
    if (!appointmentId) { this.backToAgenda(); return; }

    this.portalService.getGuidedConsultation(appointmentId).subscribe({
      next: value => {
        this.data = value;
        this.consultation = value.consultation;
        if (this.consultation) {
          this.currentStep = this.consultation.currentStep || 'summary';
          this.stepIndex = this.steps.findIndex(x => x.key === this.currentStep);
          if (this.stepIndex < 0) this.stepIndex = 0;
          this.currentStep = this.steps[this.stepIndex].key;
          this.loading = false;
        } else {
          this.portalService.startGuidedConsultation(appointmentId, value.suggestedConsultationType).subscribe({
            next: consultation => {
              this.consultation = consultation;
              this.loading = false;
            },
            error: err => {
              this.loading = false;
              this.snack.open(err?.error?.message || 'No se ha podido iniciar la consulta.', 'Cerrar', { duration: 3500 });
            }
          });
        }
      },
      error: err => {
        this.loading = false;
        this.snack.open(err?.error?.message || 'No se ha podido cargar la consulta.', 'Cerrar', { duration: 3500 });
      }
    });
  }

  isCompleted(step: string): boolean {
    return !!this.consultation?.completedSteps?.includes(step);
  }

  goToStep(step: string): void {
    const index = this.steps.findIndex(x => x.key === step);
    if (index < 0) return;
    this.stepIndex = index;
    this.currentStep = this.steps[index].key;
    this.saveProgress();
  }

  nextStep(): void {
    if (!this.consultation || this.stepIndex >= this.steps.length - 1) return;
    const completed = new Set<string>(this.consultation.completedSteps || []);
    completed.add(this.currentStep);
    const next = this.steps[this.stepIndex + 1].key;
    this.persistProgress(next, [...completed]);
  }

  previousStep(): void {
    if (this.stepIndex <= 0) return;
    this.stepIndex--;
    this.currentStep = this.steps[this.stepIndex].key;
    this.saveProgress();
  }

  private saveProgress(): void {
    this.persistProgress(this.currentStep, this.consultation?.completedSteps || []);
  }

  private persistProgress(step: string, completedSteps: string[]): void {
    if (!this.consultation || !this.data) return;
    this.portalService.updateGuidedConsultationProgress(this.data.appointment.id, {
      currentStep: step,
      completedSteps,
      progress: this.consultation.progress || {}
    }).subscribe({
      next: value => {
        this.consultation = value;
        this.currentStep = value.currentStep;
        this.stepIndex = Math.max(0, this.steps.findIndex(x => x.key === value.currentStep));
      },
      error: () => this.snack.open('No se ha podido guardar el progreso de la consulta.', 'Cerrar', { duration: 3000 })
    });
  }

  reviewCheckin(): void {
    const checkin = this.data?.latestCheckin;
    if (!checkin) return;
    this.portalService.reviewCheckin(checkin.id).subscribe({
      next: () => {
        checkin.reviewed_at = new Date().toISOString();
        this.snack.open('Check-in marcado como revisado.', 'Cerrar', { duration: 1800 });
      },
      error: () => this.snack.open('No se ha podido marcar el check-in.', 'Cerrar', { duration: 3000 })
    });
  }

  createFollowUpTask(): void {
    const checkin = this.data?.latestCheckin;
    if (!checkin || this.taskCreating) return;
    this.taskCreating = true;
    this.portalService.createFollowUpTask(checkin.id).subscribe({
      next: () => {
        this.taskCreating = false;
        this.snack.open('Tarea de seguimiento creada.', 'Cerrar', { duration: 1800 });
      },
      error: err => {
        this.taskCreating = false;
        this.snack.open(err?.error?.message || 'No se ha podido crear la tarea.', 'Cerrar', { duration: 3000 });
      }
    });
  }

  completeConsultation(): void {
    if (!this.data || this.completing || this.consultation?.status === 'completed') return;
    this.completing = true;
    this.portalService.completeGuidedConsultation(this.data.appointment.id).subscribe({
      next: consultation => {
        this.completing = false;
        this.consultation = consultation;
        this.snack.open('Consulta cerrada correctamente.', 'Cerrar', { duration: 2000 });
      },
      error: err => {
        this.completing = false;
        this.snack.open(err?.error?.message || 'No se ha podido cerrar la consulta.', 'Cerrar', { duration: 3500 });
      }
    });
  }

  openPatient(): void {
    this.router.navigate(['/clients', this.data?.appointment.clientId]);
  }

  openPatientDiet(): void {
    this.router.navigate(['/diets'], { queryParams: { clientId: this.data?.appointment.clientId } });
  }

  backToAgenda(): void {
    this.router.navigate(['/appointments']);
  }
}
