import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, EventEmitter, Input, OnInit, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom, timeout } from 'rxjs';
import { PlannerWizardDraft, PlannerWizardPhaseDraft, PlannerWizardStateService } from './planner-wizard-state.service';
import { GeneratedProgram, GenerationRequest, TrainingMesocycle } from './training-periodization.models';

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
          <p class="planner-intro">Selecciona los días y el material que realmente tiene disponible el cliente.</p>
          <div class="planner-days" role="group" aria-label="Días disponibles">
            <label *ngFor="let day of dayOptions" [class.selected]="draft.trainingDays.includes(day.value)">
              <input type="checkbox" [checked]="draft.trainingDays.includes(day.value)" (change)="toggleDay(day.value, $any($event.target).checked)" [disabled]="!!draft.serverPlanId">
              <span>{{ day.label }}</span>
            </label>
          </div>
          <p class="planner-validation" *ngIf="draft.trainingDays.length < 2">Selecciona al menos dos días para continuar.</p>
          <div class="planner-inline-fields">
            <label class="planner-field">Duración máxima por sesión
              <select [(ngModel)]="draft.sessionMinutes" (ngModelChange)="persist()" [disabled]="!!draft.serverPlanId"><option [ngValue]="45">45 minutos</option><option [ngValue]="60">60 minutos</option><option [ngValue]="75">75 minutos</option><option [ngValue]="90">90 minutos</option><option [ngValue]="120">120 minutos</option></select>
            </label>
            <label class="planner-field">Distribución
              <select [(ngModel)]="draft.split" (ngModelChange)="persist()" [disabled]="!!draft.serverPlanId"><option [ngValue]="0">Automática</option><option [ngValue]="1">Cuerpo completo</option><option [ngValue]="2">Torso / pierna</option><option [ngValue]="3">Empuje / tirón / pierna</option></select>
            </label>
          </div>
          <label class="planner-field">Material disponible (opcional)<input [(ngModel)]="draft.availableEquipmentCsv" (ngModelChange)="persist()" [disabled]="!!draft.serverPlanId" placeholder="barbell, dumbbell, machine, cable, kettlebell, pull-up-bar"><small>Separado por comas. Déjalo vacío si dispone de todo el material.</small></label>
        </section>

        <section *ngIf="draft.currentStep === 2" class="planner-step">
          <p class="planner-kicker">ETAPA 3 DE 5</p><h3>Define el macrociclo</h3>
          <p class="planner-intro">El servidor preparará una propuesta de fases editable. No se ejecuta el generador de rutinas.</p>
          <div class="planner-inline-fields">
            <label class="planner-field">Nombre de la planificación<input [(ngModel)]="draft.planName" (ngModelChange)="persist()" [disabled]="!!draft.serverPlanId" maxlength="160" placeholder="Ej. Fuerza e hipertrofia · temporada 1"></label>
            <label class="planner-field">Fecha de inicio<input type="date" [(ngModel)]="draft.startDate" (ngModelChange)="invalidateMacrocycle()" [disabled]="!!draft.serverPlanId"></label>
          </div>
          <label class="planner-field planner-weeks-field">Duración<div class="planner-range"><input type="range" min="4" max="52" step="1" [(ngModel)]="draft.durationWeeks" (ngModelChange)="invalidateMacrocycle()" [disabled]="!!draft.serverPlanId"><strong>{{ draft.durationWeeks }} semanas</strong></div></label>
          <label class="planner-check"><input type="checkbox" [(ngModel)]="draft.includeDeload" (ngModelChange)="invalidateMacrocycle()" [disabled]="!!draft.serverPlanId"> Incluir semanas de descarga</label>
          <label class="planner-field" *ngIf="draft.includeDeload">Intervalo entre descargas<select [(ngModel)]="draft.deloadEveryWeeks" (ngModelChange)="invalidateMacrocycle()" [disabled]="!!draft.serverPlanId"><option [ngValue]="4">4 semanas de trabajo</option><option [ngValue]="5">5 semanas de trabajo</option><option [ngValue]="6">6 semanas de trabajo</option><option [ngValue]="7">7 semanas de trabajo</option><option [ngValue]="8">8 semanas de trabajo</option></select></label>
          <button type="button" class="secondary" (click)="buildMacrocycleProposal()" [disabled]="buildingProposal || !macrocycleInputValid">{{ buildingProposal ? 'Preparando propuesta…' : (draft.phases.length ? 'Actualizar propuesta de fases' : 'Proponer fases del macrociclo') }}</button>
          <p *ngIf="proposalError" class="planner-validation" role="alert">{{ proposalError }}</p>
          <p *ngIf="draft.phases.length" class="planner-note">Propuesta preparada: {{ draft.phases.length }} fases. Revísalas aquí antes de continuar.</p>
          <div *ngIf="draft.phases.length" class="planner-phase-list">
            <h4>Fases propuestas · revisa antes de continuar</h4>
            <article *ngFor="let phase of draft.phases; let i = index" class="planner-phase-editor">
              <header><strong>Fase {{ i + 1 }}</strong><button type="button" class="secondary" (click)="removePhase(i)" [disabled]="draft.phases.length <= 1 || saving">Eliminar</button></header>
              <div class="planner-inline-fields">
                <label class="planner-field">Nombre<input [(ngModel)]="phase.name" (ngModelChange)="phaseChanged()" [disabled]="!!draft.serverPlanId" maxlength="160"></label>
                <label class="planner-field">Tipo<select [(ngModel)]="phase.phaseType" (ngModelChange)="phaseChanged()" [disabled]="!!draft.serverPlanId"><option value="accumulation">Acumulación</option><option value="intensification">Intensificación</option><option value="deload">Descarga</option><option value="realization">Realización</option><option value="general">General</option></select></label>
                <label class="planner-field">Inicio<input type="date" [(ngModel)]="phase.startDate" (ngModelChange)="phaseChanged()" [disabled]="!!draft.serverPlanId"></label>
                <label class="planner-field">Fin<input type="date" [(ngModel)]="phase.endDate" (ngModelChange)="phaseChanged()" [disabled]="!!draft.serverPlanId"></label>
              </div>
              <label class="planner-field">Objetivo<textarea [(ngModel)]="phase.objective" (ngModelChange)="phaseChanged()" [disabled]="!!draft.serverPlanId" rows="2" maxlength="500"></textarea></label>
            </article>
            <p *ngIf="phaseValidationMessage" class="planner-validation" role="alert">{{ phaseValidationMessage }}</p>
          </div>
        </section>



        <section *ngIf="draft.currentStep === 3" class="planner-step">
          <p class="planner-kicker">ETAPA 4 DE 5</p><h3>Previsualiza y genera cada mesociclo</h3>
          <p class="planner-intro">Cada fase se procesa por separado. Primero calculamos la vista previa con el historial disponible; solo después de revisar sus avisos podrás guardar el bloque.</p>
          <div *ngIf="draft.serverPlanId" class="planner-note">Plan borrador {{ draft.serverPlanId }} · {{ serverMesocycles.length }} mesociclos guardados. Cada bloque generado permanece como borrador y requiere revisión profesional.</div>
          <div *ngIf="draft.phases.length" class="planner-phase-progress"><span *ngFor="let phase of draft.phases; let i = index" [class.complete]="isPhaseGenerated(phase)" [class.current]="i === currentPhaseIndex">{{ i + 1 }}</span></div>
          <article *ngIf="currentPhase as phase" class="planner-phase-editor">
            <header><strong>{{ phase.name }}</strong><span>{{ phase.startDate | date:'dd/MM/yyyy' }} – {{ phase.endDate | date:'dd/MM/yyyy' }}</span></header>
            <p>{{ phase.objective }}</p>
            <p class="planner-note" *ngIf="isPhaseGenerated(phase)">Este mesociclo ya está guardado. No se volverá a generar.</p>
            <div class="planner-generation-actions">
              <button type="button" class="secondary" (click)="previewCurrentPhase()" [disabled]="previewLoading || generatingPhase || isPhaseGenerated(phase)">{{ previewLoading ? 'Calculando vista previa…' : 'Generar vista previa' }}</button>
              <button type="button" (click)="generateCurrentPhase()" [disabled]="!canGenerateCurrentPhase || generatingPhase">{{ generatingPhase ? 'Guardando bloque…' : 'Guardar este mesociclo' }}</button>
            </div>
            <p *ngIf="currentPreview && previewPhaseKey === phaseKey(phase)" class="planner-note">Vista previa: {{ currentPreview.sessions.length }} sesiones en {{ currentPreview.durationWeeks }} semanas · motor {{ currentPreview.engineVersion }}</p>
            <div *ngIf="currentPreview && previewPhaseKey === phaseKey(phase)" class="planner-preview-warnings">
              <strong>Revisión de calidad</strong>
              <p *ngIf="!(currentPreview.qualityReport?.warnings?.length)">La vista previa no ha devuelto advertencias de calidad.</p>
              <ul *ngIf="currentPreview.qualityReport?.warnings?.length"><li *ngFor="let warning of currentPreview.qualityReport?.warnings">{{ warning }}</li></ul>
              <label *ngIf="currentPreview.qualityReport?.warnings?.length" class="planner-check"><input type="checkbox" [(ngModel)]="acknowledgeWarnings"> He revisado los avisos y quiero guardar este borrador.</label>
            </div>
          </article>
          <p *ngIf="mesocycleError" class="planner-validation" role="alert">{{ mesocycleError }}</p>
          <p *ngIf="allPhasesGenerated" class="planner-note">Todas las fases tienen su mesociclo guardado. Puedes pasar a la revisión final.</p>
          <p *ngIf="!allPhasesGenerated" class="planner-note">Puedes dejar fases pendientes y continuar; podrás generarlas más tarde desde la planificación.</p>
        </section>

        <section *ngIf="draft.currentStep === 4" class="planner-step">
          <p class="planner-kicker">ETAPA 5 DE 5</p><h3>Revisión final</h3>
          <p class="planner-intro">Comprueba qué se ha guardado. No se envía ninguna notificación ni se activa el plan.</p>
          <dl class="planner-review">
            <div><dt>Objetivo</dt><dd>{{ goalLabel }}</dd></div><div><dt>Experiencia</dt><dd>{{ experienceLabel }}</dd></div>
            <div><dt>Días</dt><dd>{{ selectedDaysLabel }}</dd></div><div><dt>Sesión máxima</dt><dd>{{ draft.sessionMinutes }} minutos</dd></div>
            <div><dt>Distribución</dt><dd>{{ splitLabel }}</dd></div><div><dt>Duración</dt><dd>{{ draft.durationWeeks }} semanas</dd></div>
            <div><dt>Fases</dt><dd>{{ draft.phases.length }}</dd></div><div><dt>Mesociclos guardados</dt><dd>{{ generatedCount }} / {{ draft.phases.length }}</dd></div>
          </dl>
          <ul class="planner-final-phases"><li *ngFor="let phase of draft.phases"><strong>{{ phase.name }}</strong><span>{{ isPhaseGenerated(phase) ? 'Mesociclo guardado · borrador' : 'Pendiente de generar' }}</span></li></ul>
          <div class="planner-draft-warning"><strong>La planificación sigue en borrador.</strong><span>Los bloques generados requieren revisión profesional. El portal del cliente puede mostrar borradores para que solicite revisión, pero no se envía ninguna notificación ni se activa el plan.</span></div>
          <p *ngIf="saveError" class="planner-validation" role="alert">{{ saveError }}</p>
          <p *ngIf="draft.creationUncertain" class="planner-validation" role="alert">No se ha podido confirmar si el servidor creó el plan. Comprueba la lista de planes antes de continuar; no reintentaremos para evitar duplicados.</p>
        </section>
      </div>

      <footer class="planner-wizard-footer">
        <span class="planner-save-status" role="status">{{ savedLabel }}</span>
        <div>
          <button type="button" class="secondary" (click)="previous()" [disabled]="draft.currentStep === 0 || saving || generatingPhase">Anterior</button>
          <button type="button" (click)="next()" *ngIf="draft.currentStep < 2" [disabled]="!canContinue || saving">Continuar →</button>
          <button type="button" (click)="prepareMesocycleStep()" *ngIf="draft.currentStep === 2" [disabled]="!canContinue || saving || buildingProposal || draft.creationUncertain">{{ saving ? 'Guardando macrociclo…' : 'Guardar macrociclo y continuar' }}</button>
          <button type="button" (click)="goToFinalReview()" *ngIf="draft.currentStep === 3" [disabled]="saving || generatingPhase || previewLoading">Continuar a la revisión →</button>
          <button type="button" (click)="finish()" *ngIf="draft.currentStep === 4" [disabled]="saving || draft.creationUncertain">Finalizar y abrir planificación</button>
        </div>
      </footer>
    </section>
  `
})
export class TrainingPlannerWizardComponent implements OnInit {
  @Input({ required: true }) clientId = 0;
  @Output() close = new EventEmitter<void>();
  @Output() completed = new EventEmitter<number>();

  readonly steps = ['Objetivo', 'Disponibilidad', 'Macrociclo', 'Mesociclos', 'Revisión'];
  readonly dayOptions = [
    { value: 1, label: 'Lun' }, { value: 2, label: 'Mar' }, { value: 3, label: 'Mié' },
    { value: 4, label: 'Jue' }, { value: 5, label: 'Vie' }, { value: 6, label: 'Sáb' }, { value: 0, label: 'Dom' }
  ];
  draft!: PlannerWizardDraft;
  savedLabel = 'Borrador guardado en esta sesión';
  saving = false;
  buildingProposal = false;
  proposalError = '';
  saveError = '';
  mesocycleError = '';
  previewLoading = false;
  generatingPhase = false;
  currentPhaseIndex = 0;
  currentPreview: GeneratedProgram | null = null;
  previewPhaseKey = '';
  acknowledgeWarnings = false;
  serverMesocycles: TrainingMesocycle[] = [];

  constructor(private readonly state: PlannerWizardStateService, private readonly http: HttpClient) {}

  ngOnInit(): void {
    this.draft = this.state.load(this.clientId);
    if (this.draft.creationInProgress) { this.draft.creationInProgress = false; this.draft.creationUncertain = true; }
    this.persist();
    if (this.draft.serverPlanId) void this.refreshServerMesocycles();
  }

  get validSplitDays(): boolean {
    const count = this.draft.trainingDays.length;
    return count >= 2 && count <= 6 && (this.draft.split === 0 || this.draft.split === 1 ? count >= 2 && count <= 4 :
      this.draft.split === 2 ? count === 2 || count === 4 : this.draft.split === 3 ? count === 3 || count === 5 || count === 6 : false);
  }
  get splitValidationMessage(): string {
    if (this.draft.trainingDays.length < 2 || this.draft.trainingDays.length > 6) return 'Selecciona entre dos y seis días.';
    return this.draft.split === 2 ? 'Torso / pierna admite 2 o 4 días.' : this.draft.split === 3 ? 'Empuje / tirón / pierna admite 3, 5 o 6 días.' : 'La distribución automática y cuerpo completo admiten de 2 a 4 días.';
  }
  get macrocycleEndDate(): string { return this.addDays(this.draft.startDate, this.draft.durationWeeks * 7 - 1); }
  get macrocycleInputValid(): boolean { return !!this.draft.planName.trim() && !!this.draft.startDate && this.draft.durationWeeks >= 4 && this.draft.durationWeeks <= 52; }
  get phaseValidationMessage(): string {
    if (!this.draft.phases.length) return 'Genera una propuesta de fases antes de continuar.';
    const phases = [...this.draft.phases].sort((a, b) => a.startDate.localeCompare(b.startDate));
    for (let i = 0; i < phases.length; i++) {
      const phase = phases[i];
      if (!phase.name.trim() || !phase.startDate || !phase.endDate || phase.endDate < phase.startDate) return 'Revisa el nombre y las fechas de cada fase.';
      if (phase.startDate < this.draft.startDate || phase.endDate > this.macrocycleEndDate) return 'Todas las fases deben quedar dentro de las fechas del macrociclo.';
      const phaseDays = this.inclusiveDays(phase.startDate, phase.endDate);
      if (phaseDays % 7 !== 0) return 'Cada fase debe durar un número exacto de semanas.';
      if (phase.phaseType === 'deload' && phaseDays !== 7) return 'Cada fase de descarga debe durar exactamente siete días.';
      if (i > 0 && phase.startDate <= phases[i - 1].endDate) return 'Las fases no pueden solaparse.';
      if (i > 0 && phase.startDate !== this.addDays(phases[i - 1].endDate, 1)) return 'Las fases deben ser consecutivas, sin dejar días ni semanas sin planificar.';
    }
    if (phases[0].startDate !== this.draft.startDate || phases[phases.length - 1].endDate !== this.macrocycleEndDate) return 'Las fases deben cubrir todo el macrociclo, sin dejar semanas fuera.';
    return '';
  }
  get canContinue(): boolean {
    if (this.draft.currentStep === 1) return this.validSplitDays && this.draft.sessionMinutes >= 35 && this.draft.sessionMinutes <= 180;
    if (this.draft.currentStep === 2) return this.macrocycleInputValid && !this.phaseValidationMessage;
    return true;
  }
  get canSave(): boolean { return this.validSplitDays && this.macrocycleInputValid && !!this.draft.phases.length && !this.phaseValidationMessage; }

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

  invalidateMacrocycle(): void { this.draft.phases = []; this.draft.serverMacrocycleSaved = false; this.persist(); }
  phaseChanged(): void { this.draft.serverMacrocycleSaved = false; this.persist(); }

  async buildMacrocycleProposal(): Promise<void> {
    if (!this.macrocycleInputValid || this.buildingProposal) return;
    this.buildingProposal = true; this.proposalError = '';
    try {
      const result = await firstValueFrom(this.http.post<any>('/api/training/macrocycle-draft', {
        startDate: this.draft.startDate, totalWeeks: this.draft.durationWeeks, goal: this.draft.goal,
        experience: this.draft.experience, includeDeloadPhases: this.draft.includeDeload,
        deloadEveryTrainingWeeks: this.draft.deloadEveryWeeks
      }).pipe(timeout({ first: 15000 })));
      this.draft.phases = (result.phases ?? []).map((phase: PlannerWizardPhaseDraft) => ({ ...phase }));
      this.draft.macrocycleName = result.name || 'Macrociclo principal';
      this.draft.serverMacrocycleSaved = false;
      this.persist();
    } catch (error: any) {
      this.proposalError = error?.error?.message || 'No se pudo preparar el macrociclo. Conservamos los datos para reintentarlo.';
    } finally { this.buildingProposal = false; }
  }

  removePhase(index: number): void {
    if (this.draft.phases.length <= 1) return;
    this.draft.phases = this.draft.phases.filter((_, i) => i !== index).map((phase, i) => ({ ...phase, sequenceNumber: i + 1 }));
    this.phaseChanged();
  }

  get currentPhase(): PlannerWizardPhaseDraft | null { return this.draft.phases[this.currentPhaseIndex] ?? null; }
  phaseKey(phase: PlannerWizardPhaseDraft): string { return phase.startDate + '|' + phase.endDate; }
  isPhaseGenerated(phase: PlannerWizardPhaseDraft): boolean {
    const key = this.phaseKey(phase);
    return this.draft.generatedPhaseKeys.includes(key) || this.serverMesocycles.some(meso =>
      String(meso.startDate).slice(0, 10) === phase.startDate && String(meso.endDate ?? '').slice(0, 10) === phase.endDate);
  }
  get generatedCount(): number { return this.draft.phases.filter(phase => this.isPhaseGenerated(phase)).length; }
  get allPhasesGenerated(): boolean { return this.generatedCount === this.draft.phases.length; }
  get canGenerateCurrentPhase(): boolean {
    const phase = this.currentPhase;
    if (!phase || !this.currentPreview || this.previewPhaseKey !== this.phaseKey(phase) || this.isPhaseGenerated(phase)) return false;
    return !(this.currentPreview.qualityReport?.warnings?.length) || this.acknowledgeWarnings;
  }

  async refreshServerMesocycles(): Promise<void> {
    if (!this.draft.serverPlanId) return;
    this.serverMesocycles = await firstValueFrom(this.http.get<TrainingMesocycle[]>(
      `/api/training/plans/${this.draft.serverPlanId}/mesocycles`).pipe(timeout({ first: 15000 })));
    const known = new Set(this.draft.generatedPhaseKeys);
    for (const phase of this.draft.phases) {
      if (this.serverMesocycles.some(meso =>
        String(meso.startDate).slice(0, 10) === phase.startDate && String(meso.endDate ?? '').slice(0, 10) === phase.endDate))
        known.add(this.phaseKey(phase));
    }
    this.draft.generatedPhaseKeys = [...known];
    this.persist();
  }

  private selectNextPendingPhase(): void {
    const next = this.draft.phases.findIndex(phase => !this.isPhaseGenerated(phase));
    this.currentPhaseIndex = next >= 0 ? next : Math.max(0, this.draft.phases.length - 1);
    this.currentPreview = null;
    this.previewPhaseKey = '';
    this.acknowledgeWarnings = false;
  }

  async prepareMesocycleStep(): Promise<void> {
    if (!this.canSave || this.saving || this.draft.creationUncertain) return;
    this.saving = true; this.saveError = '';
    try {
      await this.saveBasePlan();
      await this.refreshServerMesocycles();
      this.selectNextPendingPhase();
      this.draft.currentStep = 3;
      this.persist();
    } catch (error: any) {
      this.saveError = error?.error?.message || error?.error?.errors?.join?.(' ') || error?.message || 'No se pudo preparar el plan. Puedes reintentar sin crear otro plan.';
    } finally { this.saving = false; }
  }

  private async saveBasePlan(): Promise<void> {
    if (!this.draft.serverPlanId) {
      this.draft.creationInProgress = true; this.persist();
      try {
        const created = await firstValueFrom(this.http.post<any>(`/api/training/clients/${this.clientId}/plans`, {
          name: this.draft.planName.trim(), periodizationModel: 'custom', startDate: this.draft.startDate,
          endDate: this.macrocycleEndDate, notes: 'Plan creado desde el asistente guiado. Pendiente de revisión y asignación profesional.',
          notifyClient: false
        }).pipe(timeout({ first: 20000 })));
        this.draft.serverPlanId = created.id; this.draft.creationInProgress = false; this.draft.creationUncertain = false; this.persist();
      } catch (error: any) {
        this.draft.creationInProgress = false;
        if (error?.status === 0 || error?.status >= 500 || !error?.status) this.draft.creationUncertain = true;
        this.persist(); throw error;
      }
    }
    const phases = [...this.draft.phases].sort((a, b) => a.startDate.localeCompare(b.startDate)).map((phase, i) => ({
      name: phase.name.trim(), sequenceNumber: i + 1, startDate: phase.startDate, endDate: phase.endDate,
      objective: phase.objective?.trim() || null, phaseType: phase.phaseType
    }));
    if (!this.draft.serverMacrocycleSaved) {
      await firstValueFrom(this.http.put(`/api/training/plans/${this.draft.serverPlanId}/macrocycle`, {
        name: this.draft.macrocycleName || this.draft.planName.trim(), periodizationModel: 'custom',
        startDate: this.draft.startDate, endDate: this.macrocycleEndDate,
        notes: 'Macrociclo generado y revisado en el asistente guiado.', phases
      }).pipe(timeout({ first: 20000 })));
      this.draft.serverMacrocycleSaved = true; this.persist();
    }
    if (!this.draft.serverSplitSaved) {
      await firstValueFrom(this.http.post(`/api/training/plans/${this.draft.serverPlanId}/reorganize-split`, {
        splitType: this.draft.split, trainingDays: this.draft.trainingDays, moves: []
      }).pipe(timeout({ first: 20000 })));
      this.draft.serverSplitSaved = true; this.persist();
    }
  }

  private generationRequestFor(phase: PlannerWizardPhaseDraft): GenerationRequest {
    return {
      goal: this.draft.goal,
      experience: this.draft.experience,
      startDate: phase.startDate,
      durationWeeks: this.inclusiveDays(phase.startDate, phase.endDate) / 7,
      trainingDays: [...this.draft.trainingDays],
      split: this.draft.split,
      maximumSessionDurationMinutes: this.draft.sessionMinutes,
      availableEquipment: [...new Set(this.draft.availableEquipmentCsv.split(',').map(value => value.trim()).filter(Boolean))],
      excludedExerciseIds: [],
      muscleVolumeTargets: [],
      isDeload: phase.phaseType === 'deload'
    };
  }

  async previewCurrentPhase(): Promise<void> {
    const phase = this.currentPhase;
    if (!phase || !this.draft.serverPlanId || this.previewLoading || this.isPhaseGenerated(phase)) return;
    this.previewLoading = true; this.mesocycleError = ''; this.currentPreview = null; this.previewPhaseKey = '';
    this.acknowledgeWarnings = false;
    try {
      this.currentPreview = await firstValueFrom(this.http.post<GeneratedProgram>(
        `/api/training/generate-preview?planId=${this.draft.serverPlanId}`,
        this.generationRequestFor(phase)
      ).pipe(timeout({ first: 60000 })));
      this.previewPhaseKey = this.phaseKey(phase);
    } catch (error: any) {
      this.mesocycleError = error?.error?.message || error?.error?.errors?.join?.(' ') || 'No se pudo calcular la vista previa de esta fase.';
    } finally { this.previewLoading = false; }
  }

  async generateCurrentPhase(): Promise<void> {
    const phase = this.currentPhase;
    if (!phase || !this.canGenerateCurrentPhase || !this.draft.serverPlanId || this.generatingPhase) return;
    this.generatingPhase = true; this.mesocycleError = '';
    const sequenceNumber = Math.max(0, ...this.serverMesocycles.map(meso => meso.sequenceNumber)) + 1;
    try {
      await firstValueFrom(this.http.post(
        `/api/training/plans/${this.draft.serverPlanId}/generate-mesocycle`,
        {
          name: phase.name.trim(), sequenceNumber, generation: this.generationRequestFor(phase),
          notes: 'Generado desde el asistente guiado. Pendiente de revisión profesional antes de su activación.'
        }
      ).pipe(timeout({ first: 90000 })));
      await this.refreshServerMesocycles();
      this.draft.generatedPhaseKeys = [...new Set([...this.draft.generatedPhaseKeys, this.phaseKey(phase)])];
      this.persist();
      this.selectNextPendingPhase();
      this.savedLabel = 'Mesociclo guardado como borrador';
    } catch (error: any) {
      // A timeout can happen after the transaction committed. Reconcile before offering a retry.
      try {
        await this.refreshServerMesocycles();
        if (this.isPhaseGenerated(phase)) {
          this.selectNextPendingPhase();
          this.savedLabel = 'Mesociclo guardado; respuesta reconciliada';
          return;
        }
      } catch { /* Keep the original actionable error. */ }
      this.mesocycleError = error?.error?.message || error?.error?.errors?.join?.(' ') || error?.message || 'No se pudo guardar el mesociclo. Revisa el estado antes de reintentar.';
    } finally { this.generatingPhase = false; }
  }

  goToFinalReview(): void {
    if (this.generatingPhase || this.previewLoading) return;
    this.draft.currentStep = 4; this.persist();
  }

  finish(): void {
    if (!this.draft.serverPlanId || this.draft.creationUncertain) return;
    const completedPlanId = this.draft.serverPlanId;
    this.state.clear(this.clientId);
    this.completed.emit(completedPlanId);
  }


  private inclusiveDays(start: string, end: string): number {
    return Math.floor((Date.parse(end + 'T00:00:00Z') - Date.parse(start + 'T00:00:00Z')) / 86400000) + 1;
  }
  private addDays(value: string, days: number): string {
    const date = new Date(value + 'T12:00:00Z'); date.setUTCDate(date.getUTCDate() + days); return date.toISOString().slice(0, 10);
  }

  toggleDay(day: number, checked: boolean): void {
    this.draft.trainingDays = checked
      ? [...this.draft.trainingDays, day].sort((a, b) => a - b)
      : this.draft.trainingDays.filter(value => value !== day);
    this.persist();
  }

  previous(): void {
    if (this.draft.currentStep > 0 && !this.saving && !this.generatingPhase && !this.previewLoading) {
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

}
