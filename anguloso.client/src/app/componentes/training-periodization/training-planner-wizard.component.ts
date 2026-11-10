import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PlannerWizardDraft, PlannerWizardStateService } from './planner-wizard-state.service';

@Component({
  selector: 'app-training-planner-wizard',
  standalone: true,
  imports: [CommonModule, FormsModule],
  template: `
    <section class="planner-wizard panel" aria-labelledby="plannerWizardTitle">
      <header class="planner-wizard-header">
        <div>
          <p class="eyebrow">PLANIFICADOR GUIADO · BORRADOR</p>
          <h2 id="plannerWizardTitle">Diseña tu planificación</h2>
          <p>Configura primero el objetivo y la disponibilidad. Revisaremos la propuesta antes de crear o modificar un plan real.</p>
        </div>
        <button type="button" class="secondary planner-close" (click)="close.emit()" aria-label="Cerrar planificador">×</button>
      </header>

      <div class="planner-progress" aria-label="Progreso del asistente">
        <div class="planner-progress-track"><span [style.width.%]="((draft.currentStep + 1) / steps.length) * 100"></span></div>
        <div class="planner-step-labels">
          <span *ngFor="let step of steps; let i = index" [class.active]="i === draft.currentStep" [class.complete]="i < draft.currentStep">
            <b>{{ i + 1 }}</b><small>{{ step }}</small>
          </span>
        </div>
      </div>

      <div class="planner-wizard-body">
        <section *ngIf="draft.currentStep === 0" class="planner-step">
          <p class="planner-kicker">ETAPA 1 DE 5</p>
          <h3>¿Qué quieres conseguir?</h3>
          <p class="planner-intro">Elige el objetivo principal y el nivel de experiencia que debe orientar la propuesta.</p>
          <div class="planner-choice-grid">
            <label class="planner-choice" [class.selected]="draft.goal === 0"><input type="radio" name="plannerGoal" [value]="0" [(ngModel)]="draft.goal" (ngModelChange)="persist()"><span><strong>Forma física general</strong><small>Mejorar condición física y mantener una progresión equilibrada.</small></span></label>
            <label class="planner-choice" [class.selected]="draft.goal === 1"><input type="radio" name="plannerGoal" [value]="1" [(ngModel)]="draft.goal" (ngModelChange)="persist()"><span><strong>Hipertrofia</strong><small>Priorizar el desarrollo de masa muscular.</small></span></label>
            <label class="planner-choice" [class.selected]="draft.goal === 2"><input type="radio" name="plannerGoal" [value]="2" [(ngModel)]="draft.goal" (ngModelChange)="persist()"><span><strong>Fuerza</strong><small>Priorizar el rendimiento en ejercicios de fuerza.</small></span></label>
          </div>
          <label class="planner-field">Experiencia
            <select [(ngModel)]="draft.experience" (ngModelChange)="persist()">
              <option [ngValue]="0">Principiante</option><option [ngValue]="1">Intermedio</option><option [ngValue]="2">Avanzado</option>
            </select>
          </label>
        </section>

        <section *ngIf="draft.currentStep === 1" class="planner-step">
          <p class="planner-kicker">ETAPA 2 DE 5</p>
          <h3>¿Cuándo puedes entrenar?</h3>
          <p class="planner-intro">Selecciona los días disponibles. Podremos ajustar la distribución en la siguiente iteración del asistente.</p>
          <div class="planner-days" role="group" aria-label="Días disponibles">
            <label *ngFor="let day of dayOptions" [class.selected]="draft.trainingDays.includes(day.value)">
              <input type="checkbox" [checked]="draft.trainingDays.includes(day.value)" (change)="toggleDay(day.value, $any($event.target).checked)">
              <span>{{ day.label }}</span>
            </label>
          </div>
          <p class="planner-validation" *ngIf="draft.trainingDays.length < 2">Selecciona al menos dos días para continuar.</p>
          <div class="planner-inline-fields">
            <label class="planner-field">Duración máxima por sesión
              <select [(ngModel)]="draft.sessionMinutes" (ngModelChange)="persist()"><option [ngValue]="45">45 minutos</option><option [ngValue]="60">60 minutos</option><option [ngValue]="75">75 minutos</option><option [ngValue]="90">90 minutos</option><option [ngValue]="120">120 minutos</option></select>
            </label>
            <label class="planner-field">Distribución
              <select [(ngModel)]="draft.split" (ngModelChange)="persist()"><option [ngValue]="0">Automática</option><option [ngValue]="1">Cuerpo completo</option><option [ngValue]="2">Torso / pierna</option><option [ngValue]="3">Empuje / tirón / pierna</option></select>
            </label>
          </div>
        </section>

        <section *ngIf="draft.currentStep === 2" class="planner-step">
          <p class="planner-kicker">ETAPA 3 DE 5</p>
          <h3>Define el macrociclo</h3>
          <p class="planner-intro">Establece el marco temporal. La propuesta de fases se podrá revisar y editar antes de guardarla.</p>
          <div class="planner-inline-fields">
            <label class="planner-field">Nombre de la planificación<input [(ngModel)]="draft.planName" (ngModelChange)="persist()" maxlength="160" placeholder="Ej. Fuerza e hipertrofia · temporada 1"></label>
            <label class="planner-field">Fecha de inicio<input type="date" [(ngModel)]="draft.startDate" (ngModelChange)="persist()"></label>
          </div>
          <label class="planner-field planner-weeks-field">Duración
            <div class="planner-range"><input type="range" min="4" max="52" step="1" [(ngModel)]="draft.durationWeeks" (ngModelChange)="persist()"><strong>{{ draft.durationWeeks }} semanas</strong></div>
          </label>
          <label class="planner-check"><input type="checkbox" [(ngModel)]="draft.includeDeload" (ngModelChange)="persist()"> Incluir semanas de descarga en la propuesta</label>
        </section>

        <section *ngIf="draft.currentStep === 3" class="planner-step">
          <p class="planner-kicker">ETAPA 4 DE 5</p>
          <h3>Organiza los bloques de trabajo</h3>
          <p class="planner-intro">Esta es una vista orientativa. En la siguiente fase de desarrollo conectaremos aquí la propuesta real de mesociclos y su vista previa de calidad.</p>
          <div class="planner-block-preview">
            <article><span>01</span><div><strong>Bloque inicial</strong><small>Adaptación y establecimiento de cargas</small></div><b>{{ Math.min(4, draft.durationWeeks) }} sem.</b></article>
            <article><span>02</span><div><strong>Bloque de desarrollo</strong><small>Progresión principal según el objetivo</small></div><b>{{ Math.max(1, draft.durationWeeks - 4) }} sem.</b></article>
            <article *ngIf="draft.includeDeload"><span>03</span><div><strong>Descargas</strong><small>Se planificarán como semanas específicas</small></div><b>Por definir</b></article>
          </div>
          <p class="planner-note">No se genera ninguna rutina en esta etapa. Cada bloque se configurará y previsualizará antes de guardarse.</p>
        </section>

        <section *ngIf="draft.currentStep === 4" class="planner-step">
          <p class="planner-kicker">ETAPA 5 DE 5</p>
          <h3>Revisa la planificación</h3>
          <p class="planner-intro">Comprueba los datos antes de pasar a la integración con el servidor.</p>
          <dl class="planner-review">
            <div><dt>Objetivo</dt><dd>{{ goalLabel }}</dd></div>
            <div><dt>Experiencia</dt><dd>{{ experienceLabel }}</dd></div>
            <div><dt>Días</dt><dd>{{ selectedDaysLabel }}</dd></div>
            <div><dt>Sesión máxima</dt><dd>{{ draft.sessionMinutes }} minutos</dd></div>
            <div><dt>Distribución</dt><dd>{{ splitLabel }}</dd></div>
            <div><dt>Duración</dt><dd>{{ draft.durationWeeks }} semanas</dd></div>
            <div><dt>Inicio</dt><dd>{{ draft.startDate || 'Sin definir' }}</dd></div>
          </dl>
          <div class="planner-draft-warning"><strong>Solo se guarda un borrador local.</strong><span>Este PR no crea planes, no envía notificaciones y no ejecuta el generador. La conexión con el servidor llegará en una fase posterior.</span></div>
        </section>
      </div>

      <footer class="planner-wizard-footer">
        <span class="planner-save-status" role="status">{{ savedLabel }}</span>
        <div>
          <button type="button" class="secondary" (click)="previous()" [disabled]="draft.currentStep === 0">Anterior</button>
          <button type="button" (click)="next()" *ngIf="draft.currentStep < steps.length - 1" [disabled]="!canContinue">Continuar →</button>
          <button type="button" (click)="saveAndClose()" *ngIf="draft.currentStep === steps.length - 1">Guardar borrador y cerrar</button>
        </div>
      </footer>
    </section>
  `
})
export class TrainingPlannerWizardComponent implements OnInit {
  @Input({ required: true }) clientId = 0;
  @Output() close = new EventEmitter<void>();

  readonly steps = ['Objetivo', 'Disponibilidad', 'Macrociclo', 'Bloques', 'Revisión'];
  readonly dayOptions = [
    { value: 1, label: 'Lun' }, { value: 2, label: 'Mar' }, { value: 3, label: 'Mié' },
    { value: 4, label: 'Jue' }, { value: 5, label: 'Vie' }, { value: 6, label: 'Sáb' }, { value: 0, label: 'Dom' }
  ];
  readonly Math = Math;
  draft: PlannerWizardDraft = this.state.createDefault();
  savedLabel = 'Los cambios se guardan en esta sesión';

  constructor(private readonly state: PlannerWizardStateService) {}

  ngOnInit(): void {
    this.draft = this.state.load(this.clientId);
    this.persist();
  }

  get canContinue(): boolean {
    if (this.draft.currentStep === 1) return this.draft.trainingDays.length >= 2;
    if (this.draft.currentStep === 2) return !!this.draft.planName.trim() && !!this.draft.startDate && this.draft.durationWeeks >= 4;
    return true;
  }

  get goalLabel(): string {
    return ['Forma física general', 'Hipertrofia', 'Fuerza'][this.draft.goal] ?? 'Sin definir';
  }
  get experienceLabel(): string {
    return ['Principiante', 'Intermedio', 'Avanzado'][this.draft.experience] ?? 'Sin definir';
  }
  get splitLabel(): string {
    return ['Automática', 'Cuerpo completo', 'Torso / pierna', 'Empuje / tirón / pierna'][this.draft.split] ?? 'Automática';
  }
  get selectedDaysLabel(): string {
    return this.dayOptions.filter(day => this.draft.trainingDays.includes(day.value)).map(day => day.label).join(', ') || 'Sin definir';
  }

  toggleDay(day: number, checked: boolean): void {
    this.draft.trainingDays = checked
      ? [...this.draft.trainingDays, day].sort((a, b) => a - b)
      : this.draft.trainingDays.filter(value => value !== day);
    this.persist();
  }

  previous(): void {
    if (this.draft.currentStep > 0) {
      this.draft.currentStep--;
      this.persist();
    }
  }

  next(): void {
    if (this.canContinue && this.draft.currentStep < this.steps.length - 1) {
      this.draft.currentStep++;
      this.persist();
    }
  }

  persist(): void {
    this.state.save(this.clientId, this.draft);
    this.savedLabel = 'Borrador guardado en esta sesión';
  }

  saveAndClose(): void {
    this.persist();
    this.close.emit();
  }
}
