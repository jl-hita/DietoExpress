import { CommonModule } from '@angular/common';
import { Component, OnInit, ChangeDetectionStrategy, signal} from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { FormsModule } from '@angular/forms';
import { PatientCheckin, PatientPortalService, PatientAppointment, AppointmentSlot } from '../../servicios/patient-portal.service';
import { DietService } from '../../servicios/diet.service';

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
    modality?: string | null;
  };
  suggestedConsultationType: 'first' | 'follow_up';
  consultation: any | null;
  latestCheckin: PatientCheckin | null;
  previousAppointment?: any;
  activeDiet?: any;
  openTasks?: any[];
  followupSignals?: any[];
  biometrics?: {
    latest?: any;
    previous?: any;
    changes?: { weight?: number | null; bodyFat?: number | null; muscleMass?: number | null; waist?: number | null } | null;
  };
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-guided-consultation',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatSnackBarModule],
  template: `
    <div class="consultation-page" *ngIf="!loading && data">
      <header class="consultation-header">
        <div>
          <span class="eyebrow">CONSULTA GUIADA</span>
          <h1>{{ data.appointment.clientName || 'Paciente' }}</h1>
          <p>{{ data.suggestedConsultationType === 'first' ? 'Primera consulta' : 'Seguimiento' }} · {{ data.appointment.startsAt | date:'dd/MM/yyyy HH:mm' }}</p>
        </div>
        <div class="header-actions">
          <button mat-flat-button color="primary" type="button" *ngIf="data.appointment.modality === 'online' && consultation?.status !== 'completed'" (click)="openOnlineConsultation()"><mat-icon>videocam</mat-icon> Entrar en consulta online</button>
          <button mat-stroked-button type="button" *ngIf="consultation?.status !== 'completed'" (click)="toggleQuickMode()"><mat-icon>{{ quickMode ? 'view_agenda' : 'bolt' }}</mat-icon>{{ quickMode ? 'Flujo completo' : 'Consulta rápida' }}</button>
          <span class="status" [class.completed]="consultation?.status === 'completed'">
            {{ consultation?.status === 'completed' ? 'Completada' : 'En curso' }}
          </span>
          <button mat-stroked-button type="button" (click)="backToAgenda()"><mat-icon>arrow_back</mat-icon> Agenda</button>
        </div>
      </header>

      <div class="flow-layout">
        <aside class="stepper">
          <button type="button" *ngFor="let step of visibleSteps; let i = index"
            [class.active]="step.key === currentStep"
            [class.done]="isCompleted(step.key)"
            (click)="goToStep(step.key)">
            <span class="step-number"><mat-icon *ngIf="isCompleted(step.key)">check</mat-icon><span *ngIf="!isCompleted(step.key)">{{ i + 1 }}</span></span>
            <span><strong>{{ step.label }}</strong><small>{{ step.description }}</small></span>
          </button>
        </aside>

        <main class="step-content">
          <section class="card" *ngIf="currentStep === 'anamnesis'">
            <div class="card-title"><mat-icon>clinical_notes</mat-icon><div><h2>Anamnesis y toma de datos inicial</h2><p>Primer paso de la primera consulta: conocer la situación clínica, los antecedentes y el contexto del paciente antes de definir objetivos o dieta.</p></div></div>
            <div class="mode-banner"><mat-icon>medical_information</mat-icon><div><strong>Revisión clínica inicial</strong><span>Comprueba antecedentes y patologías, medicación y suplementos, alergias e intolerancias, restricciones alimentarias, síntomas digestivos, historial ponderal, hábitos, actividad física y expectativas. Registra solo la información pertinente y confirmada con el paciente.</span></div></div>
            <div class="form-grid">
              <label>Datos de anamnesis y toma de datos
                <textarea rows="10" [(ngModel)]="anamnesisText" (blur)="saveClinicalProgress()" placeholder="Motivo de consulta; antecedentes y patologías; medicación/suplementos; alergias e intolerancias; síntomas; historial de peso; hábitos y horarios; actividad física; contexto y barreras; otra información relevante…"></textarea>
                <small>Este registro queda guardado en el progreso de la consulta. Los datos identificativos y la documentación del paciente se gestionan desde su ficha.</small>
              </label>
            </div>
            <div class="actions">
              <button mat-stroked-button type="button" (click)="openPatient()"><mat-icon>folder_open</mat-icon> Abrir ficha y documentación</button>
            </div>
          </section>

          <section class="card" *ngIf="currentStep === 'summary'">
            <div class="card-title"><mat-icon>person</mat-icon><div><h2>{{ isFirstConsultation ? 'Preparación de la primera consulta' : 'Resumen del seguimiento' }}</h2><p>{{ isFirstConsultation ? 'Contexto inicial para establecer la línea de base y los objetivos del paciente.' : 'Información disponible antes de empezar la revisión clínica.' }}</p></div></div>
            <div class="mode-banner" [class.followup]="!isFirstConsultation">
              <mat-icon>{{ isFirstConsultation ? 'flag' : 'insights' }}</mat-icon>
              <div><strong>{{ isFirstConsultation ? 'Consulta inicial' : 'Consulta de seguimiento' }}</strong><span>{{ isFirstConsultation ? 'Prioriza datos de partida, objetivos, educación y configuración del plan.' : 'Prioriza evolución, check-in, señales detectadas y ajustes respecto a la consulta anterior.' }}</span></div>
            </div>
            <div class="summary-grid">
              <div><span>Paciente</span><strong>{{ data.appointment.clientName || 'Paciente' }}</strong></div>
              <div><span>Tipo</span><strong>{{ data.suggestedConsultationType === 'first' ? 'Primera consulta' : 'Seguimiento' }}</strong></div>
              <div><span>Cita</span><strong>{{ data.appointment.startsAt | date:'dd/MM/yyyy HH:mm' }}</strong></div>
              <div><span>Check-in</span><strong>{{ data.latestCheckin ? (data.latestCheckin.submitted_at | date:'dd/MM/yyyy HH:mm') : 'Sin check-in' }}</strong></div>
            </div>
            <div class="pre-summary" *ngIf="isFirstConsultation">
              <div class="summary-card"><span>Línea de base</span><strong>{{ data.latestCheckin ? 'Datos disponibles' : 'Pendiente' }}</strong><small>Registrar medidas y hábitos iniciales.</small></div>
              <div class="summary-card"><span>Objetivos</span><strong>{{ goalsText ? 'Ya definidos' : 'Por definir' }}</strong><small>Concretar prioridades del paciente.</small></div>
              <div class="summary-card"><span>Plan</span><strong>{{ activeDiet ? 'Dieta activa' : 'Por configurar' }}</strong><small>Decidir el plan de trabajo inicial.</small></div>
            </div>
            <div class="signal-panel" *ngIf="preConsultationSignals.length">
              <h3>Señales para revisar</h3>
              <div class="signal-list"><span *ngFor="let signal of preConsultationSignals">{{ signal.label }}<b *ngIf="signal.value !== null && signal.value !== undefined">{{ signal.value }}</b></span></div>
              <div class="recommendations" *ngIf="contextualRecommendations.length">
                <h4>Ideas para abordar con el paciente</h4>
                <ul><li *ngFor="let recommendation of contextualRecommendations">{{ recommendation }}</li></ul>
                <button mat-stroked-button type="button" (click)="openPatientMessages()"><mat-icon>chat</mat-icon> Abrir conversación</button>
              </div>
            </div>
            <div class="pre-summary" *ngIf="!isFirstConsultation && (previousAppointment || preConsultationTasks.length)">
              <div class="summary-card" *ngIf="previousAppointment"><span>Consulta anterior</span><strong>{{ previousAppointment.starts_at | date:'dd/MM/yyyy' }}</strong><small>{{ previousAppointment.professional_notes || 'Sin notas registradas.' }}</small></div>
              <div class="summary-card" *ngIf="preConsultationTasks.length"><span>Tareas abiertas</span><strong>{{ preConsultationTasks.length }}</strong><small>Revisar antes de cerrar el seguimiento.</small></div>
            </div>
          </section>

          <section class="card" *ngIf="currentStep === 'evolution'">
            <div class="card-title"><mat-icon>show_chart</mat-icon><div><h2>{{ isFirstConsultation ? 'Línea de base' : 'Evolución' }}</h2><p>{{ isFirstConsultation ? 'Comprueba qué datos iniciales están disponibles para establecer el punto de partida.' : 'Compara los últimos seguimientos y detecta cambios relevantes.' }}</p></div></div>
            <p class="helper">Resumen rápido de los últimos seguimientos registrados. Para las gráficas completas puedes abrir la ficha del paciente.</p>
            <div *ngIf="followupHistory.length" class="history-table">
              <div class="history-row history-head"><span>Fecha</span><span>Peso</span><span>Adherencia</span><span>Hambre</span><span>Energía</span></div>
              <div class="history-row" *ngFor="let checkin of followupHistory.slice(-6).reverse()">
                <span>{{ checkin.submitted_at | date:'dd/MM/yyyy' }}</span>
                <span>{{ checkin.weight ?? '—' }} kg</span>
                <span>{{ checkin.adherence ?? '—' }}/10</span>
                <span>{{ checkin.hunger ?? '—' }}/10</span>
                <span>{{ checkin.energy ?? '—' }}/10</span>
              </div>
            </div>
            <div class="empty" *ngIf="!followupHistory.length"><mat-icon>timeline</mat-icon><span>{{ isFirstConsultation ? 'No hay datos previos: utiliza la consulta para establecer la línea de base.' : 'No hay histórico de check-ins.' }}</span></div>
            <div class="quick-form" *ngIf="latestBiometric">
              <h3>Última valoración antropométrica</h3>
              <div class="metric-grid">
                <div><span>Fecha</span><strong>{{ latestBiometric.measurement_date | date:'dd/MM/yyyy' }}</strong></div>
                <div><span>Peso</span><strong>{{ latestBiometric.weight ?? '—' }} kg</strong></div>
                <div><span>Altura</span><strong>{{ latestBiometric.height ?? '—' }} cm</strong></div>
                <div><span>Grasa corporal</span><strong>{{ latestBiometric.body_fat ?? '—' }} %</strong></div>
              </div>
              <div class="change-grid" *ngIf="biometricChanges">
                <span *ngIf="biometricChanges.weight !== null && biometricChanges.weight !== undefined">Peso: {{ biometricChanges.weight > 0 ? '+' : '' }}{{ biometricChanges.weight | number:'1.1-1' }} kg</span>
                <span *ngIf="biometricChanges.bodyFat !== null && biometricChanges.bodyFat !== undefined">Grasa: {{ biometricChanges.bodyFat > 0 ? '+' : '' }}{{ biometricChanges.bodyFat | number:'1.1-1' }} pp</span>
                <span *ngIf="biometricChanges.muscleMass !== null && biometricChanges.muscleMass !== undefined">Masa muscular: {{ biometricChanges.muscleMass > 0 ? '+' : '' }}{{ biometricChanges.muscleMass | number:'1.1-1' }} kg</span>
                <span *ngIf="biometricChanges.waist !== null && biometricChanges.waist !== undefined">Cintura: {{ biometricChanges.waist > 0 ? '+' : '' }}{{ biometricChanges.waist | number:'1.1-1' }} cm</span>
              </div>
            </div>
            <button mat-stroked-button type="button" (click)="openPatient()"><mat-icon>open_in_new</mat-icon> Abrir ficha del paciente</button>
          </section>

          <section class="card" *ngIf="currentStep === 'checkin'">
            <div class="card-title"><mat-icon>fact_check</mat-icon><div><h2>{{ isFirstConsultation ? 'Datos iniciales' : 'Check-in y adherencia' }}</h2><p>{{ isFirstConsultation ? 'Utiliza los datos disponibles para establecer la situación de partida; el portal puede aportar nuevos check-ins posteriormente.' : 'Últimos datos estructurados comunicados por el paciente.' }}</p></div></div>
            <div *ngIf="data.latestCheckin; else noCheckin" class="metric-grid">
              <div><span>Adherencia</span><strong>{{ data.latestCheckin.adherence ?? '—' }}/10</strong></div>
              <div><span>Hambre</span><strong>{{ data.latestCheckin.hunger ?? '—' }}/10</strong></div>
              <div><span>Energía</span><strong>{{ data.latestCheckin.energy ?? '—' }}/10</strong></div>
              <div><span>Sueño</span><strong>{{ data.latestCheckin.sleep_quality ?? '—' }}/10 · {{ data.latestCheckin.sleep_hours ?? '—' }} h</strong></div>
              <div><span>Entrenamiento</span><strong>{{ data.latestCheckin.training ?? '—' }}/10</strong></div>
              <div><span>Peso</span><strong>{{ data.latestCheckin.weight ?? '—' }} kg</strong></div>
            </div>
            <ng-template #noCheckin><div class="empty"><mat-icon>assignment_late</mat-icon><span>No hay check-in registrado.</span></div></ng-template>
            <div class="actions" *ngIf="data.latestCheckin">
              <button mat-stroked-button type="button" *ngIf="!data.latestCheckin.reviewed_at" (click)="reviewCheckin()"><mat-icon>done</mat-icon> Marcar revisado</button>
              <span class="reviewed" *ngIf="data.latestCheckin.reviewed_at"><mat-icon>check_circle</mat-icon> Revisado</span>
            </div>
          </section>

          <section class="card" *ngIf="currentStep === 'goals'">
            <div class="card-title"><mat-icon>flag</mat-icon><div><h2>{{ isFirstConsultation ? 'Objetivos iniciales' : 'Cambios y objetivos' }}</h2><p>{{ isFirstConsultation ? 'Define las prioridades del paciente y los objetivos medibles de partida.' : 'Define aquí los puntos que quieres trabajar en la consulta.' }}</p></div></div>
            <div class="form-grid">
              <label>Objetivos y cambios acordados
                <textarea rows="7" [(ngModel)]="goalsText" (blur)="saveClinicalProgress()" [placeholder]="isFirstConsultation ? 'Objetivos iniciales, prioridades del paciente, metas medibles…' : 'Objetivos concretos, cambios acordados, prioridades para la siguiente revisión…'"></textarea>
              </label>
              <label>Decisiones clínicas y acuerdos
                <textarea rows="5" [(ngModel)]="decisionSummary" (blur)="saveClinicalProgress()" placeholder="Decisiones tomadas, ajustes aprobados y acuerdos con el paciente…"></textarea>
              </label>
              <label>Acciones y tareas acordadas
                <textarea rows="5" [(ngModel)]="actionsSummary" (blur)="saveClinicalProgress()" placeholder="Una acción por línea. Al cerrar la consulta, cada línea se convertirá en una tarea pendiente…"></textarea>
                <small>Al cerrar, las líneas no vacías se crearán como tareas abiertas del paciente. Puedes escribir una acción por línea o usar viñetas.</small>
              </label>
              <label>Observaciones clínicas
                <textarea rows="5" [(ngModel)]="clinicalNotes" (blur)="saveClinicalProgress()" placeholder="Observaciones relevantes de la consulta…"></textarea>
              </label>
            </div>
          </section>

          <section class="card" *ngIf="currentStep === 'diet'">
            <div class="card-title"><mat-icon>restaurant</mat-icon><div><h2>{{ isFirstConsultation ? 'Plan nutricional inicial' : 'Dieta y ajustes' }}</h2><p>{{ isFirstConsultation ? 'Configura el plan de partida o deja preparada la dieta para el paciente.' : 'Revisa el plan activo y decide si necesita ajustes.' }}</p></div></div>
            <div *ngIf="activeDiet" class="summary-grid">
              <div><span>Nombre</span><strong>{{ activeDiet.name || 'Sin nombre' }}</strong></div>
              <div><span>Calorías</span><strong>{{ activeDiet.targetKcal ?? '—' }} kcal</strong></div>
              <div><span>Proteína</span><strong>{{ activeDiet.targetProtein ?? '—' }} g</strong></div>
              <div><span>Carbohidratos</span><strong>{{ activeDiet.targetCarbs ?? '—' }} g</strong></div>
            </div>
            <div class="empty" *ngIf="!activeDiet"><mat-icon>restaurant</mat-icon><span>No hay una dieta activa asignada.</span></div>
            <div class="actions"><button mat-stroked-button type="button" (click)="openPatientDiet()"><mat-icon>open_in_new</mat-icon> Abrir dietas</button></div>
            <div class="quick-form">
              <h3>Crear dieta rápida</h3>
              <div class="inline-fields">
                <label>Nombre<input [(ngModel)]="quickDietName" maxlength="200" placeholder="Ej. Plan de seguimiento octubre"></label>
                <label>Kcal<input type="number" [(ngModel)]="quickDietKcal"></label>
                <label>Proteína (g)<input type="number" [(ngModel)]="quickDietProtein"></label>
                <label>Carbohidratos (g)<input type="number" [(ngModel)]="quickDietCarbs"></label>
                <label>Grasa (g)<input type="number" [(ngModel)]="quickDietFat"></label>
              </div>
              <button mat-flat-button color="primary" type="button" (click)="createQuickDiet()" [disabled]="!quickDietName.trim() || creatingDiet">
                <mat-icon>add</mat-icon>{{ creatingDiet ? 'Creando…' : 'Crear y abrir dieta' }}
              </button>
            </div>
          </section>

          <section class="card" *ngIf="currentStep === 'education'">
            <div class="card-title"><mat-icon>school</mat-icon><div><h2>{{ isFirstConsultation ? 'Educación inicial' : 'Educación y recomendaciones' }}</h2><p>{{ isFirstConsultation ? 'Deja registrados los conceptos y hábitos explicados en la primera consulta.' : 'Registra los puntos que quieres reforzar con el paciente.' }}</p></div></div>
            <div class="form-grid">
              <label>Plan para la próxima consulta
                <textarea rows="5" [(ngModel)]="nextConsultationPlan" (blur)="saveClinicalProgress()" placeholder="Qué revisar en la próxima visita, objetivos o datos que deben estar disponibles…"></textarea>
              </label>
              <label>Educación y recomendaciones
                <textarea rows="8" [(ngModel)]="educationText" (blur)="saveClinicalProgress()" placeholder="Recomendaciones explicadas, hábitos trabajados, educación nutricional…"></textarea>
              </label>
            </div>
            <small class="save-state" *ngIf="clinicalSaveState === 'saving'">Guardando…</small>
            <small class="save-state saved" *ngIf="clinicalSaveState === 'saved'">Guardado</small>
            <div class="actions" *ngIf="clinicalSaveState === 'error'">
              <small class="save-state">No se han guardado los cambios.</small>
              <button mat-stroked-button type="button" (click)="saveClinicalProgress()">Reintentar guardado</button>
            </div>
          </section>

          <section class="card" *ngIf="currentStep === 'tasks'">
            <div class="card-title"><mat-icon>task_alt</mat-icon><div><h2>{{ isFirstConsultation ? 'Plan de seguimiento' : 'Tareas' }}</h2><p>{{ isFirstConsultation ? 'Deja preparadas las acciones y tareas que marcarán el seguimiento inicial.' : 'Acciones profesionales pendientes relacionadas con este paciente.' }}</p></div></div>
            <div class="task-list" *ngIf="pendingTasks.length">
              <div class="task-item" *ngFor="let task of pendingTasks">
                <div><strong>{{ task.title }}</strong><small>{{ task.dueAt ? ('Vence ' + (task.dueAt | date:'dd/MM/yyyy')) : 'Sin fecha límite' }}</small></div>
                <span>{{ task.priority }}</span>
              </div>
            </div>
            <div class="empty" *ngIf="!pendingTasks.length"><mat-icon>task_alt</mat-icon><span>No hay tareas abiertas para este paciente.</span></div>
            <button mat-stroked-button type="button" *ngIf="data.latestCheckin" (click)="createFollowUpTask()" [disabled]="taskCreating">
              <mat-icon>{{ taskCreating ? 'hourglass_top' : 'add_task' }}</mat-icon>
              {{ taskCreating ? 'Creando tarea…' : 'Crear tarea desde el seguimiento' }}
            </button>
            <div class="quick-form">
              <h3>Crear tarea manual</h3>
              <div class="form-grid">
                <label>Título<input [(ngModel)]="taskTitle" maxlength="200" placeholder="Ej. Revisar adherencia dentro de una semana"></label>
                <label>Descripción<textarea rows="3" [(ngModel)]="taskDescription" maxlength="2000"></textarea></label>
                <div class="inline-fields">
                  <label>Prioridad<select [(ngModel)]="taskPriority"><option value="low">Baja</option><option value="normal">Normal</option><option value="high">Alta</option></select></label>
                  <label>Fecha límite<input type="date" [(ngModel)]="taskDueAt"></label>
                </div>
              </div>
              <button mat-flat-button color="primary" type="button" (click)="createManualTask()" [disabled]="!taskTitle.trim() || taskCreating">
                <mat-icon>add_task</mat-icon> Crear tarea
              </button>
            </div>
          </section>

          <section class="card" *ngIf="currentStep === 'next_appointment'">
            <div class="card-title"><mat-icon>event</mat-icon><div><h2>Próxima cita</h2><p>Comprueba si ya existe una visita futura antes de cerrar la consulta.</p></div></div>
            <div class="summary-grid" *ngIf="nextAppointment">
              <div><span>Fecha</span><strong>{{ nextAppointment.startsAt | date:'dd/MM/yyyy HH:mm' }}</strong></div>
              <div><span>Estado</span><strong>{{ nextAppointment.status }}</strong></div>
            </div>
            <div class="empty" *ngIf="!nextAppointment"><mat-icon>event_busy</mat-icon><span>No hay una próxima cita confirmada.</span></div>
            <div class="quick-form">
              <h3>Programar siguiente cita</h3>
              <div class="inline-fields">
                <label>Duración<select [(ngModel)]="slotDuration" (ngModelChange)="loadConsultationSlots()"><option [ngValue]="30">30 min</option><option [ngValue]="45">45 min</option><option [ngValue]="60">60 min</option></select></label>
                <div class="slot-status">{{ slotsLoading ? 'Buscando huecos…' : consultationSlots.length ? 'Huecos disponibles' : 'No hay huecos en los próximos 30 días' }}</div>
              </div>
              <div class="slot-list" *ngIf="consultationSlots.length">
                <button type="button" *ngFor="let slot of consultationSlots" [class.selected]="selectedSlot === slot.startsAt" (click)="selectedSlot = slot.startsAt">
                  {{ slot.startsAt | date:'EEE dd/MM · HH:mm' }}
                </button>
              </div>
              <button mat-flat-button color="primary" type="button" (click)="bookNextAppointment()" [disabled]="!selectedSlot || bookingAppointment">
                <mat-icon>event_available</mat-icon>{{ bookingAppointment ? 'Programando…' : 'Confirmar próxima cita' }}
              </button>
            </div>
            <button mat-stroked-button type="button" (click)="backToAgenda()"><mat-icon>calendar_month</mat-icon> Abrir agenda</button>
          </section>

          <section class="card close-card" *ngIf="currentStep === 'close'">
            <div class="card-title"><mat-icon>check_circle</mat-icon><div><h2>Cerrar consulta</h2><p>Al cerrar se marca la consulta como completada y, si procede, la cita como completada.</p></div></div>
            <div class="quick-form">
              <h3>Acciones posteriores</h3>
              <div class="actions" *ngIf="clinicalSaveState === 'error'">
                <span class="save-state">Hay cambios sin guardar. Reintenta el guardado antes de cerrar.</span>
                <button mat-stroked-button type="button" (click)="saveClinicalProgress()">Reintentar guardado</button>
              </div>
              <p>Al cerrar se generarán las tareas a partir de las acciones acordadas. Después puedes revisar la ficha, la documentación y los mensajes del paciente.</p>
              <div class="actions">
                <button mat-stroked-button type="button" (click)="openPatient()"><mat-icon>folder_open</mat-icon> Ficha y documentación</button>
                <button mat-stroked-button type="button" (click)="openPatientMessages()"><mat-icon>chat</mat-icon> Mensajes</button>
              </div>
            </div>
            <button mat-flat-button color="primary" type="button" (click)="completeConsultation()" [disabled]="consultation?.status === 'completed' || completing || clinicalSaveState === 'saving' || clinicalSaveState === 'error'">
              <mat-icon>{{ completing ? 'hourglass_top' : 'check' }}</mat-icon>
              {{ completing ? 'Cerrando…' : 'Cerrar consulta' }}
            </button>
          </section>

          <footer class="navigation">
            <button mat-stroked-button type="button" (click)="previousStep()" [disabled]="stepIndex === 0"><mat-icon>arrow_back</mat-icon> Anterior</button>
            <button mat-flat-button color="primary" type="button" (click)="nextStep()" *ngIf="stepIndex < visibleSteps.length - 1" [disabled]="savingProgress">
              {{ isCompleted(currentStep) ? 'Siguiente' : 'Marcar paso y continuar' }} <mat-icon>arrow_forward</mat-icon>
            </button>
            <button mat-stroked-button type="button" *ngIf="stepIndex === visibleSteps.length - 1" (click)="goToStep(visibleSteps[0].key)"><mat-icon>replay</mat-icon> Volver al inicio</button>
          </footer>
        </main>
      </div>
    </div>

    <div class="loading" *ngIf="loading"><mat-spinner diameter="40"></mat-spinner><span>Preparando consulta…</span></div>
  `,
  styles: [`
    .consultation-page{width:100%;padding:28px 24px 44px;box-sizing:border-box}.consultation-header{display:flex;justify-content:space-between;gap:20px;align-items:flex-end;margin-bottom:22px}.eyebrow{font-size:11px;font-weight:800;letter-spacing:1.2px;color:#0f766e}.consultation-header h1{margin:5px 0 4px;font-size:30px;color:#0f172a}.consultation-header p{margin:0;color:#64748b}.header-actions{display:flex;align-items:center;gap:12px}.status{padding:7px 11px;border-radius:999px;background:#fef3c7;color:#92400e;font-size:12px;font-weight:700}.status.completed{background:#dcfce7;color:#166534}.flow-layout{display:grid;grid-template-columns:280px minmax(0,1fr);gap:20px;align-items:start}.stepper{position:sticky;top:18px;display:grid;gap:6px}.stepper button{border:1px solid transparent;background:#f8fafc;border-radius:11px;padding:10px;text-align:left;display:flex;gap:10px;align-items:center;cursor:pointer}.stepper button.active{background:#ecfeff;border-color:#99f6e4}.stepper button.done{background:#f0fdf4}.step-number{width:28px;height:28px;border-radius:50%;display:grid;place-items:center;background:#e2e8f0;color:#475569;font-size:12px;font-weight:700;flex:none}.stepper button.active .step-number{background:#0f766e;color:#fff}.stepper button.done .step-number{background:#16a34a;color:#fff}.stepper strong{display:block;font-size:12px;color:#0f172a}.stepper small{display:block;color:#64748b;font-size:10px;margin-top:2px}.step-content{min-width:0}.card{background:#fff;border:1px solid #e2e8f0;border-radius:15px;padding:22px;min-height:270px;box-sizing:border-box}.card-title{display:flex;gap:12px;align-items:flex-start;margin-bottom:20px}.card-title>mat-icon{color:#0f766e}.card-title h2{margin:0 0 4px;font-size:20px;color:#0f172a}.card-title p{margin:0;color:#64748b;font-size:12px}.summary-grid,.metric-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}.history-table{border:1px solid #e2e8f0;border-radius:10px;overflow:hidden;margin:14px 0}.history-row{display:grid;grid-template-columns:1.2fr repeat(4,1fr);gap:8px;padding:10px 12px;border-top:1px solid #e2e8f0;font-size:12px;color:#334155}.history-head{border-top:0;background:#f8fafc;font-weight:700;color:#64748b}.task-list{display:grid;gap:8px;margin-bottom:16px}.pre-summary{display:grid;grid-template-columns:repeat(auto-fit,minmax(150px,1fr));gap:10px;margin-bottom:16px}.summary-card{padding:14px;border:1px solid #e2e8f0;border-radius:11px;background:#fff;display:grid;gap:5px}.summary-card span{font-size:11px;color:#64748b}.summary-card strong{font-size:16px;color:#0f172a}.summary-card small{font-size:11px;color:#64748b}.mode-banner{display:flex;gap:10px;align-items:flex-start;padding:14px;border:1px solid #cbd5e1;border-radius:11px;background:#f8fafc;margin-bottom:16px}.mode-banner>mat-icon{color:#0f766e}.mode-banner strong{display:block;font-size:13px;color:#0f172a}.mode-banner span{display:block;margin-top:3px;font-size:12px;color:#64748b;line-height:1.45}.mode-banner.followup{background:#eff6ff;border-color:#bfdbfe}.mode-banner.followup>mat-icon{color:#2563eb}.signal-panel{margin-bottom:18px;padding:15px;border:1px solid #fed7aa;border-radius:11px;background:#fff7ed}.signal-panel h3{margin:0 0 10px;font-size:14px}.signal-list{display:flex;flex-wrap:wrap;gap:8px}.signal-list span{padding:7px 10px;border-radius:999px;background:#ffedd5;font-size:12px;color:#9a3412}.signal-list b{margin-left:4px}.recommendations{margin-top:14px;padding-top:12px;border-top:1px solid #fed7aa}.recommendations h4{margin:0 0 8px;font-size:12px;color:#7c2d12}.recommendations ul{margin:0 0 12px;padding-left:20px;color:#7c2d12;font-size:12px;line-height:1.6}.quick-form{margin-top:18px;padding:15px;border:1px solid #e2e8f0;border-radius:11px;background:#f8fafc}.quick-form h3{margin:0 0 12px;font-size:14px;color:#0f172a}.inline-fields{display:flex;gap:12px;flex-wrap:wrap}.inline-fields label{flex:1;min-width:130px;display:grid;gap:6px;font-size:11px;font-weight:700;color:#334155}.inline-fields input,.inline-fields select{box-sizing:border-box;width:100%;border:1px solid #cbd5e1;border-radius:8px;padding:9px;background:#fff;font:inherit}.slot-status{align-self:end;padding:10px;color:#64748b;font-size:12px}.slot-list{display:flex;gap:8px;flex-wrap:wrap;margin:14px 0}.slot-list button{border:1px solid #cbd5e1;background:#fff;border-radius:9px;padding:9px 11px;cursor:pointer;color:#334155}.slot-list button.selected{border-color:#0f766e;background:#ecfeff;color:#0f766e;font-weight:700}.task-item{display:flex;justify-content:space-between;gap:12px;padding:12px;border:1px solid #e2e8f0;border-radius:10px;background:#f8fafc}.task-item strong{display:block;font-size:13px;color:#0f172a}.task-item small{display:block;margin-top:3px;color:#64748b;font-size:11px}.task-item>span{font-size:10px;font-weight:800;text-transform:uppercase;color:#0f766e}.form-grid{display:grid;gap:16px}.form-grid label{display:grid;gap:7px;font-size:12px;font-weight:700;color:#334155}.form-grid textarea{width:100%;box-sizing:border-box;border:1px solid #cbd5e1;border-radius:10px;padding:12px;font:inherit;font-weight:400;resize:vertical;min-height:90px}.save-state{display:block;margin-top:8px;color:#64748b}.save-state.saved{color:#15803d}.change-grid{display:flex;flex-wrap:wrap;gap:8px;margin-top:12px}.change-grid span{padding:7px 10px;border-radius:999px;background:#ecfeff;color:#0f766e;font-size:11px;font-weight:700}.summary-grid>div,.metric-grid>div{padding:14px;border-radius:10px;background:#f8fafc}.summary-grid span,.metric-grid span{display:block;color:#64748b;font-size:11px}.summary-grid strong,.metric-grid strong{display:block;margin-top:4px;color:#0f172a;font-size:14px}.helper{padding:14px;border-radius:10px;background:#f8fafc;color:#64748b;font-size:13px;line-height:1.5;margin-bottom:15px}.placeholder{min-height:130px;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:8px;color:#94a3b8;text-align:center}.placeholder mat-icon{font-size:38px;width:38px;height:38px}.actions{display:flex;align-items:center;gap:12px;margin-top:18px}.reviewed{display:flex;align-items:center;gap:6px;color:#15803d;font-size:12px}.navigation{display:flex;justify-content:space-between;gap:10px;margin-top:16px}.empty{min-height:120px;display:flex;align-items:center;justify-content:center;gap:8px;color:#94a3b8}.loading{min-height:60vh;display:flex;flex-direction:column;align-items:center;justify-content:center;gap:12px;color:#64748b}@media(max-width:850px){.consultation-page{padding:20px 14px 36px}.consultation-header{align-items:stretch;flex-direction:column}.header-actions{justify-content:space-between}.flow-layout{grid-template-columns:1fr}.stepper{position:static;display:flex;overflow:auto;padding-bottom:3px}.stepper button{min-width:190px}.summary-grid,.metric-grid{grid-template-columns:1fr}.history-row{grid-template-columns:1fr 1fr}.history-head{display:none}}
  `]
})
export class GuidedConsultationComponent implements OnInit {
  private readonly loadingState = signal(true);
  get loading(): boolean { return this.loadingState(); }
  set loading(value: boolean) { this.loadingState.set(value); }
  private readonly taskCreatingState = signal(false);
  get taskCreating(): boolean { return this.taskCreatingState(); }
  set taskCreating(value: boolean) { this.taskCreatingState.set(value); }
  private readonly completingState = signal(false);
  get completing(): boolean { return this.completingState(); }
  set completing(value: boolean) { this.completingState.set(value); }
  private readonly dataState = signal<GuidedConsultationResponse | null>(null);
  get data(): GuidedConsultationResponse | null { return this.dataState(); }
  set data(value: GuidedConsultationResponse | null) { this.dataState.set(value); }
  private readonly consultationState = signal<any>(null);
  get consultation(): any { return this.consultationState(); }
  set consultation(value: any) { this.consultationState.set(value); }
  private readonly followupHistoryState = signal<PatientCheckin[]>([]);
  get followupHistory(): PatientCheckin[] { return this.followupHistoryState(); }
  set followupHistory(value: PatientCheckin[]) { this.followupHistoryState.set(value); }
  anamnesisText = '';
  savingProgress = false;
  goalsText = '';
  educationText = '';
  clinicalNotes = '';
  decisionSummary = '';
  actionsSummary = '';
  nextConsultationPlan = '';
  private clinicalSaveQueued = false;
  private readonly clinicalSaveStateState = signal<'idle' | 'saving' | 'saved' | 'error'>('idle');
  get clinicalSaveState(): 'idle' | 'saving' | 'saved' | 'error' { return this.clinicalSaveStateState(); }
  set clinicalSaveState(value: 'idle' | 'saving' | 'saved' | 'error') { this.clinicalSaveStateState.set(value); }
  private readonly activeDietState = signal<any>(null);
  get activeDiet(): any { return this.activeDietState(); }
  set activeDiet(value: any) { this.activeDietState.set(value); }
  private readonly pendingTasksState = signal<any[]>([]);
  get pendingTasks(): any[] { return this.pendingTasksState(); }
  set pendingTasks(value: any[]) { this.pendingTasksState.set(value); }
  private readonly nextAppointmentState = signal<PatientAppointment | null>(null);
  get nextAppointment(): PatientAppointment | null { return this.nextAppointmentState(); }
  set nextAppointment(value: PatientAppointment | null) { this.nextAppointmentState.set(value); }
  private readonly preConsultationSignalsState = signal<any[]>([]);
  get preConsultationSignals(): any[] { return this.preConsultationSignalsState(); }
  set preConsultationSignals(value: any[]) { this.preConsultationSignalsState.set(value); }
  private readonly previousAppointmentState = signal<any>(null);
  get previousAppointment(): any { return this.previousAppointmentState(); }
  set previousAppointment(value: any) { this.previousAppointmentState.set(value); }
  private readonly preConsultationTasksState = signal<any[]>([]);
  get preConsultationTasks(): any[] { return this.preConsultationTasksState(); }
  set preConsultationTasks(value: any[]) { this.preConsultationTasksState.set(value); }
  private readonly consultationSlotsState = signal<AppointmentSlot[]>([]);
  get consultationSlots(): AppointmentSlot[] { return this.consultationSlotsState(); }
  set consultationSlots(value: AppointmentSlot[]) { this.consultationSlotsState.set(value); }
  slotDuration = 30;
  quickMode = false;
  get visibleSteps(): ConsultationStep[] {
    const firstConsultationOrder = ['anamnesis', 'evolution', 'checkin', 'goals', 'diet', 'education', 'tasks', 'next_appointment', 'close'];
    const followUpOrder = ['summary', 'evolution', 'checkin', 'goals', 'diet', 'education', 'tasks', 'next_appointment', 'close'];
    const quickFirstOrder = ['anamnesis', 'goals', 'diet', 'tasks', 'close'];
    const quickFollowUpOrder = ['summary', 'goals', 'diet', 'tasks', 'close'];
    const order = this.quickMode
      ? (this.isFirstConsultation ? quickFirstOrder : quickFollowUpOrder)
      : (this.isFirstConsultation ? firstConsultationOrder : followUpOrder);
    return order.map(key => this.steps.find(step => step.key === key)).filter((step): step is ConsultationStep => !!step);
  }
  selectedSlot: string | null = null;
  private readonly slotsLoadingState = signal(false);
  get slotsLoading(): boolean { return this.slotsLoadingState(); }
  set slotsLoading(value: boolean) { this.slotsLoadingState.set(value); }
  private readonly bookingAppointmentState = signal(false);
  get bookingAppointment(): boolean { return this.bookingAppointmentState(); }
  set bookingAppointment(value: boolean) { this.bookingAppointmentState.set(value); }
  taskTitle = '';
  taskDescription = '';
  taskPriority = 'normal';
  taskDueAt = '';
  quickDietName = '';
  quickDietKcal: number | null = null;
  quickDietProtein: number | null = null;
  quickDietCarbs: number | null = null;
  quickDietFat: number | null = null;
  private readonly creatingDietState = signal(false);
  get creatingDiet(): boolean { return this.creatingDietState(); }
  set creatingDiet(value: boolean) { this.creatingDietState.set(value); }
  private readonly currentStepState = signal('summary');
  get currentStep(): string { return this.currentStepState(); }
  set currentStep(value: string) { this.currentStepState.set(value); }
  private readonly stepIndexState = signal(0);
  get stepIndex(): number { return this.stepIndexState(); }
  set stepIndex(value: number) { this.stepIndexState.set(value); }

  get isFirstConsultation(): boolean {
    return this.data?.suggestedConsultationType === 'first' || this.consultation?.consultationType === 'first';
  }

  get contextualRecommendations(): string[] {
    const recommendations: Record<string, string> = {
      adherence_low: 'Explorar barreras prácticas y acordar un cambio pequeño y realista.',
      hunger_high: 'Revisar saciedad, distribución de comidas y tolerancia al plan actual.',
      energy_low: 'Preguntar por descanso, rutina, ingesta y cambios recientes antes de ajustar el plan.',
      sleep_quality_low: 'Explorar factores que estén afectando al descanso y acordar qué conviene revisar.',
      sleep_hours_low: 'Revisar horarios y duración del sueño, sin atribuir la causa de antemano.',
      training_low: 'Comprobar cambios en actividad, recuperación y objetivos de movimiento.'
    };
    return (this.preConsultationSignals || [])
      .map(signal => recommendations[signal.code])
      .filter((value): value is string => !!value);
  }

  get latestBiometric(): any | null {
    return this.data?.biometrics?.latest ?? null;
  }

  get biometricChanges(): { weight?: number | null; bodyFat?: number | null; muscleMass?: number | null; waist?: number | null } | null {
    return this.data?.biometrics?.changes ?? null;
  }

  readonly steps: ConsultationStep[] = [
    { key:'anamnesis', label:'Anamnesis', icon:'clinical_notes', description:'Datos e historia inicial' },
    { key:'summary', label:'Resumen', icon:'person', description:'Contexto previo del seguimiento' },
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
    private dietService: DietService,
    private snack: MatSnackBar
  ) {}

  ngOnInit(): void {
    const appointmentId = Number(this.route.snapshot.paramMap.get('appointmentId'));
    if (!appointmentId) { this.backToAgenda(); return; }

    this.portalService.getGuidedConsultation(appointmentId).subscribe({
      next: value => {
        this.data = value;
        this.consultation = value.consultation;
        this.loadClinicalProgress();
        this.loadFollowupHistory();
        this.loadConsultationContext();
        this.preConsultationSignals = value.followupSignals ?? [];
        this.previousAppointment = value.previousAppointment ?? null;
        this.preConsultationTasks = value.openTasks ?? [];
        if (this.consultation) {
          const storedStep = this.consultation.currentStep || 'summary';
          this.currentStep = this.isFirstConsultation && storedStep === 'summary' && !(this.consultation.completedSteps || []).includes('summary')
            ? 'anamnesis'
            : storedStep;
          this.stepIndex = Math.max(0, this.visibleSteps.findIndex(x => x.key === this.currentStep));
          this.currentStep = this.visibleSteps[this.stepIndex]?.key || this.visibleSteps[0].key;
          this.loading = false;
          if (this.currentStep !== storedStep) this.saveProgress();
          if (this.currentStep === 'next_appointment') this.loadConsultationSlots();
        } else {
          this.portalService.startGuidedConsultation(appointmentId, value.suggestedConsultationType).subscribe({
            next: consultation => {
              this.consultation = consultation;
              this.currentStep = this.isFirstConsultation ? 'anamnesis' : (consultation.currentStep || 'summary');
              this.stepIndex = Math.max(0, this.visibleSteps.findIndex(x => x.key === this.currentStep));
              this.loading = false;
              if (this.currentStep !== consultation.currentStep) this.saveProgress();
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

  loadClinicalProgress(): void {
    const progress = this.consultation?.progress;
    if (!progress || typeof progress !== 'object') return;
    this.anamnesisText = typeof progress.anamnesis === 'string' ? progress.anamnesis : '';
    this.goalsText = typeof progress.goals === 'string' ? progress.goals : '';
    this.educationText = typeof progress.education === 'string' ? progress.education : '';
    this.clinicalNotes = typeof progress.clinicalNotes === 'string' ? progress.clinicalNotes : '';
    this.decisionSummary = typeof progress.decisionSummary === 'string' ? progress.decisionSummary : (this.consultation?.decisionSummary || '');
    this.actionsSummary = typeof progress.actionsSummary === 'string' ? progress.actionsSummary : (this.consultation?.actionsSummary || '');
    this.nextConsultationPlan = typeof progress.nextConsultationPlan === 'string' ? progress.nextConsultationPlan : (this.consultation?.nextConsultationPlan || '');
  }

  loadFollowupHistory(): void {
    const clientId = this.data?.appointment.clientId;
    if (!clientId) return;
    this.portalService.getCheckins(clientId).subscribe({
      next: checkins => this.followupHistory = [...checkins].sort((a, b) =>
        new Date(a.submitted_at).getTime() - new Date(b.submitted_at).getTime()),
      error: () => this.followupHistory = []
    });
  }

  loadConsultationContext(): void {
    const clientId = this.data?.appointment.clientId;
    if (!clientId) return;

    this.dietService.getActiveDiet(clientId).subscribe({
      next: diet => this.activeDiet = diet,
      error: () => this.activeDiet = null
    });

    this.portalService.getProfessionalTasks('open', 200).subscribe({
      next: tasks => this.pendingTasks = (tasks || []).filter(t => t.clientId === clientId).slice(0, 8),
      error: () => this.pendingTasks = []
    });

    this.portalService.getProfessionalAppointments().subscribe({
      next: appointments => {
        const now = Date.now();
        this.nextAppointment = (appointments || [])
          .filter(a => a.clientId === clientId && a.id !== this.data?.appointment.id &&
            a.status === 'confirmed' && new Date(a.startsAt).getTime() > now)
          .sort((a, b) => new Date(a.startsAt).getTime() - new Date(b.startsAt).getTime())[0] || null;
      },
      error: () => this.nextAppointment = null
    });
  }

  private buildClinicalProgress(): Record<string, unknown> {
    return {
      ...(this.consultation?.progress || {}),
      anamnesis: this.anamnesisText.trim(),
      goals: this.goalsText.trim(),
      education: this.educationText.trim(),
      clinicalNotes: this.clinicalNotes.trim(),
      decisionSummary: this.decisionSummary.trim(),
      actionsSummary: this.actionsSummary.trim(),
      nextConsultationPlan: this.nextConsultationPlan.trim()
    };
  }

  private flushQueuedClinicalSave(): void {
    if (!this.clinicalSaveQueued) return;
    this.clinicalSaveQueued = false;
    this.saveClinicalProgress();
  }

  saveClinicalProgress(): void {
    if (!this.consultation || !this.data || this.consultation.status === 'completed') return;
    if (this.clinicalSaveState === 'saving') {
      this.clinicalSaveQueued = true;
      return;
    }
    this.clinicalSaveState = 'saving';
    this.portalService.updateGuidedConsultationProgress(this.data.appointment.id, {
      currentStep: this.currentStep,
      completedSteps: this.consultation.completedSteps || [],
      progress: this.buildClinicalProgress()
    }).subscribe({
      next: value => {
        this.consultation = value;
        this.clinicalSaveState = 'saved';
        this.flushQueuedClinicalSave();
      },
      error: () => {
        this.clinicalSaveState = 'error';
        this.flushQueuedClinicalSave();
      }
    });
  }

  isCompleted(step: string): boolean {
    return !!this.consultation?.completedSteps?.includes(step);
  }

  toggleQuickMode(): void {
    this.quickMode = !this.quickMode;
    if (!this.visibleSteps.some(step => step.key === this.currentStep)) {
      this.currentStep = 'summary';
      this.stepIndex = 0;
      this.saveProgress();
    } else {
      this.stepIndex = this.visibleSteps.findIndex(step => step.key === this.currentStep);
    }
  }

  goToStep(step: string): void {
    const index = this.visibleSteps.findIndex(x => x.key === step);
    if (index < 0) return;
    this.stepIndex = index;
    this.currentStep = this.visibleSteps[index].key;
    this.saveProgress();
    if (step === 'next_appointment' && !this.consultationSlots.length && !this.slotsLoading) this.loadConsultationSlots();
  }

  nextStep(): void {
    if (!this.consultation || this.stepIndex >= this.visibleSteps.length - 1) return;
    const completed = new Set<string>(this.consultation.completedSteps || []);
    completed.add(this.currentStep);
    const next = this.visibleSteps[this.stepIndex + 1].key;
    this.persistProgress(next, [...completed]);
    if (next === 'next_appointment' && !this.consultationSlots.length && !this.slotsLoading) this.loadConsultationSlots();
  }

  previousStep(): void {
    if (this.stepIndex <= 0) return;
    this.stepIndex--;
    this.currentStep = this.visibleSteps[this.stepIndex].key;
    this.saveProgress();
  }

  private saveProgress(): void {
    this.persistProgress(this.currentStep, this.consultation?.completedSteps || []);
  }

  private persistProgress(step: string, completedSteps: string[]): void {
    if (!this.consultation || !this.data || this.savingProgress) return;
    const previousStep = this.currentStep;
    this.savingProgress = true;
    // Navegación optimista: la interfaz responde inmediatamente, pero revierte el paso
    // si el servidor rechaza el guardado y muestra el error al profesional.
    this.currentStep = step;
    this.stepIndex = Math.max(0, this.visibleSteps.findIndex(x => x.key === step));
    this.portalService.updateGuidedConsultationProgress(this.data.appointment.id, {
      currentStep: step,
      completedSteps,
      progress: this.buildClinicalProgress()
    }).subscribe({
      next: value => {
        this.consultation = value;
        const persistedStep = this.visibleSteps.some(x => x.key === value.currentStep) ? value.currentStep : step;
        this.currentStep = persistedStep;
        this.stepIndex = Math.max(0, this.visibleSteps.findIndex(x => x.key === persistedStep));
        this.savingProgress = false;
      },
      error: err => {
        this.currentStep = previousStep;
        this.stepIndex = Math.max(0, this.visibleSteps.findIndex(x => x.key === previousStep));
        this.savingProgress = false;
        this.snack.open(err?.error?.message || 'No se ha podido guardar el progreso. El paso anterior se ha mantenido.', 'Cerrar', { duration: 4000 });
      }
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
        this.loadConsultationContext();
      },
      error: err => {
        this.taskCreating = false;
        this.snack.open(err?.error?.message || 'No se ha podido crear la tarea.', 'Cerrar', { duration: 3000 });
      }
    });
  }

  createManualTask(): void {
    if (!this.data || !this.taskTitle.trim() || this.taskCreating) return;
    this.taskCreating = true;
    this.portalService.createProfessionalTask({
      clientId: this.data.appointment.clientId,
      title: this.taskTitle.trim(),
      description: this.taskDescription.trim() || null,
      priority: this.taskPriority,
      dueAt: this.taskDueAt ? new Date(this.taskDueAt + 'T23:59:59').toISOString() : null
    }).subscribe({
      next: () => {
        this.taskCreating = false;
        this.taskTitle = ''; this.taskDescription = ''; this.taskDueAt = '';
        this.snack.open('Tarea creada.', 'Cerrar', { duration: 1800 });
        this.loadConsultationContext();
      },
      error: err => {
        this.taskCreating = false;
        this.snack.open(err?.error?.message || 'No se ha podido crear la tarea.', 'Cerrar', { duration: 3000 });
      }
    });
  }

  loadConsultationSlots(): void {
    const clientId = this.data?.appointment.clientId;
    if (!clientId) return;
    this.slotsLoading = true;
    this.selectedSlot = null;
    this.portalService.getConsultationSlots(clientId, this.slotDuration, 30).subscribe({
      next: slots => { this.consultationSlots = slots; this.slotsLoading = false; },
      error: err => { this.consultationSlots = []; this.slotsLoading = false; this.snack.open(err?.error?.message || 'No se han podido consultar los huecos.', 'Cerrar', { duration: 3000 }); }
    });
  }

  bookNextAppointment(): void {
    const clientId = this.data?.appointment.clientId;
    if (!clientId || !this.selectedSlot || this.bookingAppointment) return;
    this.bookingAppointment = true;
    this.portalService.createConsultationAppointment({
      clientId,
      startsAt: this.selectedSlot,
      durationMinutes: this.slotDuration,
      notes: 'Programada desde la consulta guiada.'
    }).subscribe({
      next: appointment => {
        this.bookingAppointment = false;
        this.nextAppointment = appointment;
        this.consultationSlots = this.consultationSlots.filter(s => s.startsAt !== this.selectedSlot);
        this.selectedSlot = null;
        this.snack.open('Próxima cita programada.', 'Cerrar', { duration: 2000 });
        this.saveProgress();
      },
      error: err => {
        this.bookingAppointment = false;
        this.snack.open(err?.error?.message || 'No se ha podido programar la cita.', 'Cerrar', { duration: 3500 });
        this.loadConsultationSlots();
      }
    });
  }

  createQuickDiet(): void {
    const clientId = this.data?.appointment.clientId;
    if (!clientId || !this.quickDietName.trim() || this.creatingDiet) return;
    this.creatingDiet = true;
    this.dietService.createDiet({
      name: this.quickDietName.trim(),
      targetKcal: this.quickDietKcal ?? undefined,
      targetProtein: this.quickDietProtein ?? undefined,
      targetCarbs: this.quickDietCarbs ?? undefined,
      targetFat: this.quickDietFat ?? undefined,
      clientId,
      days: []
    }).subscribe({
      next: diet => {
        this.creatingDiet = false;
        this.activeDiet = diet;
        this.snack.open('Dieta creada y asignada al paciente.', 'Cerrar', { duration: 2200 });
        if (diet?.id) this.router.navigate(['/diets', diet.id]);
      },
      error: err => {
        this.creatingDiet = false;
        this.snack.open(err?.error?.message || 'No se ha podido crear la dieta.', 'Cerrar', { duration: 3500 });
      }
    });
  }

  completeConsultation(): void {
    if (!this.data || !this.consultation || this.completing || this.consultation.status === 'completed') return;
    if (!confirm('¿Cerrar esta consulta? Se registrará como completada y no se podrá continuar editándola como consulta en curso.')) return;

    // Persistir explícitamente el estado clínico antes del cierre. Los guardados disparados
    // por blur y por la navegación del stepper son asíncronos; cerrar en paralelo podía
    // convertir una consulta en completada antes de que actionsSummary llegara al servidor,
    // perdiendo las tareas acordadas.
    this.completing = true;
    this.portalService.updateGuidedConsultationProgress(this.data.appointment.id, {
      currentStep: this.currentStep,
      completedSteps: this.consultation.completedSteps || [],
      progress: this.buildClinicalProgress()
    }).subscribe({
      next: savedConsultation => {
        this.consultation = savedConsultation;
        this.portalService.completeGuidedConsultation(this.data!.appointment.id).subscribe({
          next: consultation => {
            this.completing = false;
            this.consultation = consultation;
            this.clinicalSaveState = 'saved';
            this.snack.open('Consulta cerrada correctamente.', 'Cerrar', { duration: 2000 });
          },
          error: err => {
            this.completing = false;
            this.snack.open(err?.error?.message || 'No se ha podido cerrar la consulta.', 'Cerrar', { duration: 3500 });
          }
        });
      },
      error: err => {
        this.completing = false;
        this.clinicalSaveState = 'error';
        this.snack.open(err?.error?.message || 'No se han podido guardar los acuerdos de la consulta; no se ha cerrado.', 'Cerrar', { duration: 4500 });
      }
    });
  }

  openOnlineConsultation(): void {
    if (!this.data?.appointment.id || this.data.appointment.modality !== 'online') return;
    this.router.navigate(['/video-consultation', this.data.appointment.id], { queryParams: { returnUrl: this.router.url } });
  }

  openPatient(): void {
    this.router.navigate(['/clients', this.data?.appointment.clientId]);
  }

  openPatientMessages(): void {
    const clientId = this.data?.appointment.clientId;
    if (clientId) this.router.navigate(['/clients', clientId, 'messages']);
  }

  openPatientDiet(): void {
    this.router.navigate(['/diets'], { queryParams: { clientId: this.data?.appointment.clientId } });
  }

  backToAgenda(): void {
    this.router.navigate(['/appointments']);
  }
}
