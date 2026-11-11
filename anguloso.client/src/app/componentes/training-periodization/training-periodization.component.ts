import { EquipmentFieldComponent } from './equipment-field.component';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, firstValueFrom, forkJoin, of, Subscription, timeout } from 'rxjs';
import { DateInputDirective } from '../../directives/date-input.directive';
import { TrainingReviewRequestsPanelComponent } from './training-review-requests-panel.component';
import { TrainingPlannerWizardComponent } from './training-planner-wizard.component';
import { PlannerWizardStateService } from './planner-wizard-state.service';
import { MacroChartMesocycle, MacroChartPhase, MacroChartWeek, MacrocycleChartComponent } from './macrocycle-chart.component';

import {
  TrainingProfile,
  TrainingPlan,
  GeneratedExercise,
  GeneratedSession,
  TrainingExerciseCatalogItem,
  GeneratedProgram,
  GenerationRequest,
  ManualVolumePreview,
  MuscleVolumePreferenceDraft,
  TrainingExerciseVolumeRecommendation,
  TrainingAdaptationProposal,
  TrainingSession,
  StructuredExerciseDraft,
  WeeklySessionDraft,
  SplitReorganizationMove,
  SplitReorganizationDraft,
  SplitReplacementSession,
  SplitReplacementPreview,
  TrainingLoadWeek,
  TrainingMesocycle,
  MacrocyclePhase,
  MacroPhaseLoadWeek,
  MacrocyclePlan,
  TrainingMicrocycle,
  TrainingCollaboration,
  TrainingInvitation,
  TrainingTemplate,
  TrainingSessionTemplate,
  RepDbExercise
} from './training-periodization.models';

@Component({
  selector: 'app-training-periodization',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, DateInputDirective, MacrocycleChartComponent, TrainingReviewRequestsPanelComponent, TrainingPlannerWizardComponent, EquipmentFieldComponent],
  templateUrl: './training-periodization.component.html',
})
export class TrainingPeriodizationComponent implements OnInit, OnDestroy {
  showPlannerWizard = false;
  clientId = 0;
  loading = true;
  loadFailed = false;
  saving = false;
  private loadVersion = 0;
  bulkGenerationProgress: Record<number, { completed: number; total: number; phaseName: string }> = {};
  error = '';
  notice = '';
  recordingSessionId: number | null = null;
  editingSessionId: number | null = null;
  sessionEditDraft: { date: string; title: string; type: string; duration: number | null; intensity: string; instructions: string; exercisePrescription: string; exercisePrescriptionData: Record<string, unknown> | null } | null = null;
  recordDrafts: Record<number, { duration: number | null; rpe: number | null; notes: string }> = {};
  adaptationProposals: TrainingAdaptationProposal[] = [];
  deepLinkRequestId: number | null = null;
  private deepLinkApplied = false;
  adaptationReviewNotes: Record<number, string> = {};
  adaptationTargetExercises: Record<number, string[]> = {};
  collaborations: TrainingCollaboration[] = [];
  invitations: TrainingInvitation[] = [];
  trainingTemplates: TrainingTemplate[] = [];
  sessionTemplates: TrainingSessionTemplate[] = [];
  standaloneSessionTemplateDraft = { name: '', type: 'strength', duration: 60 as number | null, intensity: '', instructions: '', exercisePrescription: '', exercisePrescriptionData: null as Record<string, unknown> | null };
  sessionTemplateNameDrafts: Record<number, string> = {};
  selectedSessionTemplateIds: Record<number, number | null> = {};
  templateNameDrafts: Record<number, string> = {};
  selectedTemplateId: number | null = null;
  templatePlanName = '';
  templateStartDate = new Date().toISOString().slice(0, 10);
  mesocycles: Record<number, TrainingMesocycle[]> = {};
  macrocyclePlans: Record<number, MacrocyclePlan | null> = {};
  selectedMacroPhaseId: Record<number, number> = {};
  macroProposals: Record<number, Array<{ name: string; sequence: number; startDate: string; endDate: string; objective: string; phaseType: MacrocyclePhase['phaseType'] }>> = {};
  macroProposalWeeks: Record<number, number> = {};

  microcycles: Record<number, TrainingMicrocycle[]> = {};
  mesocycleDrafts: Record<number, { name: string; sequence: number; startDate: string; endDate: string; objective: string }> = {};
  microcycleDrafts: Record<number, { name: string; sequence: number; startDate: string; endDate: string; focus: string }> = {};
  editingMesocycleId: number | null = null;
  editingMicrocycleId: number | null = null;
  mesocycleEditDraft: { name: string; sequence: number; startDate: string; endDate: string; objective: string; notes: string } | null = null;
  microcycleEditDraft: { name: string; sequence: number; startDate: string; endDate: string; focus: string; notes: string } | null = null;
  collaboratorEmail = '';
  acceptedClientId: number | null = null;
  profile: TrainingProfile | null = null;
  trainingManagementMode: 'none' | 'self' | 'collaboration' = 'self';
  autoGoal = 1;
  autoExperience = 0;
  autoDurationWeeks = 4;
  autoSplit = 0;
  macroDesignerOpen: Record<number, boolean> = {};
  manualMesoOpen: Record<number, boolean> = {};
  macroIncludeDeload = false;
  macroDeloadEvery = 4;
  // Optional manual override. Empty = the server rotates automatically with the mesocycle number.
  autoRotationIndex: number | null = null;
  autoIsDeload = false;
  autoMaximumMinutes = 60;
  autoTrainingDayOptions = [{ value: 1, label: 'Lunes' }, { value: 2, label: 'Martes' }, { value: 3, label: 'Miércoles' }, { value: 4, label: 'Jueves' }, { value: 5, label: 'Viernes' }, { value: 6, label: 'Sábado' }, { value: 0, label: 'Domingo' }];
  autoTrainingDaySelection: Record<number, boolean> = { 1: true, 2: false, 3: true, 4: false, 5: true, 6: false, 0: false };
  autoStartDates: Record<number, string> = {};
  autoTrainingDaysCsv = '1,3,5';
  autoEquipmentCsv = '';
  autoMusclePreferences: MuscleVolumePreferenceDraft[] = [
    { muscleGroup: 'Chest', label: 'Pecho', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Lats', label: 'Dorsales', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'UpperBack', label: 'Espalda alta', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Deltoids', label: 'Deltoides', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Biceps', label: 'Bíceps', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Triceps', label: 'Tríceps', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Quadriceps', label: 'Cuádriceps', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Hamstrings', label: 'Isquios', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Glutes', label: 'Glúteos', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Calves', label: 'Gemelos', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Abdominals', label: 'Abdominales', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Adductors', label: 'Aductores', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Abductors', label: 'Abductores', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Forearms', label: 'Antebrazos', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Obliques', label: 'Oblicuos', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'ErectorSpinae', label: 'Erectores espinales', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'HipFlexors', label: 'Flexores de cadera', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'Serratus', label: 'Serrato', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'TibialisAnterior', label: 'Tibial anterior', enabled: false, priority: 0, targetWeeklyPrimarySets: null },
    { muscleGroup: 'RotatorCuff', label: 'Manguito rotador', enabled: false, priority: 0, targetWeeklyPrimarySets: null }
  ];
  autoMesoNames: Record<number, string> = {};
  autoTargetMesocycleIds: Record<number, number | null> = {};
  generatedPreviews: Record<number, GeneratedProgram | null> = {};
  volumeDeficitAcknowledged: Record<number, boolean> = {};
  splitVolumeDeficitAcknowledged: Record<number, boolean> = {};
  generationRequests: Record<number, GenerationRequest | null> = {};
  generationErrors: Record<number, string> = {};
  generatingPlan = false;
  activePlanWorkspace: Record<number, 'overview' | 'macrocycle' | 'mesocycles' | 'routines'> = {};
  showCreatePlanForm = false;
  showQuickRoutineForm = false;
  newPlanTarget: 'macrocycle' | 'mesocycles' = 'macrocycle';
  editingPlanId: number | null = null;
  planEditDraft: { name: string; periodizationModel: string; startDate: string; endDate: string; notes: string } | null = null;
  quickRoutineName = 'Rutina sencilla';
  quickRoutineDate = '';
  quickRoutineType = 'strength';
  quickRoutineDuration = 45;
  quickRoutineIntensity = 'Moderada';
  quickRoutineInstructions = '';
  quickRoutineExercises = '';
  plans: TrainingPlan[] = [];
  exportingTestPlan = false;
  diagnosticAccess = false;
  diagnosticPlansLoading = false;
  diagnosticPlans: Array<{
    id: number; name: string; periodizationModel: string; startDate: string; endDate: string | null;
    status: string; version: number; notes: string | null; clientId: number; clientName: string;
    profileEnabled: boolean; clientArchived: boolean; mesocycleCount: number; sessionCount: number; recordCount: number;
  }> = [];
  diagnosticExportingPlanId: number | null = null;
  trainingPdfExportingPlanId: number | null = null;
  trainingPdfStartDate = this.defaultTrainingPdfPeriod().start;
  trainingPdfEndDate = this.defaultTrainingPdfPeriod().end;
  diagnosticDeletingPlanId: number | null = null;
  sessions: Record<number, TrainingSession[]> = {};
  loadTrends: Record<number, TrainingLoadWeek[]> = {};
  sessionViews: Record<number, 'week' | 'list'> = {};
  weekOffsets: Record<number, number> = {};
  weeklyEditorOpen: Record<number, boolean> = {};
  weeklyEditorDrafts: Record<number, WeeklySessionDraft> = {};
  weeklyEditorDirty: Record<number, boolean> = {};
  weeklyDeleteConfirmIds: Record<number, number | null> = {};
  weeklyDeleteErrors: Record<number, string> = {};
  splitReorganizationOpen: Record<number, boolean> = {};
  splitReorganizationDrafts: Record<number, SplitReorganizationDraft> = {};
  splitPreferences: Record<number, { splitType: number; trainingDays: number[] }> = {};
  splitReorganizationSaving: Record<number, boolean> = {};
  splitReplacementPreviews: Record<number, SplitReplacementPreview | null> = {};
  splitReplacementErrors: Record<number, string> = {};
  splitReplacementLoading: Record<number, boolean> = {};
  sessionDrafts: Record<number, { date: string; title: string; type: string; duration: number | null; intensity: string; instructions: string; exercisePrescription: string; exercisePrescriptionData: Record<string, unknown> | null }> = {};
  manualVolumePreviews: Record<number, ManualVolumePreview | null> = {};
  manualVolumePreviewErrors: Record<number, string> = {};
  manualVolumePreviewLoading: Record<number, boolean> = {};
  private manualVolumePreviewTimers: Record<number, ReturnType<typeof setTimeout>> = {};
  private manualVolumePreviewSubscriptions: Record<number, Subscription> = {};
  exerciseSearch = '';
  private exerciseCatalogSearchTimer: ReturnType<typeof setTimeout> | null = null;
  private exerciseCatalogRequest: Subscription | null = null;
  private filteredExercisesCacheKey = '';
  private filteredExercisesCache: Array<{ id: string; name: string; muscles: string; goal: string; equipment: string; equipmentLabel: string; instructions: string; prescription: string; imageUrl?: string; source?: string }> = [];
  private filteredExercisesCacheCatalog: object | null = null;
  exerciseGoalFilter = 'all';
  exerciseEquipmentFilter = 'all';
  exerciseMuscleFilter = 'all';
  standaloneExerciseSets = 3;
  standaloneExerciseReps = '8–12';
  standaloneExerciseRest = '90 s';
  sessionExerciseSets = 3;
  sessionExerciseReps = '8–12';
  sessionExerciseRest = '90 s';
  templateSessionId: number | null = null;
  generatorGoal = 'strength';
  generatorLevel = 'beginner';
  generatorEquipment = 'mixed';
  exerciseCatalog: TrainingExerciseCatalogItem[] = [
    { id:'squat', name:'Sentadilla', muscles:'Cuádriceps y glúteos', goal:'strength', equipment:'bodyweight', equipmentLabel:'Peso corporal', instructions:'Mantén el tronco estable y las rodillas alineadas con los pies.', prescription:'Sentadilla · 3 × 8–12 · descanso 90 s' },
    { id:'goblet-squat', name:'Sentadilla goblet', muscles:'Cuádriceps y glúteos', goal:'strength', equipment:'dumbbell', equipmentLabel:'Mancuerna', instructions:'Sujeta la mancuerna cerca del pecho y controla la bajada.', prescription:'Sentadilla goblet · 3 × 8–12 · descanso 90 s' },
    { id:'deadlift', name:'Peso muerto', muscles:'Cadena posterior', goal:'strength', equipment:'barbell', equipmentLabel:'Barra', instructions:'Bisagra de cadera, carga próxima al cuerpo y espalda neutra.', prescription:'Peso muerto · 3 × 5–8 · descanso 2–3 min' },
    { id:'rdl', name:'Peso muerto rumano', muscles:'Isquiotibiales y glúteos', goal:'strength', equipment:'dumbbell', equipmentLabel:'Mancuernas', instructions:'Lleva la cadera atrás y detén el recorrido antes de perder posición.', prescription:'Peso muerto rumano · 3 × 8–10 · descanso 90 s' },
    { id:'pushup', name:'Flexiones', muscles:'Pecho, hombro y tríceps', goal:'strength', equipment:'bodyweight', equipmentLabel:'Peso corporal', instructions:'Mantén el cuerpo alineado; adapta la inclinación al nivel.', prescription:'Flexiones · 3 × 6–12 · descanso 60–90 s' },
    { id:'bench', name:'Press de banca', muscles:'Pectoral y tríceps', goal:'strength', equipment:'barbell', equipmentLabel:'Barra', instructions:'Controla la bajada y evita rebotes sobre el pecho.', prescription:'Press de banca · 3 × 6–10 · descanso 2 min' },
    { id:'row', name:'Remo con mancuerna', muscles:'Espalda y bíceps', goal:'strength', equipment:'dumbbell', equipmentLabel:'Mancuerna', instructions:'Evita girar el tronco y lleva el codo hacia la cadera.', prescription:'Remo con mancuerna · 3 × 8–12 por lado · descanso 90 s' },
    { id:'band-row', name:'Remo con banda', muscles:'Espalda y bíceps', goal:'strength', equipment:'band', equipmentLabel:'Banda elástica', instructions:'Mantén hombros bajos y controla el retorno.', prescription:'Remo con banda · 3 × 12–15 · descanso 60 s' },
    { id:'plank', name:'Plancha frontal', muscles:'Core', goal:'core', equipment:'bodyweight', equipmentLabel:'Peso corporal', instructions:'Mantén pelvis neutra y respira sin perder la posición.', prescription:'Plancha frontal · 3 × 20–40 s · descanso 45–60 s' },
    { id:'dead-bug', name:'Dead bug', muscles:'Core y control motor', goal:'core', equipment:'bodyweight', equipmentLabel:'Peso corporal', instructions:'Extiende brazo y pierna contrarios sin arquear la zona lumbar.', prescription:'Dead bug · 3 × 8 por lado · descanso 45 s' },
    { id:'lunge', name:'Zancadas', muscles:'Piernas y estabilidad', goal:'strength', equipment:'bodyweight', equipmentLabel:'Peso corporal', instructions:'Da un paso estable y controla la rodilla durante el descenso.', prescription:'Zancadas · 3 × 8 por lado · descanso 60–90 s' },
    { id:'shoulder-press', name:'Press de hombros', muscles:'Hombros y tríceps', goal:'strength', equipment:'dumbbell', equipmentLabel:'Mancuernas', instructions:'Evita compensar arqueando la espalda.', prescription:'Press de hombros · 3 × 8–12 · descanso 90 s' },
    { id:'bike', name:'Bicicleta estática', muscles:'Sistema cardiovascular', goal:'endurance', equipment:'cardio', equipmentLabel:'Cardio', instructions:'Ajusta resistencia para mantener la intensidad prevista.', prescription:'Bicicleta · 10–20 min · intensidad conversacional' },
    { id:'walk', name:'Caminata rápida', muscles:'Sistema cardiovascular', goal:'endurance', equipment:'cardio', equipmentLabel:'Sin material', instructions:'Ritmo vivo que permita hablar en frases completas.', prescription:'Caminata rápida · 15–30 min · intensidad moderada' },
    { id:'mobility-hip', name:'Movilidad de cadera', muscles:'Cadera', goal:'mobility', equipment:'bodyweight', equipmentLabel:'Peso corporal', instructions:'Recorrido cómodo, lento y sin dolor.', prescription:'Movilidad de cadera · 2 × 8 por lado · controlada' },
    { id:'band-pullapart', name:'Aperturas con banda', muscles:'Espalda alta y hombros', goal:'strength', equipment:'band', equipmentLabel:'Banda elástica', instructions:'Mantén costillas controladas y separa la banda sin encoger hombros.', prescription:'Aperturas con banda · 3 × 12–15 · descanso 45–60 s' },
    { id:'leg-press', name:'Prensa de piernas', muscles:'Cuádriceps y glúteos', goal:'strength', equipment:'machine', equipmentLabel:'Máquina', instructions:'No bloquees las rodillas y usa un recorrido que controles.', prescription:'Prensa de piernas · 3 × 10–12 · descanso 90 s' }
  ];
  private exerciseMuscleLabels(muscles: string): string[] {
    if (typeof muscles !== 'string' || !muscles.trim()) return [];
    return muscles.split(/,|\s+y\s+|\s+and\s+|\s*\/\s*/i)
      .map(muscle => muscle.trim())
      .filter(muscle => muscle && !/^grupo muscular según ficha$/i.test(muscle));
  }

  get exerciseMuscleGroups(): string[] {
    const labels = new Map<string, string>();
    for (const exercise of this.exerciseCatalog) {
      if (!exercise || typeof exercise !== 'object') continue;
      for (const muscle of this.exerciseMuscleLabels(exercise.muscles)) {
        const key = muscle.toLocaleLowerCase('es');
        if (!labels.has(key)) labels.set(key, muscle);
      }
    }
    return [...labels.values()].sort((a, b) => a.localeCompare(b, 'es'));
  }

  get filteredExercises() {
    const query = this.exerciseSearch.trim().toLocaleLowerCase('es');
    const selectedMuscle = this.exerciseMuscleFilter.toLocaleLowerCase('es');
    const cacheKey = [query, this.exerciseGoalFilter, this.exerciseEquipmentFilter, selectedMuscle].join('|');
    if (cacheKey === this.filteredExercisesCacheKey && this.filteredExercisesCacheCatalog === this.exerciseCatalog) return this.filteredExercisesCache;
    this.filteredExercisesCacheKey = cacheKey;
    this.filteredExercisesCacheCatalog = this.exerciseCatalog;
    this.filteredExercisesCache = this.exerciseCatalog.filter(exercise =>
      !!exercise && typeof exercise.name === 'string' &&
      (!query || (exercise.name + ' ' + exercise.muscles + ' ' + exercise.instructions).toLocaleLowerCase('es').includes(query)) &&
      (this.exerciseGoalFilter === 'all' || exercise.goal === this.exerciseGoalFilter) &&
      (this.exerciseEquipmentFilter === 'all' || exercise.equipment === this.exerciseEquipmentFilter) &&
      (selectedMuscle === 'all' || this.exerciseMuscleLabels(exercise.muscles).some(muscle => muscle.toLocaleLowerCase('es') === selectedMuscle)));
    return this.filteredExercisesCache;
  }

  planName = '';
  periodizationModel = 'custom';
  startDate = new Date().toISOString().slice(0, 10);
  endDate = '';
  planNotes = '';

  constructor(private route: ActivatedRoute, private http: HttpClient, private changeDetector: ChangeDetectorRef, private plannerWizardState: PlannerWizardStateService) {
    this.quickRoutineDate = this.localDateInputValue();
    this.startDate = this.localDateInputValue();
  }

  ngOnInit(): void {
    this.loadExpandedExerciseCatalog();
    this.loadTrainingTemplates();
    this.loadSessionTemplates();
    const routeId = this.route.snapshot.paramMap.get('id');
    if (routeId === null) {
      this.clientId = 0;
      this.loadDiagnosticPlans();
      this.http.get<TrainingInvitation[]>('/api/training/collaborations/invitations').pipe(
        timeout({ first: 12000 }),
        catchError(() => {
          this.error = 'No se pudieron cargar las invitaciones de entrenamiento. Comprueba la conexión y vuelve a intentarlo.';
          this.loadFailed = true;
          return of([] as TrainingInvitation[]);
        })
      ).subscribe({
        next: items => {
          this.invitations = items;
          this.loading = false;
          this.changeDetector.detectChanges();
        },
        error: () => {
          // Keep a final safety net for any unexpected RxJS error.
          this.error = 'No se pudieron cargar las invitaciones de entrenamiento.';
          this.loadFailed = true;
          this.loading = false;
          this.changeDetector.detectChanges();
        }
      });
      return;
    }
    this.clientId = Number(routeId);
    if (!Number.isInteger(this.clientId) || this.clientId < 1) { this.error = 'Identificador de cliente no válido.'; this.loading = false; return; }
    this.loadInvitations();
    this.load();
  }

  loadTrainingTemplates(): void {
    this.http.get<TrainingTemplate[]>('/api/training/templates').pipe(catchError(() => of([] as TrainingTemplate[])))
      .subscribe(items => {
        // Treat API payloads as untrusted runtime data: a single null row must not crash Angular rendering.
        this.trainingTemplates = Array.isArray(items) ? items
          .filter((item): item is TrainingTemplate => !!item && typeof item === 'object' && Number.isInteger(item.id))
          .map(item => ({ ...item, name: typeof item.name === 'string' && item.name.trim() ? item.name : 'Plantilla sin nombre' })) : [];
        this.changeDetector.markForCheck();
      });
  }


  loadSessionTemplates(): void {
    this.http.get<TrainingSessionTemplate[]>('/api/training/session-templates')
      .pipe(catchError(() => of([] as TrainingSessionTemplate[])))
      .subscribe(items => {
        this.sessionTemplates = Array.isArray(items) ? items
          .filter((item): item is TrainingSessionTemplate => !!item && typeof item === 'object' && Number.isInteger(item.id))
          .map(item => ({ ...item, name: typeof item.name === 'string' && item.name.trim() ? item.name : 'Plantilla sin nombre' })) : [];
        this.changeDetector.markForCheck();
      });
  }

  addExerciseToStandaloneDraft(exercise: typeof this.exerciseCatalog[number]): void {
    const draft = this.standaloneSessionTemplateDraft;
    const lines = draft.exercisePrescription.trim() ? draft.exercisePrescription.split(/\r?\n/).map(line => line.trim()).filter(Boolean) : [];
    if (lines.some(line => line.toLocaleLowerCase('es').startsWith(exercise.name.toLocaleLowerCase('es')))) {
      this.notice = 'Ese ejercicio ya está incluido en la plantilla.';
      return;
    }
    draft.exercisePrescription = [...lines, this.buildExercisePrescription(exercise, this.standaloneExerciseSets, this.standaloneExerciseReps, this.standaloneExerciseRest)].join('\n');
    if (!draft.instructions.trim()) draft.instructions = exercise.instructions;
    this.notice = exercise.name + ' añadido a la plantilla.';
  }

  private buildExercisePrescription(exercise: typeof this.exerciseCatalog[number], sets: number, reps: string, rest: string): string {
    if (exercise.goal !== 'strength' && exercise.goal !== 'core') return exercise.prescription;
    const safeSets = Number.isFinite(sets) ? Math.max(1, Math.min(20, Math.round(sets))) : 3;
    const safeReps = reps.trim() || '8–12';
    const safeRest = rest.trim() || '90 s';
    return exercise.name + ' · ' + safeSets + ' × ' + safeReps + ' · descanso ' + safeRest;
  }

  prescriptionLines(prescription: string | null | undefined): string[] {
    return typeof prescription === 'string'
      ? prescription.split(/\r?\n/).map(line => line.trim()).filter(Boolean)
      : [];
  }

  updatePrescriptionLine(
    target: { exercisePrescription: string; exercisePrescriptionData?: Record<string, unknown> | null },
    index: number,
    value: string,
    plan?: TrainingPlan
  ): void {
    const lines = this.prescriptionLines(target.exercisePrescription);
    if (index < 0 || index >= lines.length) return;
    lines[index] = value;
    target.exercisePrescription = lines.join('\n');
    if ('exercisePrescriptionData' in target) target.exercisePrescriptionData = null;
    if (plan) this.refreshManualVolumeForTarget(target, plan);
  }

  movePrescriptionLine(
    target: { exercisePrescription: string; exercisePrescriptionData?: Record<string, unknown> | null },
    index: number,
    offset: number,
    plan?: TrainingPlan
  ): void {
    const lines = this.prescriptionLines(target.exercisePrescription);
    const destination = index + offset;
    if (index < 0 || index >= lines.length || destination < 0 || destination >= lines.length) return;
    [lines[index], lines[destination]] = [lines[destination], lines[index]];
    target.exercisePrescription = lines.join('\n');
    if ('exercisePrescriptionData' in target) target.exercisePrescriptionData = null;
    if (plan) this.refreshManualVolumeForTarget(target, plan);
  }

  removePrescriptionLine(
    target: { exercisePrescription: string; exercisePrescriptionData?: Record<string, unknown> | null },
    index: number,
    plan?: TrainingPlan
  ): void {
    const lines = this.prescriptionLines(target.exercisePrescription);
    if (index < 0 || index >= lines.length) return;
    lines.splice(index, 1);
    target.exercisePrescription = lines.join('\n');
    if ('exercisePrescriptionData' in target) target.exercisePrescriptionData = null;
    if (plan) this.refreshManualVolumeForTarget(target, plan);
  }

  onManualPrescriptionChanged(plan: TrainingPlan): void {
    const draft = this.sessionDrafts[plan.id];
    if (!draft) return;
    draft.exercisePrescriptionData = null;
    this.refreshManualVolumePreview(plan);
  }

  onManualSessionDateChanged(plan: TrainingPlan, date: string): void {
    const draft = this.sessionDrafts[plan.id];
    if (!draft) return;
    draft.date = date;
    this.refreshManualVolumePreview(plan);
  }

  onEditedSessionPrescriptionChanged(plan: TrainingPlan, session: TrainingSession, value: string): void {
    if (!this.sessionEditDraft) return;
    this.sessionEditDraft.exercisePrescription = value;
    this.sessionEditDraft.exercisePrescriptionData = null;
    this.refreshManualVolumePreview(plan, this.sessionEditDraft.date, value, session.id);
  }

  onEditedSessionDateChanged(plan: TrainingPlan, session: TrainingSession, date: string): void {
    if (!this.sessionEditDraft) return;
    this.sessionEditDraft.date = date;
    const structuredExercises = this.sessionEditDraft.exercisePrescriptionData
      ? this.parseStructuredExerciseDrafts(this.sessionEditDraft.exercisePrescription, this.sessionEditDraft.exercisePrescriptionData)
      : undefined;
    this.refreshManualVolumePreview(plan, date, this.sessionEditDraft.exercisePrescription, session.id, undefined, structuredExercises);
  }

  private refreshManualVolumeForTarget(
    target: { exercisePrescription: string },
    plan: TrainingPlan
  ): void {
    if (target === this.sessionEditDraft && this.sessionEditDraft && this.editingSessionId !== null) {
      const structuredExercises = this.sessionEditDraft.exercisePrescriptionData
        ? this.parseStructuredExerciseDrafts(this.sessionEditDraft.exercisePrescription, this.sessionEditDraft.exercisePrescriptionData)
        : undefined;
      this.refreshManualVolumePreview(plan, this.sessionEditDraft.date,
        this.sessionEditDraft.exercisePrescription, this.editingSessionId, undefined, structuredExercises);
    } else {
      this.refreshManualVolumePreview(plan);
    }
  }

  private findUniqueCatalogExerciseByName(name: string): TrainingExerciseCatalogItem | undefined {
    const normalized = name.trim().split(/\s+/).join(' ').toLocaleLowerCase('es');
    if (!normalized) return undefined;
    const matches = this.exerciseCatalog.filter(exercise =>
      exercise.name.trim().split(/\s+/).join(' ').toLocaleLowerCase('es') === normalized);
    return matches.length === 1 ? matches[0] : undefined;
  }

  private parseManualVolumeExercises(prescription: string | null | undefined): Array<{ exerciseId: string | null; exerciseName: string; sets: number }> {
    const parsed: Array<{ exerciseId: string | null; exerciseName: string; sets: number }> = [];
    for (const line of this.prescriptionLines(prescription)) {
      const match = line.match(/^(.*?)\s*[·|—-]\s*(\d+)\s*[×x]\s*\d+/i);
      if (!match) continue;
      const exerciseName = match[1].trim().replace(/^\d+\.\s*/, '');
      const sets = Number(match[2]);
      if (!exerciseName || !Number.isInteger(sets) || sets < 1 || sets > 10) continue;
      const item = this.findUniqueCatalogExerciseByName(exerciseName);
      const stableId = line.match(/\[ID:\s*([^;\]]+)/i)?.[1]?.trim();
      parsed.push({
        exerciseId: stableId || (item ? item.id.replace(/^repdb-/, '') : null),
        exerciseName,
        sets
      });
    }
    return parsed;
  }

  private manualVolumeExercisesFromStructuredDrafts(
    exercises: StructuredExerciseDraft[]
  ): Array<{ exerciseId: string | null; exerciseName: string; sets: number }> {
    return exercises
      .filter(item => item.exerciseName.trim().length > 0 && Number.isFinite(item.sets) && item.sets >= 1)
      .map(item => {
        const catalogItem = this.findUniqueCatalogExerciseByName(item.exerciseName);
        return {
          exerciseId: item.exerciseId?.trim() || catalogItem?.id.replace(/^repdb-/, '') || null,
          exerciseName: item.exerciseName.trim(),
          sets: Math.max(1, Math.min(10, Math.round(item.sets)))
        };
      });
  }

  private weekStartDate(dateValue: string): string {
    const date = new Date(dateValue.slice(0, 10) + 'T00:00:00Z');
    if (Number.isNaN(date.getTime())) return '';
    date.setUTCDate(date.getUTCDate() - ((date.getUTCDay() + 6) % 7));
    return date.toISOString().slice(0, 10);
  }

  private refreshManualVolumePreview(
    plan: TrainingPlan,
    overrideDate?: string,
    overridePrescription?: string,
    excludeSessionId?: number,
    overrideTitle?: string,
    overrideExercises?: StructuredExerciseDraft[]
  ): void {
    const planId = plan.id;
    const existingTimer = this.manualVolumePreviewTimers[planId];
    if (existingTimer) clearTimeout(existingTimer);
    this.manualVolumePreviewSubscriptions[planId]?.unsubscribe();
    this.manualVolumePreviewLoading[planId] = true;
    this.manualVolumePreviewErrors[planId] = '';

    this.manualVolumePreviewTimers[planId] = setTimeout(() => {
      const draft = this.sessionDrafts[planId];
      const targetDate = (overrideDate || draft?.date || this.defaultSessionDate(plan)).slice(0, 10);
      const targetWeek = this.weekStartDate(targetDate);
      const requestSessions: Array<{ weekNumber: number; day: number; title: string; exercises: Array<{ exerciseId: string | null; exerciseName: string; sets: number }> }> = [];
      const addSession = (
        dateValue: string,
        title: string,
        prescription: string | null | undefined,
        prescriptionData?: Record<string, unknown> | null
      ): void => {
        const date = dateValue.slice(0, 10);
        if (!date || this.weekStartDate(date) !== targetWeek) return;
        const structuredDrafts = prescriptionData
          ? this.parseStructuredExerciseDrafts(prescription, prescriptionData)
          : [];
        const exercises = structuredDrafts.length
          ? this.manualVolumeExercisesFromStructuredDrafts(structuredDrafts)
          : this.parseManualVolumeExercises(prescription);
        if (!exercises.length) return;
        requestSessions.push({ weekNumber: 1, day: new Date(date + 'T00:00:00Z').getUTCDay(), title, exercises });
      };

      for (const session of this.sessions[planId] || []) {
        if (session.id === excludeSessionId) continue;
        addSession(session.scheduledDate, session.title, session.exercisePrescription, session.exercisePrescriptionData);
      }
      const currentPrescription = overridePrescription ?? draft?.exercisePrescription ?? '';
      const currentStructuredExercises = overrideExercises ??
        (draft?.exercisePrescriptionData
          ? this.parseStructuredExerciseDrafts(currentPrescription, draft.exercisePrescriptionData)
          : []);
      const currentExercises = currentStructuredExercises.length
        ? this.manualVolumeExercisesFromStructuredDrafts(currentStructuredExercises)
        : this.parseManualVolumeExercises(currentPrescription);
      const currentDate = (overrideDate || draft?.date || targetDate).slice(0, 10);
      if (currentDate && this.weekStartDate(currentDate) === targetWeek && currentExercises.length) {
        requestSessions.push({
          weekNumber: 1,
          day: new Date(currentDate + 'T00:00:00Z').getUTCDay(),
          title: overrideTitle || draft?.title || 'Sesión en edición',
          exercises: currentExercises
        });
      }
      if (!requestSessions.length) {
        this.manualVolumePreviewSubscriptions[planId]?.unsubscribe();
        this.manualVolumePreviews[planId] = null;
        this.manualVolumePreviewLoading[planId] = false;
        this.changeDetector.markForCheck();
        return;
      }

      this.manualVolumePreviewSubscriptions[planId]?.unsubscribe();
      this.manualVolumePreviewSubscriptions[planId] = this.http.post<ManualVolumePreview>(
        '/api/training/volume-preview', { sessions: requestSessions }
      ).subscribe({
        next: result => {
          this.manualVolumePreviews[planId] = result;
          this.manualVolumePreviewLoading[planId] = false;
          this.changeDetector.markForCheck();
        },
        error: error => {
          this.manualVolumePreviews[planId] = null;
          this.manualVolumePreviewErrors[planId] = error?.error?.message || 'No se pudo calcular el volumen semanal con el catálogo revisado.';
          this.manualVolumePreviewLoading[planId] = false;
          this.changeDetector.markForCheck();
        }
      });
    }, 250);
  }

  muscleSetSummary(prescription: string | null | undefined): Array<{ muscle: string; sets: number }> {
    if (!prescription?.trim()) return [];
    const totals = new Map<string, number>();
    for (const line of prescription.split(/\r?\n/)) {
      const match = line.match(/^(.*?)\s*[·|—-]\s*(\d+)\s*[×x]\s*\d+/i);
      if (!match) continue;
      const exerciseName = match[1].trim().toLocaleLowerCase('es');
      const sets = Number(match[2]);
      if (!Number.isFinite(sets) || sets < 1) continue;
      const exercise = this.findUniqueCatalogExerciseByName(exerciseName);
      if (!exercise?.muscles) continue;
      const muscles = exercise.muscles.split(/,|\s+y\s+|\s+and\s+|\s*\/\s*/i).map(value => value.trim()).filter(Boolean);
      for (const muscle of muscles) totals.set(muscle, (totals.get(muscle) ?? 0) + sets);
    }
    return [...totals.entries()].map(([muscle, sets]) => ({ muscle, sets })).sort((a, b) => b.sets - a.sets || a.muscle.localeCompare(b.muscle, 'es'));
  }

  saveStandaloneSessionTemplate(): void {
    const draft = this.standaloneSessionTemplateDraft;
    const name = draft.name.trim();
    if (!name) return;
    if (!Number.isFinite(draft.duration) || draft.duration === null || draft.duration < 1 || draft.duration > 1440) {
      this.error = 'La duración debe estar entre 1 y 1440 minutos.';
      return;
    }
    if (!draft.exercisePrescription.trim() && !draft.instructions.trim()) {
      this.error = 'Añade ejercicios o instrucciones antes de guardar la plantilla.';
      return;
    }
    this.saving = true; this.error = ''; this.notice = '';
    this.http.post<TrainingSessionTemplate>('/api/training/session-templates', {
      name, sessionType: draft.type, durationMinutes: draft.duration,
      intensityTarget: draft.intensity.trim() || null,
      instructions: draft.instructions.trim() || null,
      exercisePrescription: draft.exercisePrescription.trim() || null,
      exercisePrescriptionData: draft.exercisePrescriptionData
    }).subscribe({
      next: () => {
        this.saving = false;
        this.standaloneSessionTemplateDraft = { name: '', type: 'strength', duration: 60, intensity: '', instructions: '', exercisePrescription: '', exercisePrescriptionData: null };
        this.notice = 'Plantilla de sesión guardada en tu biblioteca.';
        this.loadSessionTemplates();
      },
      error: e => { this.saving = false; this.error = e?.error?.message || 'No se pudo guardar la plantilla de sesión.'; }
    });
  }

  saveSessionAsTemplate(plan: TrainingPlan): void {
    const draft = this.sessionDrafts[plan.id];
    const name = (this.sessionTemplateNameDrafts[plan.id] || '').trim();
    if (!draft || !name) return;
    if (!draft.exercisePrescription.trim() && !draft.instructions.trim()) {
      this.error = 'Añade ejercicios o instrucciones antes de guardar una plantilla de sesión.';
      return;
    }
    this.saving = true; this.error = ''; this.notice = '';
    this.http.post<TrainingSessionTemplate>('/api/training/session-templates', {
      name, sessionType: draft.type, durationMinutes: draft.duration,
      intensityTarget: draft.intensity.trim() || null,
      instructions: draft.instructions.trim() || null,
      exercisePrescription: draft.exercisePrescription.trim() || null
    }).subscribe({
      next: () => {
        this.saving = false;
        this.sessionTemplateNameDrafts[plan.id] = '';
        this.notice = 'Plantilla de sesión guardada en tu biblioteca.';
        this.loadSessionTemplates();
      },
      error: e => { this.saving = false; this.error = e?.error?.message || 'No se pudo guardar la plantilla de sesión.'; }
    });
  }

  applySavedSessionTemplate(plan: TrainingPlan): void {
    const templateId = this.selectedSessionTemplateIds[plan.id];
    const saved = this.sessionTemplates.find(item => item.id === templateId);
    const draft = this.sessionDrafts[plan.id];
    if (!saved || !draft) return;
    draft.title = saved.name;
    draft.type = saved.sessionType;
    draft.duration = saved.durationMinutes ?? 60;
    draft.intensity = saved.intensityTarget ?? '';
    draft.instructions = saved.instructions ?? '';
    draft.exercisePrescription = saved.exercisePrescription ?? '';
    draft.exercisePrescriptionData = saved.exercisePrescriptionData ?? null;
    this.notice = 'Plantilla «' + saved.name + '» cargada. Ajusta el nombre y la fecha antes de añadir la sesión.';
  }

  deleteSessionTemplate(template: TrainingSessionTemplate): void {
    if (!confirm('¿Eliminar la plantilla de sesión «' + template.name + '»? Las sesiones ya creadas no cambiarán.')) return;
    this.saving = true; this.error = '';
    this.http.delete('/api/training/session-templates/' + template.id).subscribe({
      next: () => {
        this.saving = false;
        this.notice = 'Plantilla eliminada. Las sesiones ya creadas se conservan.';
        this.loadSessionTemplates();
      },
      error: e => { this.saving = false; this.error = e?.error?.message || 'No se pudo eliminar la plantilla.'; }
    });
  }

  savePlanAsTemplate(plan: TrainingPlan): void {
    const name = (this.templateNameDrafts[plan.id] || '').trim();
    if (!name) return;
    this.saving = true; this.error = ''; this.notice = '';
    this.http.post<TrainingTemplate>(`/api/training/plans/${plan.id}/templates`, { name }).subscribe({
      next: () => {
        this.saving = false;
        this.templateNameDrafts[plan.id] = '';
        this.notice = 'Plantilla guardada. Puedes reutilizarla en otros clientes de tu clínica.';
        this.loadTrainingTemplates();
      },
      error: e => { this.saving = false; this.error = e?.error?.message || 'No se pudo guardar la plantilla.'; }
    });
  }

  createPlanFromTemplate(): void {
    if (!this.selectedTemplateId || !this.templatePlanName.trim() || !this.templateStartDate || !this.clientId) return;
    this.saving = true; this.error = ''; this.notice = '';
    this.http.post<{ id: number; copiedSessions: number }>(
      `/api/training/clients/${this.clientId}/plans/from-template/${this.selectedTemplateId}`,
      { name: this.templatePlanName.trim(), startDate: this.templateStartDate }
    ).subscribe({
      next: result => {
        this.saving = false;
        this.notice = `Plan creado desde plantilla con ${result.copiedSessions} sesiones. El original no se ha modificado.`;
        this.templatePlanName = '';
        this.load();
      },
      error: e => { this.saving = false; this.error = e?.error?.message || 'No se pudo crear el plan desde la plantilla.'; }
    });
  }

  loadExpandedExerciseCatalog(): void {
    // Evita una petición por tecla y cancela búsquedas obsoletas; el catálogo anterior permanece visible mientras se busca.
    if (this.exerciseCatalogSearchTimer !== null) clearTimeout(this.exerciseCatalogSearchTimer);
    this.exerciseCatalogRequest?.unsubscribe();
    const query = this.exerciseSearch.trim();
    if (query.length === 1) return;
    this.exerciseCatalogSearchTimer = setTimeout(() => {
      this.exerciseCatalogSearchTimer = null;
      const url = '/api/exercise-catalog?limit=100' + (query ? '&q=' + encodeURIComponent(query) : '');
      this.exerciseCatalogRequest = this.http.get<RepDbExercise[]>(url)
      .pipe(timeout(12000), catchError(() => of([] as RepDbExercise[])))
      .subscribe(data => {
        const safeData = Array.isArray(data) ? data : [];
        const imported = safeData.filter((item): item is RepDbExercise => !!item && typeof item === 'object' && typeof item.id === 'string' && typeof item.nameEs === 'string' && !!item.nameEs.trim())
          .map(item => {
            const equipmentRaw = String(item.equipment ?? '').toLocaleLowerCase('es');
            const bodyPart = String(item.bodyPart ?? '').toLocaleLowerCase('es');
            const category = String(item.category ?? '').toLocaleLowerCase('es');
            const goals = Array.isArray(item.goals) ? item.goals.join(' ').toLocaleLowerCase('es') : '';
            const primary = Array.isArray(item.primaryMuscles) ? item.primaryMuscles : [];
            const secondary = Array.isArray(item.secondaryMuscles) ? item.secondaryMuscles : [];
            const muscles = [...primary, ...secondary].join(', ').replace(/[-_]/g, ' ') || bodyPart.replace(/[-_]/g, ' ');
            const goal = /endurance|cardio|aerobic/.test(goals + ' ' + category) ? 'endurance'
              : /mobility|stretch|flexib/.test(goals + ' ' + category) ? 'mobility'
              : /core|abdom|oblique/.test(bodyPart + ' ' + muscles) ? 'core' : 'strength';
            const equipment = /bike|treadmill|elliptical|cardio/.test(equipmentRaw + ' ' + category) ? 'cardio'
              : /band|resistance-band/.test(equipmentRaw) ? 'band'
              : /barbell|ez-bar|weight-plate/.test(equipmentRaw) ? 'barbell'
              : /dumbbell|kettlebell|medicine-ball/.test(equipmentRaw) ? 'dumbbell'
              : /bodyweight|body-weight|none|no-equipment/.test(equipmentRaw) || !equipmentRaw ? 'bodyweight'
              : /machine|cable|smith/.test(equipmentRaw) ? 'machine' : 'machine';
            const equipmentLabel = equipmentRaw ? equipmentRaw.replace(/[-_]/g, ' ') : 'Peso corporal';
            const instructions = Array.isArray(item.instructionsEs) ? item.instructionsEs.join(' ') :
              (typeof item.descriptionEs === 'string' ? item.descriptionEs : 'Realiza el movimiento con control y dentro de un rango cómodo.');
            const imagePath = item.images?.flat?.start ?? item.images?.flat?.main ?? item.images?.flat?.peak;
            const name = (item.nameEs || item.nameEn || '').trim();
            if (!name) return null;
            const prescription = goal === 'endurance' ? name + ' · 10–20 min · intensidad ajustable'
              : goal === 'mobility' ? name + ' · 2 × 8–12 repeticiones controladas'
              : name + ' · 3 × 8–12 · ajustar carga y descanso';
            return {
              id: 'repdb-' + item.id, name, muscles: muscles || 'Grupo muscular según ficha',
              goal, equipment, equipmentLabel: equipmentLabel.charAt(0).toLocaleUpperCase('es') + equipmentLabel.slice(1),
              instructions, prescription,
              // Pictures are served by our own backend (same origin, session cookie); no third-party host is contacted.
              imageUrl: typeof imagePath === 'string' ? '/api/exercise-catalog/' + encodeURIComponent(String(item.id)) + '/images/0' : undefined,
              source: 'repdb'
            };
          }).filter((item): item is NonNullable<typeof item> => !!item && typeof item.name === 'string' && !!item.name.trim());
        if (imported.length) {
          const curated = this.exerciseCatalog.filter(item => !item.id.startsWith('repdb-'));
          const seen = new Set(imported.map(item => item.name.toLocaleLowerCase('es')));
          this.exerciseCatalog = [...imported, ...curated.filter(item => !seen.has(item.name.toLocaleLowerCase('es')))];
          this.filteredExercisesCacheKey = '';
          this.changeDetector.markForCheck();
        }
      });
    }, query ? 300 : 0);
  }

  ngOnDestroy(): void {
    if (this.exerciseCatalogSearchTimer !== null) clearTimeout(this.exerciseCatalogSearchTimer);
    this.exerciseCatalogRequest?.unsubscribe();
    for (const timer of Object.values(this.manualVolumePreviewTimers)) clearTimeout(timer);
    for (const subscription of Object.values(this.manualVolumePreviewSubscriptions)) subscription.unsubscribe();
  }

  loadInvitations(): void {
    this.http.get<TrainingInvitation[]>('/api/training/collaborations/invitations').pipe(catchError(() => of([] as TrainingInvitation[])))
      .subscribe(items => this.invitations = Array.isArray(items) ? items
        .filter((item): item is TrainingInvitation => !!item && typeof item === 'object' && Number.isInteger(item.id))
        .map(item => ({ ...item, clientName: typeof item.clientName === 'string' ? item.clientName : 'Cliente', invitedBy: typeof item.invitedBy === 'string' ? item.invitedBy : 'Profesional' })) : []);
  }

  loadCollaborations(): void {
    this.http.get<TrainingCollaboration[]>(`/api/training/clients/${this.clientId}/collaborations`)
      .pipe(catchError(() => of([] as TrainingCollaboration[])))
      .subscribe(items => this.collaborations = Array.isArray(items) ? items
        .filter((item): item is TrainingCollaboration => !!item && typeof item === 'object' && Number.isInteger(item.id))
        .map(item => ({ ...item, professionalName: typeof item.professionalName === 'string' ? item.professionalName : 'Profesional', email: typeof item.email === 'string' ? item.email : '' })) : []);
  }


  beginEditMesocycle(meso: TrainingMesocycle): void {
    this.editingMesocycleId = meso.id;
    this.mesocycleEditDraft = { name: meso.name, sequence: meso.sequenceNumber, startDate: meso.startDate, endDate: meso.endDate ?? '', objective: meso.objective ?? '', notes: meso.notes ?? '' };
    this.editingMicrocycleId = null; this.microcycleEditDraft = null;
  }

  saveMesocycle(meso: TrainingMesocycle): void {
    const d = this.mesocycleEditDraft;
    if (!d || !d.name.trim() || !d.startDate || !Number.isInteger(d.sequence) || d.sequence < 1) return;
    this.saving = true; this.error = '';
    this.http.put('/api/training/mesocycles/' + meso.id, { name: d.name.trim(), sequenceNumber: d.sequence, startDate: d.startDate, endDate: d.endDate || null, objective: d.objective.trim() || null, notes: d.notes.trim() || null }).subscribe({
      next: () => { this.editingMesocycleId = null; this.mesocycleEditDraft = null; this.saving = false; this.load(); },
      error: e => { this.error = e?.error?.message || 'No se pudo actualizar el mesociclo.'; this.saving = false; }
    });
  }

  deleteMesocycle(meso: TrainingMesocycle): void {
    if (!confirm('¿Eliminar el mesociclo «' + meso.name + '»? Solo se permite si no contiene microciclos.')) return;
    this.saving = true; this.error = '';
    this.http.delete('/api/training/mesocycles/' + meso.id).subscribe({
      next: () => { this.saving = false; this.load(); },
      error: e => { this.error = e?.error?.message || 'No se pudo eliminar el mesociclo.'; this.saving = false; }
    });
  }

  moveMesocycle(plan: TrainingPlan, meso: TrainingMesocycle, delta: number): void {
    const list = [...(this.mesocycles[plan.id] ?? [])];
    const index = list.findIndex(x => x.id === meso.id); const target = index + delta;
    if (index < 0 || target < 0 || target >= list.length || this.saving) return;
    [list[index], list[target]] = [list[target], list[index]];
    this.saving = true; this.error = '';
    this.http.post('/api/training/plans/' + plan.id + '/mesocycles/reorder', { orderedIds: list.map(x => x.id) }).subscribe({
      next: () => { this.saving = false; this.load(); },
      error: e => { this.error = e?.error?.message || 'No se pudo reordenar los mesociclos.'; this.saving = false; }
    });
  }

  beginEditMicrocycle(micro: TrainingMicrocycle): void {
    this.editingMicrocycleId = micro.id;
    this.microcycleEditDraft = { name: micro.name, sequence: micro.sequenceNumber, startDate: micro.startDate, endDate: micro.endDate ?? '', focus: micro.focus ?? '', notes: micro.notes ?? '' };
    this.editingMesocycleId = null; this.mesocycleEditDraft = null;
  }

  saveMicrocycle(micro: TrainingMicrocycle): void {
    const d = this.microcycleEditDraft;
    if (!d || !d.name.trim() || !d.startDate || !Number.isInteger(d.sequence) || d.sequence < 1) return;
    this.saving = true; this.error = '';
    this.http.put('/api/training/microcycles/' + micro.id, { name: d.name.trim(), sequenceNumber: d.sequence, startDate: d.startDate, endDate: d.endDate || null, focus: d.focus.trim() || null, notes: d.notes.trim() || null }).subscribe({
      next: () => { this.editingMicrocycleId = null; this.microcycleEditDraft = null; this.saving = false; this.load(); },
      error: e => { this.error = e?.error?.message || 'No se pudo actualizar el microciclo.'; this.saving = false; }
    });
  }

  deleteMicrocycle(micro: TrainingMicrocycle): void {
    if (!confirm('¿Eliminar el microciclo «' + micro.name + '»?')) return;
    this.saving = true; this.error = '';
    this.http.delete('/api/training/microcycles/' + micro.id).subscribe({
      next: () => { this.saving = false; this.load(); },
      error: e => { this.error = e?.error?.message || 'No se pudo eliminar el microciclo.'; this.saving = false; }
    });
  }

  moveMicrocycle(meso: TrainingMesocycle, micro: TrainingMicrocycle, delta: number): void {
    const list = [...(this.microcycles[meso.id] ?? [])];
    const index = list.findIndex(x => x.id === micro.id); const target = index + delta;
    if (index < 0 || target < 0 || target >= list.length || this.saving) return;
    [list[index], list[target]] = [list[target], list[index]];
    this.saving = true; this.error = '';
    this.http.post('/api/training/mesocycles/' + meso.id + '/microcycles/reorder', { orderedIds: list.map(x => x.id) }).subscribe({
      next: () => { this.saving = false; this.load(); },
      error: e => { this.error = e?.error?.message || 'No se pudo reordenar los microciclos.'; this.saving = false; }
    });
  }

  createMesocycle(plan: TrainingPlan): void {
    const d = this.mesocycleDrafts[plan.id];
    if (!d?.name.trim() || !d.startDate || !Number.isInteger(d.sequence) || d.sequence < 1) return;
    this.saving = true; this.error = '';
    this.http.post(`/api/training/plans/${plan.id}/mesocycles`, { name: d.name.trim(), sequenceNumber: d.sequence, startDate: d.startDate, endDate: d.endDate || null, objective: d.objective.trim() || null, notes: null }).subscribe({
      next: () => { d.name = ''; d.objective = ''; d.sequence += 1; this.saving = false; this.changeDetector.detectChanges(); this.load(); },
      error: e => { this.error = e?.error?.message || 'No se pudo crear el mesociclo.'; this.saving = false; }
    });
  }

  createMicrocycle(meso: TrainingMesocycle): void {
    const d = this.microcycleDrafts[meso.id];
    if (!d?.name.trim() || !d.startDate || !Number.isInteger(d.sequence) || d.sequence < 1) return;
    this.saving = true; this.error = '';
    this.http.post(`/api/training/mesocycles/${meso.id}/microcycles`, { name: d.name.trim(), sequenceNumber: d.sequence, startDate: d.startDate, endDate: d.endDate || null, focus: d.focus.trim() || null, notes: null }).subscribe({
      next: () => { d.name = ''; d.focus = ''; d.sequence += 1; this.saving = false; this.changeDetector.detectChanges(); this.http.get<TrainingMicrocycle[]>(`/api/training/mesocycles/${meso.id}/microcycles`).subscribe({ next: rows => { this.microcycles[meso.id] = rows; this.changeDetector.detectChanges(); }, error: () => { this.error = 'El microciclo se guardó, pero no se pudo actualizar la lista.'; this.changeDetector.detectChanges(); } }); },
      error: e => { this.error = e?.error?.message || 'No se pudo crear el microciclo.'; this.saving = false; }
    });
  }

  inviteCollaborator(): void {
    const email = this.collaboratorEmail.trim();
    if (!email) return;
    this.saving = true; this.error = '';
    this.http.post<{ emailSent: boolean; message: string }>('/api/training/clients/' + this.clientId + '/collaborations', { email, accessScope: 'training' }).subscribe({
      next: response => { this.collaboratorEmail = ''; this.notice = response.message; this.saving = false; this.loadCollaborations(); },
      error: e => { this.error = e?.error?.message || 'No se pudo crear la invitación.'; this.saving = false; }
    });
  }

  acceptInvitation(invitation: TrainingInvitation): void {
    this.saving = true; this.error = '';
    this.http.post(`/api/training/collaborations/${invitation.id}/accept`, {}).subscribe({
      next: () => { this.saving = false; this.acceptedClientId = invitation.clientId; this.invitations = this.invitations.filter(x => x.id !== invitation.id); },
      error: e => { this.error = e?.error?.message || 'No se pudo aceptar la invitación.'; this.saving = false; }
    });
  }

  revokeCollaboration(collaboration: TrainingCollaboration): void {
    if (!confirm('¿Revocar el acceso de este profesional al entrenamiento?')) return;
    this.saving = true; this.error = '';
    this.http.post(`/api/training/collaborations/${collaboration.id}/revoke`, {}).subscribe({
      next: () => { this.saving = false; this.loadCollaborations(); },
      error: e => { this.error = e?.error?.message || 'No se pudo revocar la colaboración.'; this.saving = false; }
    });
  }

  load(): void {
    const loadVersion = ++this.loadVersion;
    this.loading = true;
    this.loadFailed = false;
    this.error = '';
    this.profile = null;

    this.http.get<TrainingProfile>(`/api/training/clients/${this.clientId}/profile`).pipe(
      timeout({ first: 15000 }),
      catchError(() => {
        this.loadFailed = true;
        this.error = 'No se pudo cargar el perfil deportivo. Comprueba la conexión y vuelve a intentarlo.';
        return of(null);
      })
    ).subscribe({
      next: profile => {
        if (loadVersion !== this.loadVersion) return;
        this.profile = profile;
        this.changeDetector.detectChanges();
        if (!profile) {
          this.loading = false;
          this.changeDetector.detectChanges();
          return;
        }

        this.trainingManagementMode = profile.managementMode ?? (profile.enabled ? 'self' : 'none');
        if (profile.enabled && profile.canManage) this.loadAdaptationProposals();
        else this.adaptationProposals = [];
        if (profile.enabled && this.trainingManagementMode === 'collaboration') this.loadCollaborations();
        else this.collaborations = [];

        if (!profile.enabled) {
          this.loading = false;
          this.changeDetector.detectChanges();
          return;
        }

        this.loading = true;
        this.http.get<TrainingPlan[]>(`/api/training/clients/${this.clientId}/plans`).pipe(
          timeout({ first: 15000 }),
          catchError(() => {
            this.error = 'No se pudieron cargar los planes. Comprueba la conexión y vuelve a intentarlo.';
            return of(null);
          })
        ).subscribe(plans => {
          if (loadVersion !== this.loadVersion) return;
          if (plans === null) {
            this.loading = false;
            this.changeDetector.detectChanges();
            return;
          }

          try {
            const safePlans = Array.isArray(plans) ? plans
              .filter((plan): plan is TrainingPlan => !!plan && typeof plan === 'object' && Number.isInteger(plan.id))
              .map(plan => ({ ...plan, name: typeof plan.name === 'string' && plan.name.trim() ? plan.name : 'Plan sin nombre' })) : [];
            this.plans = safePlans;
            this.sessionDrafts = {};
            for (const plan of safePlans) {
              // Initialize every field used by plan forms before loading=false lets Angular render them.
              this.mesocycleDrafts[plan.id] = this.mesocycleDrafts[plan.id] ?? { name: '', sequence: 1, startDate: plan.startDate, endDate: plan.endDate ?? '', objective: '' };
              this.sessionDrafts[plan.id] = {
                date: this.defaultSessionDate(plan),
                title: '',
                type: 'strength',
                duration: 60,
                intensity: '',
                instructions: '',
                exercisePrescription: '',
                exercisePrescriptionData: null
              };
            }
          } catch {
            // A malformed plan must not leave the whole screen stuck on its loading state.
            this.error = 'La planificación se ha recibido, pero no se han podido preparar todos sus datos. Recarga e inténtalo de nuevo.';
            this.loadFailed = true;
          } finally {
            // The main plan list is the critical path. Session details and trends are loaded
            // independently and must never keep the entire screen behind the loading message.
            this.loading = false;
            this.changeDetector.detectChanges();
            this.applyDeepLink();
          }

          try {
            this.loadSessions(this.plans, loadVersion);
          } catch {
            this.error = this.error || 'La planificación se ha cargado, pero algunos detalles de las sesiones no están disponibles.';
          }
        });
      },
      error: () => {
        // Defensive fallback: no terminal HTTP/RxJS path should leave the view spinning.
        this.loadFailed = true;
        this.error = 'No se pudo cargar la planificación deportiva. Recarga la página y vuelve a intentarlo.';
        this.loading = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  /** Abre la rutina de una solicitud (o enlace profundo ?plan=) en el espacio de rutinas y la centra en pantalla. */
  openPlanFromRequest(planId: number): void {
    if (!this.plans.some(plan => plan.id === planId)) return;
    this.setPlanWorkspace(planId, 'routines');
    setTimeout(() => document.getElementById('training-plan-' + planId)?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 50);
  }

  private applyDeepLink(): void {
    if (this.deepLinkApplied) return;
    this.deepLinkApplied = true;
    const params = this.route.snapshot.queryParamMap;
    const request = Number(params.get('request'));
    this.deepLinkRequestId = Number.isInteger(request) && request > 0 ? request : null;
    const planId = Number(params.get('plan'));
    if (Number.isInteger(planId) && planId > 0) this.openPlanFromRequest(planId);
  }

  // ---- Estado de revisión de los bloques ----
  blockStatus(meso: TrainingMesocycle): { label: string; tone: 'draft' | 'upcoming' | 'running' | 'done' } {
    if (meso.reviewStatus === 'draft') return { label: 'Borrador sin revisar', tone: 'draft' };
    const today = this.localDateInputValue();
    const start = meso.startDate.slice(0, 10);
    const end = (meso.endDate || meso.startDate).slice(0, 10);
    if (today < start) return { label: 'Revisado · programado', tone: 'upcoming' };
    if (today > end) return { label: 'Completado', tone: 'done' };
    return { label: 'En curso', tone: 'running' };
  }

  blockToneStyle(tone: string): string {
    const colors: Record<string, string> = {
      draft: 'background:#fef3c7;color:#92400e', upcoming: 'background:#e0e7ff;color:#3730a3',
      running: 'background:#ccfbf1;color:#115e59', done: 'background:#e2e8f0;color:#334155'
    };
    return 'display:inline-block;padding:2px 9px;border-radius:999px;font-size:.78rem;font-weight:600;margin-left:8px;' + (colors[tone] ?? colors['done']);
  }

  phaseMesocycle(plan: TrainingPlan, phase: MacrocyclePhase): TrainingMesocycle | undefined {
    const start = phase.startDate.slice(0, 10);
    return (this.mesocycles[plan.id] || []).find(meso => meso.startDate.slice(0, 10) <= start && (meso.endDate || meso.startDate).slice(0, 10) >= start);
  }

  setMesocycleReviewStatus(meso: TrainingMesocycle, status: 'draft' | 'reviewed'): void {
    this.saving = true;
    this.http.post(`/api/training/mesocycles/${meso.id}/review-status`, { status }).subscribe({
      next: () => {
        meso.reviewStatus = status;
        this.notice = status === 'reviewed' ? 'Bloque «' + meso.name + '» marcado como revisado.' : 'Bloque «' + meso.name + '» devuelto a borrador.';
        this.saving = false;
        this.changeDetector.detectChanges();
      },
      error: e => { this.error = e?.error?.message || 'No se pudo cambiar el estado de revisión del bloque.'; this.saving = false; this.changeDetector.detectChanges(); }
    });
  }

  /** Vacía los borradores futuros intactos y vuelve a generarlos; lo revisado, empezado o editado a mano se conserva. */
  async regenerateFutureDrafts(plan: TrainingPlan): Promise<void> {
    if (this.saving || this.generatingPlan || this.bulkGenerationProgress[plan.id]) return;
    type Summary = { cleared: Array<{ id: number; name: string; startDate: string; sessions: number }>; kept: Array<{ id: number; name: string; reason: string }> };
    try {
      const preview = await firstValueFrom(this.http.post<Summary>(`/api/training/plans/${plan.id}/mesocycles/clear-future-drafts?dryRun=true`, {}));
      if (!preview.cleared.length) {
        this.notice = 'No hay bloques futuros en borrador que se puedan regenerar' + (preview.kept.length ? ' (' + preview.kept.length + ' se conservan por estar revisados, empezados o editados).' : '.');
        return;
      }
      const sessions = preview.cleared.reduce((total, item) => total + item.sessions, 0);
      if (!confirm('Se vaciarán ' + preview.cleared.length + ' bloque(s) futuro(s) en borrador (' + sessions + ' sesiones) y se volverán a generar con el historial actual.' +
        (preview.kept.length ? '\n\nSe conservarán ' + preview.kept.length + ' bloque(s): revisados, empezados o con ediciones manuales.' : '') + '\n\n¿Continuar?')) return;
      const result = await firstValueFrom(this.http.post<Summary>(`/api/training/plans/${plan.id}/mesocycles/clear-future-drafts`, {}));
      for (const item of result.cleared) {
        this.microcycles[item.id] = [];
        const start = item.startDate.slice(0, 10);
        const meso = (this.mesocycles[plan.id] || []).find(entry => entry.id === item.id);
        const end = (meso?.endDate || meso?.startDate || start).slice(0, 10);
        this.sessions[plan.id] = (this.sessions[plan.id] || []).filter(session => session.scheduledDate.slice(0, 10) < start || session.scheduledDate.slice(0, 10) > end);
      }
      await this.generateAllMacrocycleMesocycles(plan, true);
    } catch (error: any) {
      this.generationErrors[plan.id] = error?.error?.message || error?.message || 'No se pudieron regenerar los bloques futuros.';
      this.changeDetector.detectChanges();
    }
  }

  historyLabel(status: string): string {
    return status === 'kept' ? 'Se mantiene' : status === 'new' ? 'Nuevo en este bloque' : 'Sale en este bloque';
  }

  historyBadgeStyle(status: string): string {
    const colors: Record<string, string> = { kept: 'background:#e2e8f0;color:#334155', new: 'background:#ccfbf1;color:#115e59', dropped: 'background:#fee2e2;color:#991b1b' };
    return 'display:inline-block;padding:2px 8px;border-radius:999px;font-size:.76rem;margin:2px 4px 2px 0;' + (colors[status] ?? colors['kept']);
  }

  historyExercises(program: GeneratedProgram, status: string): Array<{ exerciseId: string; name: string; status: string }> {
    return (program.history?.exercises ?? []).filter(item => item.status === status);
  }

  loadAdaptationProposals(): void {
    this.http.get<TrainingAdaptationProposal[]>('/api/training/clients/' + this.clientId + '/adaptation-proposals')
      .pipe(catchError(() => of([] as TrainingAdaptationProposal[])))
      .subscribe(items => {
        this.adaptationProposals = Array.isArray(items) ? items
          .filter((item): item is TrainingAdaptationProposal => !!item && typeof item === 'object' && Number.isInteger(item.id))
          .map(item => ({ ...item, action: typeof item.action === 'string' ? item.action : '', sessionTitle: typeof item.sessionTitle === 'string' ? item.sessionTitle : 'Sesión sin título', explanation: typeof item.explanation === 'string' ? item.explanation : '' })) : [];
        for (const item of this.adaptationProposals) this.adaptationReviewNotes[item.id] = this.adaptationReviewNotes[item.id] ?? '';
        this.changeDetector.markForCheck();
      });
  }

  adaptationActionLabel(action: string): string {
    const labels: Record<string, string> = {
      reorganize_future_sessions: 'Reorganizar sesiones futuras',
      hold_progression_and_review: 'Pausar progresión y revisar recuperación',
      review_and_reduce_training_stress: 'Revisar y reducir carga de entrenamiento',
      progress_according_to_prescription: 'Revisar progresión prescrita',
      pause_affected_exercise_and_review: 'Dolor registrado: revisión prioritaria',
      restore_volume: 'Recuperar una serie reducida (sin fatiga reciente)',
      propose_deload: 'Valorar una semana de descarga (señales de estrés combinadas)'
    };
    return labels[action] ?? 'Revisión de adaptación';
  }

  isAdaptationExerciseSelected(proposalId: number, exerciseId: string): boolean {
    return (this.adaptationTargetExercises[proposalId] ?? []).includes(exerciseId);
  }

  toggleAdaptationExercise(proposalId: number, exerciseId: string, checked: boolean): void {
    const selected = new Set(this.adaptationTargetExercises[proposalId] ?? []);
    if (checked) selected.add(exerciseId); else selected.delete(exerciseId);
    this.adaptationTargetExercises[proposalId] = [...selected];
  }

  reviewAdaptationProposal(proposal: TrainingAdaptationProposal, decision: 'approved' | 'rejected'): void {
    const verb = decision === 'approved' ? 'aprobar' : 'rechazar';
    const selectedExercises = this.adaptationTargetExercises[proposal.id] ?? [];
    const warning = proposal.action === 'review_and_reduce_training_stress'
      ? (selectedExercises.length
          ? ' Solo se reducirá una serie en los ejercicios seleccionados, con señales repetidas de fatiga.'
          : ' No se reducirá volumen hasta que selecciones ejercicios con señales específicas y repetidas.')
      : ' Solo se podrán ajustar sesiones futuras que aún no tengan feedback.';
    if (!confirm('¿Quieres ' + verb + ' esta propuesta?' + warning)) return;
    this.saving = true;
    this.error = '';
    this.http.post<{ message?: string; appliedFutureSessions?: number }>('/api/training/adaptation-proposals/' + proposal.id + '/review', {
      decision, notes: this.adaptationReviewNotes[proposal.id] ?? '', targetExerciseIds: selectedExercises
    }).subscribe({
      next: result => {
        this.notice = result.message ?? 'Revisión guardada.';
        this.saving = false;
        this.loadAdaptationProposals();
        this.load();
      },
      error: e => {
        this.error = e?.error?.message || 'No se pudo guardar la revisión de la adaptación.';
        this.saving = false;
      }
    });
  }

  private loadSessions(plans: TrainingPlan[], loadVersion: number): void {
    if (loadVersion !== this.loadVersion) return;
    if (!plans.length) { this.changeDetector.detectChanges(); return; }
    for (const plan of plans) {
      this.mesocycleDrafts[plan.id] = this.mesocycleDrafts[plan.id] ?? { name: '', sequence: 1, startDate: plan.startDate, endDate: plan.endDate ?? '', objective: '' };
      this.http.get<MacrocyclePlan | null>(`/api/training/plans/${plan.id}/macrocycle`).pipe(catchError(() => of(null as MacrocyclePlan | null))).subscribe(macrocycle => {
        if (loadVersion !== this.loadVersion) return;
        this.macrocyclePlans[plan.id] = macrocycle && Array.isArray(macrocycle.phases)
          ? { ...macrocycle, phases: macrocycle.phases.filter(phase => !!phase && Number.isInteger(phase.id)).map(phase => ({ ...phase, phaseType: phase.phaseType || 'general' })) }
          : null;
        if (!this.selectedMacroPhaseId[plan.id] || !this.macrocyclePhases(plan).some(phase => phase.id === this.selectedMacroPhaseId[plan.id]))
          this.selectedMacroPhaseId[plan.id] = this.macrocyclePhases(plan)[0]?.id || 0;
        this.changeDetector.detectChanges();
      });
      this.http.get<TrainingMesocycle[]>(`/api/training/plans/${plan.id}/mesocycles`).pipe(catchError(() => of([] as TrainingMesocycle[]))).subscribe(items => {
        if (loadVersion !== this.loadVersion) return;
        const safeMesocycles = Array.isArray(items) ? items
          .filter((item): item is TrainingMesocycle => !!item && typeof item === 'object' && Number.isInteger(item.id))
          .map(item => ({ ...item, name: typeof item.name === 'string' && item.name.trim() ? item.name : 'Bloque sin nombre', startDate: typeof item.startDate === 'string' ? item.startDate : plan.startDate })) : [];
        this.mesocycles[plan.id] = safeMesocycles;
        this.changeDetector.detectChanges();
        for (const meso of safeMesocycles) {
          this.microcycleDrafts[meso.id] = this.microcycleDrafts[meso.id] ?? { name: '', sequence: 1, startDate: meso.startDate, endDate: meso.endDate ?? '', focus: '' };
          this.http.get<TrainingMicrocycle[]>(`/api/training/mesocycles/${meso.id}/microcycles`).pipe(catchError(() => of([] as TrainingMicrocycle[]))).subscribe(rows => {
            if (loadVersion !== this.loadVersion) return;
            this.microcycles[meso.id] = Array.isArray(rows) ? rows
              .filter((item): item is TrainingMicrocycle => !!item && typeof item === 'object' && Number.isInteger(item.id))
              .map(item => ({ ...item, name: typeof item.name === 'string' && item.name.trim() ? item.name : 'Semana sin nombre', startDate: typeof item.startDate === 'string' ? item.startDate : meso.startDate })) : [];
            this.changeDetector.detectChanges();
          });
        }
      });
    }
    forkJoin(plans.map(p => this.http.get<TrainingSession[]>(`/api/training/plans/${p.id}/sessions`).pipe(catchError(() => of([] as TrainingSession[]))))).subscribe(items => {
      if (loadVersion !== this.loadVersion) return;
      plans.forEach((p, i) => this.sessions[p.id] = Array.isArray(items[i]) ? items[i]
        .filter((item): item is TrainingSession => !!item && typeof item === 'object' && Number.isInteger(item.id))
        .map(item => ({ ...item, title: typeof item.title === 'string' && item.title.trim() ? item.title : 'Sesión sin título' })) : []);
      this.changeDetector.detectChanges();
      for (const plan of plans) {
        this.loadPlanSplitPreference(plan);
        this.http.get<TrainingLoadWeek[]>(`/api/training/plans/${plan.id}/load-trend`)
          .pipe(catchError(() => of([] as TrainingLoadWeek[])))
          .subscribe(weeks => {
            if (loadVersion !== this.loadVersion) return;
            this.loadTrends[plan.id] = Array.isArray(weeks) ? weeks
              .filter((item): item is TrainingLoadWeek => !!item && typeof item === 'object') : [];
            this.changeDetector.detectChanges();
          });
      }
    });
  }

  activate(): void {
    this.profile = { id: 0, clientId: this.clientId, enabled: true, primaryGoal: 'general_fitness', experienceLevel: 'beginner', managementMode: 'self', notes: '' };
    this.saveProfile();
  }

  toggleEnabled(): void { if (!this.profile) return; this.trainingManagementMode = this.profile.enabled ? 'none' : 'self'; this.profile.enabled = this.trainingManagementMode !== 'none'; this.saveProfile(); }

  saveProfile(): void {
    if (!this.profile) return;
    this.saving = true; this.error = '';
    const payload = { ...this.profile, enabled: this.trainingManagementMode !== 'none', managementMode: this.trainingManagementMode };
    this.http.put<TrainingProfile>(`/api/training/clients/${this.clientId}/profile`, payload).subscribe({
      next: p => { this.trainingManagementMode = p.managementMode ?? this.trainingManagementMode; this.profile = p; this.saving = false; this.load(); if (this.trainingManagementMode === 'collaboration') this.loadCollaborations(); },
      error: e => { this.error = e?.error?.message || 'No se pudo guardar el perfil deportivo.'; this.saving = false; }
    });
  }

  selectMacroPhase(planId: number, phaseId: number): void {
    this.selectedMacroPhaseId[planId] = phaseId;
    this.changeDetector.detectChanges();
  }

  openPlannerWizard(): void {
    this.showPlannerWizard = true;
    this.showCreatePlanForm = false;
    this.showQuickRoutineForm = false;
  }

  /** Cerrar el asistente con la × también deja datos en el servidor (plan, mesociclos, sesiones): hay que refrescar la vista. */
  onPlannerClosed(): void {
    this.showPlannerWizard = false;
    this.load();
  }

  onPlannerCompleted(planId: number): void {
    this.showPlannerWizard = false;
    this.activePlanWorkspace[planId] = 'mesocycles';
    this.notice = 'Planificación creada como borrador. No se ha enviado ninguna notificación al cliente.';
    this.load();
  }

  setPlanWorkspace(planId: number, workspace: 'overview' | 'macrocycle' | 'mesocycles' | 'routines'): void {
    this.activePlanWorkspace[planId] = workspace;
    this.changeDetector.detectChanges();
  }

  macrocyclePhases(plan: TrainingPlan): MacrocyclePhase[] {
    const stored = this.macrocyclePlans[plan.id]?.phases;
    if (stored?.length) return [...stored].sort((a, b) => a.sequenceNumber - b.sequenceNumber || a.startDate.localeCompare(b.startDate));
    return (this.mesocycles[plan.id] || []).map(phase => ({
      id: phase.id, name: phase.name, sequenceNumber: phase.sequenceNumber, startDate: phase.startDate,
      endDate: phase.endDate || phase.startDate, objective: phase.objective, phaseType: this.macroPhaseKind(phase)
    }));
  }

  isMacroPhaseSelected(planId: number, phase: MacrocyclePhase): boolean {
    const phases = this.macrocyclePhases(this.plans.find(item => item.id === planId) || ({ id: planId } as TrainingPlan));
    const selectedId = this.selectedMacroPhaseId[planId];
    if (selectedId && phases.some(item => item.id === selectedId)) return selectedId === phase.id;
    return phases[0]?.id === phase.id;
  }

  macroPhaseKind(phase: TrainingMesocycle | MacrocyclePhase): MacrocyclePhase['phaseType'] {
    if ('phaseType' in phase && phase.phaseType) return phase.phaseType;
    const label = (phase.name + ' ' + (phase.objective || '')).toLocaleLowerCase('es');
    if (/descarga|recuperaci[oó]n|deload|descanso/.test(label)) return 'deload';
    if (/realizaci[oó]n|puesta a punto|competici[oó]n|peaking|pico/.test(label)) return 'realization';
    if (/intensificaci[oó]n|fuerza m[aá]xima|intensidad/.test(label)) return 'intensification';
    if (/acumulaci[oó]n|base|volumen|hipertrofia/.test(label)) return 'accumulation';
    return 'general';
  }

  macroPhaseDurationWeeks(phase: TrainingMesocycle | MacrocyclePhase): number {
    if (!phase.startDate || !phase.endDate) return 1;
    const start = Date.parse(phase.startDate.slice(0, 10) + 'T00:00:00Z');
    const end = Date.parse(phase.endDate.slice(0, 10) + 'T00:00:00Z');
    if (!Number.isFinite(start) || !Number.isFinite(end) || end < start) return 1;
    return Math.max(1, Math.ceil((end - start + 86400000) / (7 * 86400000)));
  }

  private memoStore: Record<string, { deps: unknown[]; value: unknown }> = {};
  /** Cachea cálculos derivados mientras no cambien las colecciones (que siempre se reemplazan, nunca se mutan). */
  private memo<T>(key: string, deps: unknown[], compute: () => T): T {
    const hit = this.memoStore[key];
    if (hit && hit.deps.length === deps.length && hit.deps.every((dep, index) => dep === deps[index])) return hit.value as T;
    const value = compute();
    this.memoStore[key] = { deps, value };
    return value;
  }

  macroPhaseWeeks(plan: TrainingPlan, phase: TrainingMesocycle | MacrocyclePhase): MacroPhaseLoadWeek[] {
    return this.memo(`weeks:${plan.id}:${phase.id}:${phase.startDate}:${phase.endDate}:${this.macroPhaseKind(phase)}`,
      [this.sessions[plan.id]], () => this.computeMacroPhaseWeeks(plan, phase));
  }

  private computeMacroPhaseWeeks(plan: TrainingPlan, phase: TrainingMesocycle | MacrocyclePhase): MacroPhaseLoadWeek[] {
    const weeks = this.macroPhaseDurationWeeks(phase);
    const phaseStart = phase.startDate.slice(0, 10);
    const phaseEnd = (phase.endDate || phase.startDate).slice(0, 10);
    const allSessions = (this.sessions[plan.id] || []).filter(session => {
      const date = (session.scheduledDate || '').slice(0, 10);
      return date >= phaseStart && date <= phaseEnd;
    });
    const rows = Array.from({ length: weeks }, (_, index) => {
      const startDate = this.addDays(phaseStart, index * 7);
      const calculatedEnd = this.addDays(startDate, 6);
      const endDate = calculatedEnd > phaseEnd ? phaseEnd : calculatedEnd;
      const sessions = allSessions.filter(session => {
        const date = (session.scheduledDate || '').slice(0, 10);
        return date >= startDate && date <= endDate;
      });
      const plannedMinutes = sessions.reduce((sum, session) => sum + Math.max(0, session.durationMinutes || 0), 0);
      let sets = 0;
      let hasPrescription = false;
      const rpes: number[] = [];
      for (const session of sessions) {
        const prescription = session.exercisePrescription || '';
        const data = session.exercisePrescriptionData;
        let structuredSets: number | null = null;
        if (data && typeof data === 'object') {
          const raw = JSON.stringify(data);
          const dataMatches = [...raw.matchAll(/"sets"\s*:\s*(\d+)/g)];
          if (dataMatches.length) structuredSets = dataMatches.reduce((total, match) => total + Number(match[1] || 0), 0);
        }
        if (structuredSets !== null) {
          sets += structuredSets;
          hasPrescription = true;
        } else if (prescription.trim()) {
          const matches = [...prescription.matchAll(/(?:^|[·\n])\s*(\d{1,2})\s*[x×]\s*\d/gim)];
          if (matches.length) {
            sets += matches.reduce((total, match) => total + Number(match[1] || 0), 0);
            hasPrescription = true;
          }
        }
        const rpeMatch = (session.intensityTarget || '').match(/(?:RPE|esfuerzo)\s*[:=]?\s*(\d+(?:[.,]\d+)?)/i);
        if (rpeMatch) {
          const rpe = Number(rpeMatch[1].replace(',', '.'));
          if (rpe >= 0 && rpe <= 10) rpes.push(rpe);
        }
      }
      return { weekNumber: index + 1, startDate, endDate, sessions: sessions.length,
        completedSessions: sessions.filter(session => !!session.completedAt || /completed|completed|realizada/i.test(session.status || '')).length,
        plannedMinutes, sets: hasPrescription ? sets : null,
        intensity: rpes.length ? rpes.reduce((sum, value) => sum + value, 0) / rpes.length : null,
        isDeload: this.macroPhaseKind(phase) === 'deload' };
    });
    const maxSets = Math.max(1, ...rows.map(row => row.sets ?? 0));
    const maxSessions = Math.max(1, ...rows.map(row => row.sessions));
    return rows.map(row => ({
      weekNumber: row.weekNumber, startDate: row.startDate, endDate: row.endDate, sessions: row.sessions,
      completedSessions: row.completedSessions, plannedMinutes: row.plannedMinutes, sets: row.sets,
      intensityLabel: row.intensity === null ? 'N/D' : 'RPE ' + (Math.round(row.intensity * 10) / 10).toLocaleString('es-ES'),
      intensityPct: row.intensity === null ? 0 : Math.round(row.intensity / 10 * 100),
      volumePct: row.sets === null ? 0 : Math.round(row.sets / maxSets * 100),
      frequencyPct: Math.round(row.sessions / maxSessions * 100), isDeload: row.isDeload
    }));
  }

  nextMesocycleStart(plan: TrainingPlan): string {
    const candidates = [plan.startDate];
    for (const mesocycle of this.mesocycles[plan.id] || []) {
      if (mesocycle.endDate) candidates.push(this.addUtcDays(mesocycle.endDate, 1));
      else candidates.push(mesocycle.startDate);
    }
    for (const session of this.sessions[plan.id] || []) {
      candidates.push(this.addUtcDays(session.scheduledDate, 1));
    }
    const sorted = candidates.filter(Boolean).sort();
    return sorted[sorted.length - 1] || plan.startDate;
  }

  private addUtcDays(dateValue: string, days: number): string {
    const date = new Date(`${dateValue.slice(0, 10)}T00:00:00Z`);
    date.setUTCDate(date.getUTCDate() + days);
    return date.toISOString().slice(0, 10);
  }

  private isSplitDayCountValidFor(split: number, dayCount: number): boolean {
    return split === 0 ? dayCount >= 2 && dayCount <= 4 :
      split === 1 ? dayCount >= 2 && dayCount <= 4 :
      split === 2 ? dayCount === 2 || dayCount === 4 :
      split === 3 ? dayCount === 3 || dayCount === 5 || dayCount === 6 : false;
  }

  private isSplitDayCountValid(dayCount: number): boolean {
    return this.isSplitDayCountValidFor(this.autoSplit, dayCount);
  }

  syncAutoTrainingDays(): void {
    this.autoTrainingDaysCsv = this.autoTrainingDayOptions
      .filter(day => this.autoTrainingDaySelection[day.value])
      .map(day => day.value)
      .join(',');
  }

  preparePhaseRoutineBlock(plan: TrainingPlan, phase: MacrocyclePhase): void {
    const existing = (this.mesocycles[plan.id] || []).find(item =>
      item.startDate.slice(0, 10) === phase.startDate.slice(0, 10) &&
      (item.endDate || item.startDate).slice(0, 10) === phase.endDate.slice(0, 10));
    if (existing) {
      const hasMicrocycles = (this.microcycles[existing.id] || []).length > 0;
      const hasSessions = (this.sessions[plan.id] || []).some(session =>
        session.scheduledDate.slice(0, 10) >= phase.startDate.slice(0, 10) &&
        session.scheduledDate.slice(0, 10) <= phase.endDate.slice(0, 10));
      if (hasMicrocycles || hasSessions) {
        this.notice = 'Ya existe un bloque para esta fase y contiene planificación o sesiones. Se conserva intacto; no se generarán rutinas encima de ese trabajo.';
        this.error = '';
        return;
      }
      this.autoTargetMesocycleIds[plan.id] = existing.id;
      this.selectRoutineTarget(plan, existing.id);
      this.notice = 'Ya existe un mesociclo vacío para esta fase. Está seleccionado como destino de las rutinas.';
      this.error = '';
      return;
    }
    const overlap = (this.mesocycles[plan.id] || []).find(item =>
      phase.startDate <= (item.endDate || item.startDate).slice(0, 10) &&
      phase.endDate >= item.startDate.slice(0, 10));
    if (overlap) {
      this.error = 'La fase se solapa con el mesociclo «' + overlap.name + '». Ajusta las fechas del bloque existente antes de preparar rutinas para esta fase.';
      return;
    }
    const sequenceNumber = Math.max(0, ...(this.mesocycles[plan.id] || []).map(item => item.sequenceNumber)) + 1;
    this.saving = true;
    this.error = '';
    this.http.post<{ id: number }>(`/api/training/plans/${plan.id}/mesocycles`, {
      name: phase.name, sequenceNumber, startDate: phase.startDate, endDate: phase.endDate,
      objective: phase.objective || null, notes: 'Bloque creado desde la planificación del macrociclo. Las rutinas se generan por separado.'
    }).subscribe({
      next: result => {
        this.autoTargetMesocycleIds[plan.id] = result.id;
        this.autoMesoNames[plan.id] = phase.name;
        this.autoStartDates[plan.id] = phase.startDate;
        const days = Math.floor((Date.parse(phase.endDate + 'T00:00:00Z') - Date.parse(phase.startDate + 'T00:00:00Z')) / 86400000) + 1;
        this.autoDurationWeeks = Math.max(1, Math.min(52, Math.floor(days / 7)));
        this.saving = false;
        this.notice = 'Bloque de rutinas preparado para «' + phase.name + '». Ya puedes generar la rutina en ese mesociclo sin crear otro bloque.';
        this.load();
      },
      error: e => {
        this.error = e?.error?.message || 'No se pudo preparar el bloque de rutinas para esta fase.';
        this.saving = false;
      }
    });
  }

  selectRoutineTarget(plan: TrainingPlan, mesocycleId: number | null): void {
    this.autoTargetMesocycleIds[plan.id] = mesocycleId;
    if (mesocycleId === null) {
      delete this.autoStartDates[plan.id];
      return;
    }
    const target = (this.mesocycles[plan.id] || []).find(item => item.id === mesocycleId);
    if (!target) return;
    this.autoMesoNames[plan.id] = target.name;
    this.autoStartDates[plan.id] = target.startDate.slice(0, 10);
    const targetEnd = (target.endDate || target.startDate).slice(0, 10);
    const availableDays = Math.floor((Date.parse(targetEnd + 'T00:00:00Z') - Date.parse(target.startDate.slice(0, 10) + 'T00:00:00Z')) / 86400000) + 1;
    this.autoDurationWeeks = Math.max(1, Math.min(52, Math.floor(availableDays / 7)));
  }

  previewGeneratedMesocycle(plan: TrainingPlan): void {
    const days = [...new Set(this.autoTrainingDaysCsv.split(',').map(value => Number(value.trim())).filter(value => Number.isInteger(value) && value >= 0 && value <= 6))];
    if (days.length < 2 || days.length > 6) {
      this.generationErrors[plan.id] = 'Selecciona entre 2 y 6 días de entrenamiento.';
      return;
    }
    if (!this.isSplitDayCountValid(days.length)) {
      this.generationErrors[plan.id] = this.autoSplit === 1
        ? 'Cuerpo completo admite de 2 a 4 días por semana.'
        : this.autoSplit === 2
          ? 'Torso/Pierna admite 2 o 4 días por semana.'
          : this.autoSplit === 3
            ? 'Push/Pull/Legs admite 3, 5 o 6 días por semana.'
            : 'La distribución automática admite entre 2 y 4 días por semana.';
      return;
    }
    if (!Number.isInteger(this.autoDurationWeeks) || this.autoDurationWeeks < 1 || this.autoDurationWeeks > 52 ||
        !Number.isInteger(this.autoMaximumMinutes) || this.autoMaximumMinutes < 35 || this.autoMaximumMinutes > 180) {
      this.generationErrors[plan.id] = 'Revisa la duración del ciclo y el límite de minutos por sesión.';
      return;
    }
    if (this.autoIsDeload && this.autoDurationWeeks !== 1) {
      this.generationErrors[plan.id] = 'Una descarga activa debe generarse como un bloque de una semana. Divide el periodo si necesitas planificar fases más largas.';
      return;
    }
    const preferenceError = this.validateMuscleVolumePreferences();
    if (preferenceError) {
      this.generationErrors[plan.id] = preferenceError;
      return;
    }
    const requestedStart = (this.autoStartDates[plan.id] || this.nextMesocycleStart(plan)).slice(0, 10);
    const targetMesocycleId = this.autoTargetMesocycleIds[plan.id] ?? null;
    const targetMesocycle = targetMesocycleId === null ? null : (this.mesocycles[plan.id] || []).find(item => item.id === targetMesocycleId) || null;
    const requestedEnd = this.addDays(requestedStart, this.autoDurationWeeks * 7 - 1);
    if (targetMesocycle && (requestedStart < targetMesocycle.startDate.slice(0, 10) || requestedEnd > (targetMesocycle.endDate || targetMesocycle.startDate).slice(0, 10))) {
      this.generationErrors[plan.id] = 'Las semanas solicitadas no caben en el mesociclo seleccionado (' + targetMesocycle.startDate.slice(0, 10) + ' a ' + (targetMesocycle.endDate || targetMesocycle.startDate).slice(0, 10) + '). Ajusta la duración o las fechas del bloque.';
      return;
    }
    if (plan.endDate) {
      if (requestedStart < plan.startDate.slice(0, 10) || requestedEnd > plan.endDate.slice(0, 10)) {
        this.generationErrors[plan.id] = 'El bloque solicitado (' + requestedStart + ' a ' + requestedEnd + ') queda fuera de las fechas del plan (' + plan.startDate.slice(0, 10) + ' a ' + plan.endDate.slice(0, 10) + '). Ajusta el inicio o la duración, o amplía primero las fechas del plan.';
        return;
      }
    }
    const request: GenerationRequest = {
      goal: this.autoGoal,
      split: this.autoSplit, experience: this.autoExperience, startDate: requestedStart,
      durationWeeks: this.autoDurationWeeks, trainingDays: days,
      maximumSessionDurationMinutes: this.autoMaximumMinutes,
      availableEquipment: [...new Set(this.autoEquipmentCsv.split(',').map(value => value.trim()).filter(Boolean))],
      excludedExerciseIds: [],
      muscleVolumeTargets: this.buildMuscleVolumeTargets(),
      isDeload: this.autoIsDeload,
      rotationIndex: this.autoRotationIndex ?? undefined
    };
    this.generatingPlan = true;
    this.volumeDeficitAcknowledged[plan.id] = false;
    this.generationErrors[plan.id] = '';
    this.generationRequests[plan.id] = request;
    this.http.post<GeneratedProgram>('/api/training/generate-preview?planId=' + plan.id, request).pipe(
      timeout({ first: 30000 }),
      catchError(error => {
        this.generationErrors[plan.id] = error?.error?.errors?.join?.(' ') || error?.error?.message || 'No se pudo generar la vista previa. Comprueba que haya ejercicios revisados compatibles.';
        return of(null);
      })
    ).subscribe(result => {
      this.generatedPreviews[plan.id] = result;
      this.generatingPlan = false;
      this.changeDetector.detectChanges();
    });
  }

  muscleLabel(value: string): string {
    const labels: Record<string, string> = {
      Chest: 'Pecho', Lats: 'Dorsales', UpperBack: 'Espalda alta', Deltoids: 'Deltoides',
      Biceps: 'Bíceps', Triceps: 'Tríceps', Forearms: 'Antebrazos', Abdominals: 'Abdominales',
      Obliques: 'Oblicuos', ErectorSpinae: 'Erectores espinales', Glutes: 'Glúteos',
      Quadriceps: 'Cuádriceps', Hamstrings: 'Isquiotibiales', Calves: 'Gemelos',
      Adductors: 'Aductores', Abductors: 'Abductores', HipFlexors: 'Flexores de cadera',
      Serratus: 'Serrato', TibialisAnterior: 'Tibial anterior', RotatorCuff: 'Manguito rotador'
    };
    return labels[value] || value;
  }

  private validateMuscleVolumePreferences(): string {
    for (const item of this.autoMusclePreferences.filter(preference => preference.enabled)) {
      if (!Number.isInteger(item.priority) || item.priority < 0 || item.priority > 3)
        return 'La prioridad de ' + item.label + ' debe estar entre 0 y 3.';
      if (item.targetWeeklyPrimarySets !== null &&
          (!Number.isInteger(item.targetWeeklyPrimarySets) || item.targetWeeklyPrimarySets < 1 || item.targetWeeklyPrimarySets > 30))
        return 'El objetivo semanal de ' + item.label + ' debe ser un número entero entre 1 y 30.';
    }
    return '';
  }

  private buildMuscleVolumeTargets(): Array<{ muscleGroup: string; priority: number; targetWeeklyPrimarySets?: number | null }> {
    return this.autoMusclePreferences
      .filter(item => item.enabled)
      .map(item => ({
        muscleGroup: item.muscleGroup,
        priority: item.priority,
        targetWeeklyPrimarySets: item.targetWeeklyPrimarySets
      }));
  }

  saveGeneratedMesocycle(plan: TrainingPlan): void {
    const generation = this.generationRequests[plan.id];
    const preview = this.generatedPreviews[plan.id];
    if (!generation || !preview) return;
    if (this.hasVolumeDoseDeficit(preview.qualityReport?.warnings) && !this.volumeDeficitAcknowledged[plan.id]) {
      this.generationErrors[plan.id] = 'Revisa y confirma expresamente los déficits de volumen antes de guardar el mesociclo.';
      return;
    }
    const name = (this.autoMesoNames[plan.id] || ('Mesociclo ' + ((this.mesocycles[plan.id]?.length || 0) + 1))).trim();
    if (!name) { this.generationErrors[plan.id] = 'Indica un nombre para el mesociclo.'; return; }
    const sequenceNumber = Math.max(0, ...(this.mesocycles[plan.id] || []).map(item => item.sequenceNumber)) + 1;
    this.saving = true;
    this.generationErrors[plan.id] = '';
    const existingMesocycleId = this.autoTargetMesocycleIds[plan.id] ?? null;
    this.http.post<{ sessionsCreated: number; durationWeeks: number; requiresProfessionalReview: boolean; addedToExistingMesocycle?: boolean }>(
      `/api/training/plans/${plan.id}/generate-mesocycle`,
      { name, sequenceNumber, generation, existingMesocycleId, notes: this.hasVolumeDoseDeficit(preview.qualityReport?.warnings) ? 'Generado automáticamente; el profesional ha revisado y aceptado los déficits de volumen/cobertura de la vista previa. Requiere revisión antes de asignarlo al paciente.' : 'Generado automáticamente; requiere revisión profesional antes de asignarlo al paciente.' }
    ).subscribe({
      next: result => {
        this.notice = result.addedToExistingMesocycle
          ? `Rutinas añadidas al mesociclo existente: ${result.durationWeeks} semanas y ${result.sessionsCreated} sesiones. Revisa el borrador antes de asignarlo.`
          : `Mesociclo y rutinas creados: ${result.durationWeeks} semanas y ${result.sessionsCreated} sesiones. Revisa el borrador antes de asignarlo.`;
        this.generatedPreviews[plan.id] = null;
        this.generationRequests[plan.id] = null;
        this.saving = false;
        this.load();
      },
      error: error => {
        this.generationErrors[plan.id] = error?.error?.message || error?.error?.errors?.join?.(' ') || 'No se pudo guardar el mesociclo.';
        this.saving = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  discardGeneratedPreview(planId: number): void {
    this.generatedPreviews[planId] = null;
    this.generationRequests[planId] = null;
    this.volumeDeficitAcknowledged[planId] = false;
    this.generationErrors[planId] = '';
  }

  prescriptionExerciseCount(prescription: string | null | undefined): number {
    return this.prescriptionLines(prescription).length;
  }

  sessionVolume(generated: GeneratedProgram, session: GeneratedSession): { muscles: Array<{ muscleGroup: string; estimatedEffectiveSets: number }> } | null {
    return generated.qualityReport?.sessionVolumes?.find(item =>
      item.weekNumber === session.weekNumber && item.day === session.day && item.title === session.title) ?? null;
  }

  hasVolumeDoseDeficit(warnings: string[] | null | undefined): boolean {
    return (warnings ?? []).some(warning =>
      /alcanza .* series con estímulo efectivo frente al rango|no hay ningún ejercicio disponible con estímulo suficiente/i.test(warning));
  }

  dayName(day: number): string {
    return ['domingo', 'lunes', 'martes', 'miércoles', 'jueves', 'viernes', 'sábado'][day] || 'día no válido';
  }

  private localDateInputValue(): string {
    const date = new Date();
    const pad = (value: number) => String(value).padStart(2, '0');
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
  }

  viewPlan(plan: TrainingPlan): void {
    this.editingPlanId = null;
    this.planEditDraft = null;
    this.setPlanWorkspace(plan.id, 'overview');
  }

  beginEditPlan(plan: TrainingPlan): void {
    this.editingPlanId = plan.id;
    this.planEditDraft = {
      name: plan.name,
      periodizationModel: plan.periodizationModel,
      startDate: String(plan.startDate).slice(0, 10),
      endDate: plan.endDate ? String(plan.endDate).slice(0, 10) : '',
      notes: plan.notes ?? ''
    };
  }

  savePlan(plan: TrainingPlan): void {
    const draft = this.planEditDraft;
    if (!draft || this.editingPlanId !== plan.id || !draft.name.trim() || !draft.startDate) return;
    this.saving = true;
    this.error = '';
    this.http.put<TrainingPlan>(`/api/training/plans/${plan.id}`, {
      name: draft.name.trim(),
      periodizationModel: draft.periodizationModel,
      startDate: draft.startDate,
      endDate: draft.endDate || null,
      notes: draft.notes.trim() || null,
      version: plan.version
    }).subscribe({
      next: updated => {
        this.plans = this.plans.map(item => item.id === plan.id ? updated : item);
        this.editingPlanId = null;
        this.planEditDraft = null;
        this.saving = false;
        this.notice = 'Plan de entrenamiento actualizado.';
        this.changeDetector.detectChanges();
      },
      error: e => {
        this.error = e?.error?.message || 'No se pudo actualizar el plan.';
        this.saving = false;
      }
    });
  }

  deletePlan(plan: TrainingPlan): void {
    const sessionCount = this.sessions[plan.id]?.length || 0;
    const mesocycleCount = this.mesocycles[plan.id]?.length || 0;
    const warning = `¿Eliminar «${plan.name}»? Se eliminarán también ${sessionCount} sesiones, sus registros de realización, ${mesocycleCount} mesociclos y todos los microciclos, macrociclo y fases asociados. Esta operación no se puede deshacer.`;
    if (!confirm(warning) || this.saving) return;
    this.saving = true;
    this.error = '';
    this.http.delete(`/api/training/plans/${plan.id}`).subscribe({
      next: () => {
        this.plannerWizardState.clearIfPlanMatches(this.clientId, plan.id);
        this.plans = this.plans.filter(item => item.id !== plan.id);
        delete this.sessions[plan.id];
        delete this.mesocycles[plan.id];
        delete this.microcycles[plan.id];
        delete this.macrocyclePlans[plan.id];
        delete this.activePlanWorkspace[plan.id];
        this.editingPlanId = null;
        this.planEditDraft = null;
        this.saving = false;
        this.notice = 'Planificación eliminada.';
        this.changeDetector.detectChanges();
      },
      error: e => {
        this.error = e?.error?.message || 'No se pudo eliminar el plan.';
        this.saving = false;
      }
    });
  }

  createQuickRoutineFromTrainingPage(): void {
    if (!this.clientId || this.trainingManagementMode !== 'self' || !this.quickRoutineName.trim() ||
        !this.quickRoutineDate || this.quickRoutineDuration < 1 || this.saving) return;
    this.saving = true;
    this.error = '';
    this.http.post<{ planId: number }>(`/api/training/clients/${this.clientId}/quick-routine`, {
      name: this.quickRoutineName.trim(),
      scheduledDate: this.quickRoutineDate,
      sessionType: this.quickRoutineType,
      durationMinutes: this.quickRoutineDuration,
      intensityTarget: this.quickRoutineIntensity.trim() || null,
      instructions: this.quickRoutineInstructions.trim() || null,
      exercisePrescription: this.quickRoutineExercises.trim() || null
    }).subscribe({
      next: result => {
        this.showQuickRoutineForm = false;
        this.activePlanWorkspace[result.planId] = 'routines';
        this.quickRoutineName = 'Rutina sencilla';
        this.quickRoutineExercises = '';
        this.quickRoutineInstructions = '';
        this.saving = false;
        this.notice = 'Rutina creada y asignada al cliente.';
        this.changeDetector.detectChanges();
        this.load();
      },
      error: e => {
        this.error = e?.error?.message || 'No se pudo crear la rutina sencilla.';
        this.saving = false;
      }
    });
  }

  createPlan(): void {
    if (!this.planName.trim() || !this.startDate) return;
    this.saving = true; this.error = '';
    this.http.post<TrainingPlan>(`/api/training/clients/${this.clientId}/plans`, { name: this.planName, periodizationModel: this.periodizationModel, startDate: this.startDate, endDate: this.endDate || null, notes: this.planNotes }).subscribe({
      next: created => {
        this.showCreatePlanForm = false;
        this.planName = ''; this.endDate = ''; this.planNotes = '';
        this.activePlanWorkspace[created.id] = this.newPlanTarget;
        this.saving = false;
        this.load();
      },
      error: e => { this.error = e?.error?.message || 'No se pudo crear el plan.'; this.saving = false; }
    });
  }

  addExerciseToDraft(plan: TrainingPlan, exercise: typeof this.exerciseCatalog[number]): void {
    const draft = this.sessionDrafts[plan.id];
    if (!draft) return;
    const lines = draft.exercisePrescription.trim() ? draft.exercisePrescription.split(/\r?\n/).map(line => line.trim()).filter(Boolean) : [];
    if (lines.some(line => line.toLocaleLowerCase('es').startsWith(exercise.name.toLocaleLowerCase('es')))) {
      this.notice = 'Ese ejercicio ya está incluido en la sesión.';
      return;
    }
    draft.exercisePrescription = [...lines, this.buildExercisePrescription(exercise, this.sessionExerciseSets, this.sessionExerciseReps, this.sessionExerciseRest)].join('\n');
    draft.exercisePrescriptionData = null;
    this.notice = exercise.name + ' añadido a la sesión.';
    this.refreshManualVolumePreview(plan);
  }

  applySessionTemplate(plan: TrainingPlan): void {
    const saved = (this.sessions[plan.id] ?? []).find(item => item.id === this.templateSessionId);
    const draft = this.sessionDrafts[plan.id];
    if (!saved || !draft) return;
    draft.type = saved.sessionType;
    draft.duration = saved.durationMinutes ?? 60;
    draft.intensity = saved.intensityTarget ?? '';
    draft.instructions = saved.instructions ?? '';
    draft.exercisePrescription = saved.exercisePrescription ?? '';
    draft.exercisePrescriptionData = saved.exercisePrescriptionData ?? null;
    this.notice = 'Se ha copiado la estructura de «' + saved.title + '». Ajusta fecha y nombre antes de guardarla.';
    this.refreshManualVolumePreview(plan);
  }

  generateSessionDraft(plan: TrainingPlan): void {
    const draft = this.sessionDrafts[plan.id];
    if (!draft) return;
    draft.exercisePrescriptionData = null;
    const level = this.generatorLevel;
    const equipment = this.generatorEquipment;
    const available = this.exerciseCatalog.filter(item => equipment === 'mixed' || item.equipment === equipment);
    let selected: typeof this.exerciseCatalog[number][] = [];
    if (this.generatorGoal === 'endurance') {
      selected = available.filter(item => item.goal === 'endurance').slice(0, level === 'beginner' ? 2 : 3);
      draft.type = 'endurance';
      draft.intensity = level === 'beginner' ? 'RPE 3–5 · ritmo conversacional' : 'RPE 5–7 · ajustar al objetivo del plan';
    } else if (this.generatorGoal === 'mobility') {
      selected = available.filter(item => item.goal === 'mobility' || item.goal === 'core').slice(0, 4);
      draft.type = 'mobility';
      draft.intensity = 'RPE 2–4 · rango cómodo, sin dolor';
    } else {
      selected = available.filter(item => item.goal === 'strength' || item.goal === 'core')
        .slice(0, level === 'beginner' ? 4 : level === 'intermediate' ? 5 : 6);
      draft.type = 'strength';
      draft.intensity = level === 'beginner' ? 'RPE 5–6 · dejar 3–4 repeticiones en reserva' : level === 'intermediate' ? 'RPE 6–8 · dejar 2–3 repeticiones en reserva' : 'RPE 7–8 · autorregular y mantener técnica';
    }
    if (!selected.length) { this.error = 'No hay ejercicios compatibles con ese objetivo y material. Prueba «Variado» u otro filtro.'; return; }
    draft.exercisePrescription = selected.map(item => {
      if (this.generatorGoal === 'hypertrophy' && item.goal === 'strength') return item.name + ' · 3 × 8–12 · descanso 60–120 s';
      if (level === 'beginner' && item.goal === 'strength') return item.name + ' · 2 × 8–10 · descanso 90 s';
      if (level === 'advanced' && item.goal === 'strength') return item.name + ' · 4 × 6–10 · descanso 90–150 s';
      return item.prescription;
    }).join('\n');
    draft.instructions = 'Borrador para nivel ' + (level === 'beginner' ? 'principiante' : level === 'intermediate' ? 'intermedio' : 'avanzado') +
      '. Incluye calentamiento progresivo y vuelta a la calma. Ajusta ejercicios, volumen y esfuerzo al historial, disponibilidad y tolerancia del cliente.';
    if (!draft.duration) draft.duration = this.generatorGoal === 'endurance' ? 30 : 45;
    this.notice = 'Borrador de sesión preparado. Revísalo y personalízalo antes de guardarlo.';
    this.refreshManualVolumePreview(plan);
  }

  createSession(plan: TrainingPlan): void {
    const draft = this.sessionDrafts[plan.id];
    if (!draft?.title.trim() || !draft.date) return;
    this.saving = true; this.error = '';
    this.http.post(`/api/training/plans/${plan.id}/sessions`, { scheduledDate: draft.date, title: draft.title.trim(), sessionType: draft.type, durationMinutes: draft.duration, intensityTarget: draft.intensity.trim() || null, instructions: draft.instructions.trim() || null, exercisePrescription: draft.exercisePrescription.trim() || null,
      exercisePrescriptionData: this.buildStructuredPrescriptionData(draft.date, draft.duration, this.parseStructuredExerciseDrafts(draft.exercisePrescription)) }).subscribe({
      next: () => { draft.title = ''; draft.intensity = ''; draft.instructions = ''; draft.exercisePrescription = ''; this.saving = false; this.refreshSessions(plan.id); },
      error: e => { this.error = e?.error?.message || 'No se pudo crear la sesión.'; this.saving = false; }
    });
  }

  loadDiagnosticPlans(): void {
    if (this.diagnosticPlansLoading) return;
    this.diagnosticPlansLoading = true;
    this.http.get<typeof this.diagnosticPlans>('/api/training/diagnostics/plans').pipe(
      timeout({ first: 15000 }),
      catchError(error => {
        if (error?.status !== 403) this.error = error?.error?.message || 'No se pudo cargar el inventario global de entrenamientos.';
        return of(null);
      })
    ).subscribe(items => {
      this.diagnosticPlansLoading = false;
      if (items === null) { this.changeDetector.detectChanges(); return; }
      this.diagnosticAccess = true;
      this.diagnosticPlans = Array.isArray(items)
        ? items.filter(item => !!item && Number.isInteger(item.id) && Number.isInteger(item.clientId))
        : [];
      this.changeDetector.detectChanges();
    });
  }

  private defaultTrainingPdfPeriod(): { start: string; end: string } {
    const today = new Date();
    const start = new Date(today.getFullYear(), today.getMonth(), today.getDate());
    start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
    const end = new Date(start);
    end.setDate(end.getDate() + 27);
    const format = (value: Date) => value.getFullYear() + '-' + String(value.getMonth() + 1).padStart(2, '0') + '-' + String(value.getDate()).padStart(2, '0');
    return { start: format(start), end: format(end) };
  }

  exportTrainingPdf(plan: TrainingPlan): void {
    if (!this.clientId || this.trainingPdfExportingPlanId !== null) return;
    if (!this.trainingPdfStartDate || !this.trainingPdfEndDate || this.trainingPdfEndDate < this.trainingPdfStartDate) {
      this.error = 'Selecciona un periodo válido para el PDF de entrenamiento.';
      return;
    }
    this.trainingPdfExportingPlanId = plan.id;
    this.error = '';
    const params = new URLSearchParams({ planId: String(plan.id), startDate: this.trainingPdfStartDate, endDate: this.trainingPdfEndDate });
    this.http.get('/api/clients/' + this.clientId + '/diets/training-pdf?' + params.toString(), { responseType: 'blob' }).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'dietoexpress-entrenamiento-' + plan.id + '-' + this.trainingPdfStartDate + '-' + this.trainingPdfEndDate + '.pdf';
        link.click();
        URL.revokeObjectURL(url);
        this.trainingPdfExportingPlanId = null;
        this.notice = 'PDF de entrenamiento generado correctamente.';
        this.changeDetector.detectChanges();
      },
      error: () => {
        this.trainingPdfExportingPlanId = null;
        this.error = 'No se pudo generar el PDF de entrenamiento.';
        this.changeDetector.detectChanges();
      }
    });
  }

  exportTestPlan(plan: typeof this.diagnosticPlans[number]): void {
    if (this.diagnosticExportingPlanId !== null) return;
    this.diagnosticExportingPlanId = plan.id;
    this.error = '';
    this.http.get(`/api/training/diagnostics/plans/${plan.id}/test-plan.pdf`, { responseType: 'blob' }).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = `dietoexpress-plan-${plan.id}.pdf`;
        link.click();
        URL.revokeObjectURL(url);
        this.diagnosticExportingPlanId = null;
        this.notice = `PDF de la planificación #${plan.id} generado correctamente.`;
        this.changeDetector.detectChanges();
      },
      error: async e => {
        this.diagnosticExportingPlanId = null;
        let serverMessage = '';
        if (e?.error instanceof Blob) {
          try {
            const responseText = await e.error.text();
            if (responseText) {
              try {
                const payload = JSON.parse(responseText);
                serverMessage = payload?.message || payload?.detail || payload?.title || '';
              } catch { serverMessage = responseText.slice(0, 500); }
            }
          } catch { /* HTTP status remains available below. */ }
        } else {
          serverMessage = e?.error?.message || e?.error?.detail || e?.error?.title || '';
        }
        console.error('[TrainingDiagnostics] Plan PDF export failed', {
          planId: plan.id, status: e?.status, statusText: e?.statusText, message: serverMessage, url: e?.url
        });
        this.error = serverMessage || `No se pudo exportar el plan #${plan.id} (HTTP ${e?.status || 'desconocido'}).`;
        this.changeDetector.detectChanges();
      }
    });
  }

  deleteDiagnosticPlan(plan: typeof this.diagnosticPlans[number]): void {
    if (this.diagnosticDeletingPlanId !== null) return;
    const confirmed = confirm(
      `Vas a eliminar definitivamente la planificación «${plan.name}» (#${plan.id}) de ${plan.clientName}.\\n\\nIncluye ${plan.mesocycleCount} mesociclos, ${plan.sessionCount} sesiones y ${plan.recordCount} registros asociados. Esta acción no se puede deshacer.\\n\\n¿Eliminar esta planificación?`
    );
    if (!confirmed) return;
    this.diagnosticDeletingPlanId = plan.id;
    this.error = '';
    this.http.delete(`/api/training/plans/${plan.id}`).subscribe({
      next: () => {
        this.diagnosticPlans = this.diagnosticPlans.filter(item => item.id !== plan.id);
        this.diagnosticDeletingPlanId = null;
        this.notice = `Planificación #${plan.id} eliminada.`;
        this.changeDetector.detectChanges();
      },
      error: e => {
        this.diagnosticDeletingPlanId = null;
        this.error = e?.error?.message || `No se pudo eliminar la planificación #${plan.id} (HTTP ${e?.status || 'desconocido'}).`;
        this.changeDetector.detectChanges();
      }
    });
  }

  exportSessions(plan: TrainingPlan): void {
    const rows = this.sessions[plan.id] ?? [];
    if (!rows.length) return;
    const headers = ['Fecha', 'Sesión', 'Tipo', 'Minutos previstos', 'Zona objetivo', 'Instrucciones', 'Ejercicios y series', 'Realizada en', 'Minutos reales', 'RPE', 'Notas'];
    const cell = (value: unknown) => '"' + String(value ?? '').replace(/"/g, '""').replace(/\r?\n/g, ' ') + '"';
    const lines = [headers, ...rows.map(session => [
      session.scheduledDate, session.title, this.sessionTypeLabel(session.sessionType), session.durationMinutes,
      session.intensityTarget, session.instructions, session.exercisePrescription, session.completedAt,
      session.actualDurationMinutes, session.sessionRpe, session.recordNotes
    ])].map(row => row.map(cell).join(';'));
    const blob = new Blob(['\uFEFF' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = 'dietoexpress-entrenamiento-' + plan.id + '.csv';
    link.click();
    URL.revokeObjectURL(url);
  }

  trendBarPixels(planId: number, score: number): number {
    const maximum = Math.max(1, ...(this.loadTrends[planId] ?? []).map(item => item.loadScore));
    return score <= 0 ? 2 : Math.max(4, Math.round(score / maximum * 92));
  }

  setSessionView(planId: number, view: 'week' | 'list'): void {
    this.sessionViews[planId] = view;
  }

  private localDate(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  weekDays(plan: TrainingPlan): { date: string; isToday: boolean }[] {
    const [year, month, day] = plan.startDate.slice(0, 10).split('-').map(Number);
    const monday = new Date(year, month - 1, day);
    const weekday = (monday.getDay() + 6) % 7;
    monday.setDate(monday.getDate() - weekday + (this.weekOffsets[plan.id] ?? 0) * 7);
    const today = this.localDate(new Date());
    return Array.from({ length: 7 }, (_, index) => {
      const date = new Date(monday);
      date.setDate(monday.getDate() + index);
      const value = this.localDate(date);
      return { date: value, isToday: value === today };
    });
  }

  shiftWeek(plan: TrainingPlan, amount: number): void {
    this.weekOffsets[plan.id] = (this.weekOffsets[plan.id] ?? 0) + amount;
  }

  goToCurrentWeek(plan: TrainingPlan): void {
    const [year, month, day] = plan.startDate.slice(0, 10).split('-').map(Number);
    const start = new Date(year, month - 1, day);
    start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
    const today = new Date();
    today.setHours(0, 0, 0, 0);
    const monday = new Date(today);
    monday.setDate(today.getDate() - ((today.getDay() + 6) % 7));
    this.weekOffsets[plan.id] = Math.round((monday.getTime() - start.getTime()) / (7 * 86400000));
  }

  sessionsForDay(planId: number, date: string): TrainingSession[] {
    return (this.sessions[planId] ?? []).filter(session => session.scheduledDate.slice(0, 10) === date)
      .sort((a, b) => a.title.localeCompare(b.title, 'es'));
  }

  weekSessions(planId: number, plan: TrainingPlan): TrainingSession[] {
    const days = this.weekDays(plan);
    const first = days[0].date;
    const last = days[6].date;
    return (this.sessions[planId] ?? []).filter(session => {
      const date = session.scheduledDate.slice(0, 10);
      return date >= first && date <= last;
    });
  }

  private loadPlanSplitPreference(plan: TrainingPlan): void {
    this.http.get<{ splitType: number; trainingDays: number[] }>(`/api/training/plans/${plan.id}/split`)
      .pipe(catchError(() => of({ splitType: 0, trainingDays: [1, 3, 5] })))
      .subscribe(value => {
        const safeDays = Array.isArray(value.trainingDays) ? value.trainingDays.filter(day => Number.isInteger(day) && day >= 0 && day <= 6) : [1, 3, 5];
        this.splitPreferences[plan.id] = { splitType: value.splitType, trainingDays: safeDays };
        if (!this.splitReorganizationDrafts[plan.id]) this.splitReorganizationDrafts[plan.id] = { splitType: value.splitType, trainingDays: [...safeDays], moves: null, error: '' };
        this.changeDetector.markForCheck();
      });
  }

  toggleSplitReorganization(plan: TrainingPlan): void {
    this.splitReorganizationOpen[plan.id] = !this.splitReorganizationOpen[plan.id];
    if (!this.splitReorganizationDrafts[plan.id]) {
      const saved = this.splitPreferences[plan.id] || { splitType: 0, trainingDays: [1, 3, 5] };
      this.splitReorganizationDrafts[plan.id] = { splitType: saved.splitType, trainingDays: [...saved.trainingDays], moves: null, error: '' };
    }
  }

  invalidateSplitReplacement(plan: TrainingPlan): void {
    this.splitReplacementPreviews[plan.id] = null;
    this.splitReplacementErrors[plan.id] = '';
    this.splitVolumeDeficitAcknowledged[plan.id] = false;
  }

  toggleReorganizationDay(plan: TrainingPlan, day: number, checked: boolean): void {
    const draft = this.splitReorganizationDrafts[plan.id];
    draft.trainingDays = checked
      ? [...new Set([...draft.trainingDays, day])]
      : draft.trainingDays.filter(value => value !== day);
    draft.moves = null;
    draft.error = '';
    this.invalidateSplitReplacement(plan);
  }

  prepareSplitReorganization(plan: TrainingPlan): void {
    const draft = this.splitReorganizationDrafts[plan.id];
    const days = [...draft.trainingDays].sort((a, b) => ((a + 6) % 7) - ((b + 6) % 7));
    if (!this.isSplitDayCountValidFor(draft.splitType, days.length)) {
      draft.error = draft.splitType === 1 ? 'Cuerpo completo admite de 2 a 4 días.' :
        draft.splitType === 2 ? 'Torso/Pierna admite 2 o 4 días.' :
        draft.splitType === 3 ? 'Push/Pull/Legs admite 3, 5 o 6 días.' :
        'La distribución automática admite entre 2 y 4 días.';
      draft.moves = null;
      return;
    }
    const today = this.localDateInputValue();
    const nextMonday = this.addUtcDays(this.weekStartDate(today), 7);
    const allSessions = this.sessions[plan.id] || [];
    const completedDates = new Set(allSessions.filter(session => !!session.completedAt || session.status === 'completed')
      .map(session => session.scheduledDate.slice(0, 10)));
    const futureSessions = allSessions.filter(session =>
      !session.completedAt && session.status !== 'completed' && session.scheduledDate.slice(0, 10) >= nextMonday
    );
    if (futureSessions.some(session => this.weeklyEditorDirty[session.id]) ||
        (this.editingSessionId !== null && futureSessions.some(session => session.id === this.editingSessionId))) {
      draft.error = 'Hay cambios de sesión sin guardar. Guarda o descarta esos cambios antes de reorganizar el split.';
      draft.moves = null;
      return;
    }
    if (futureSessions.length) {
      const current = this.splitPreferences[plan.id] || { splitType: 0, trainingDays: [1, 3, 5] };
      if (draft.splitType !== current.splitType || days.length !== current.trainingDays.length) {
        draft.error = 'Esta acción solo recoloca los días y conserva el split y su frecuencia actuales. Para cambiar el tipo de split o el número de días, utiliza «Generar propuesta con este split».';
        draft.moves = null;
        return;
      }
    }
    const byWeek = new Map<string, TrainingSession[]>();
    for (const session of futureSessions) {
      const week = this.weekStartDate(session.scheduledDate);
      const rows = byWeek.get(week) || [];
      rows.push(session);
      byWeek.set(week, rows);
    }
    const moves: SplitReorganizationMove[] = [];
    for (const [week, rows] of [...byWeek.entries()].sort(([a], [b]) => a.localeCompare(b))) {
      if (rows.length !== days.length) {
        draft.error = 'La semana que comienza el ' + week + ' tiene ' + rows.length + ' sesiones pendientes, pero el split seleccionado requiere ' + days.length + '. No se han movido sesiones. Para cambiar la frecuencia, genera y revisa un nuevo bloque.';
        draft.moves = null;
        return;
      }
      rows.sort((a, b) => a.scheduledDate.localeCompare(b.scheduledDate) || a.id - b.id);
      rows.forEach((session, index) => {
        const dayOffset = days[index] === 0 ? 6 : days[index] - 1;
        const newDate = this.addUtcDays(week, dayOffset);
        if (completedDates.has(newDate)) {
          draft.error = 'La fecha ' + newDate + ' ya contiene una sesión completada. Se conserva esa sesión y no se aplicará una reorganización que la solape.';
          draft.moves = null;
          return;
        }
        if (newDate < nextMonday || newDate < plan.startDate.slice(0, 10) || (plan.endDate && newDate > plan.endDate.slice(0, 10))) {
          draft.error = 'La fecha propuesta para «' + session.title + '» queda fuera del periodo permitido. Ajusta los días o las fechas del plan.';
          draft.moves = null;
          return;
        }
        moves.push({ sessionId: session.id, title: session.title, oldDate: session.scheduledDate.slice(0, 10), scheduledDate: newDate });
      });
      if (draft.error) return;
    }
    draft.moves = moves;
    draft.error = '';
  }

  applySplitReorganization(plan: TrainingPlan): void {
    const draft = this.splitReorganizationDrafts[plan.id];
    if (!draft || draft.moves === null) return;
    const movedIds = draft.moves.map(move => move.sessionId);
    if (movedIds.some(id => this.weeklyEditorDirty[id]) || (this.editingSessionId !== null && movedIds.includes(this.editingSessionId))) {
      draft.error = 'Hay cambios de sesión sin guardar. Guarda o descarta esos cambios antes de aplicar la reorganización.';
      return;
    }
    this.splitReorganizationSaving[plan.id] = true;
    this.http.post<{ updatedSessions: number }>('/api/training/plans/' + plan.id + '/reorganize-split', {
      splitType: draft.splitType,
      trainingDays: draft.trainingDays,
      moves: draft.moves.map(move => ({ sessionId: move.sessionId, scheduledDate: move.scheduledDate }))
    }).subscribe({
      next: result => {
        this.splitPreferences[plan.id] = { splitType: draft.splitType, trainingDays: [...draft.trainingDays] };
        this.splitReorganizationSaving[plan.id] = false;
        movedIds.forEach(id => { delete this.weeklyEditorDrafts[id]; delete this.weeklyEditorDirty[id]; });
        if (this.editingSessionId !== null && movedIds.includes(this.editingSessionId)) {
          this.editingSessionId = null;
          this.sessionEditDraft = null;
        }
        draft.moves = null;
        draft.error = '';
        this.notice = 'Distribución guardada. Se han reorganizado ' + result.updatedSessions + ' sesiones futuras sin modificar sus prescripciones ni los registros completados.';
        this.refreshSessions(plan.id);
        this.changeDetector.detectChanges();
      },
      error: error => {
        this.splitReorganizationSaving[plan.id] = false;
        draft.error = error?.error?.message || 'No se pudo reorganizar la semana. No se ha aplicado ningún cambio.';
        this.changeDetector.detectChanges();
      }
    });
  }

  generateSplitReplacementPreview(plan: TrainingPlan): void {
    const draft = this.splitReorganizationDrafts[plan.id];
    const days = [...draft.trainingDays].sort((a, b) => ((a + 6) % 7) - ((b + 6) % 7));
    if (!this.isSplitDayCountValidFor(draft.splitType, days.length)) {
      this.splitReplacementErrors[plan.id] = 'Selecciona días compatibles con el split elegido.';
      return;
    }
    if (!Number.isInteger(this.autoMaximumMinutes) || this.autoMaximumMinutes < 35 || this.autoMaximumMinutes > 180) {
      this.splitReplacementErrors[plan.id] = 'La duración máxima debe estar entre 35 y 180 minutos.';
      return;
    }
    const firstAvailableMonday = this.addUtcDays(this.weekStartDate(this.localDateInputValue()), 7);
    const planStart = plan.startDate.slice(0, 10);
    const planStartWeek = this.weekStartDate(planStart);
    // Do not propose sessions before the plan starts. If it starts mid-week, leave
    // that partial week untouched and begin replacement on the next full Monday.
    const firstFullPlanWeek = planStart === planStartWeek
      ? planStartWeek
      : this.addUtcDays(planStartWeek, 7);
    const nextMonday = firstAvailableMonday > firstFullPlanWeek
      ? firstAvailableMonday
      : firstFullPlanWeek;
    const futureSessions = (this.sessions[plan.id] || []).filter(session =>
      session.scheduledDate.slice(0, 10) >= nextMonday && !session.completedAt && session.status !== 'completed');
    if (!futureSessions.length) {
      this.splitReplacementErrors[plan.id] = 'No hay sesiones futuras pendientes que permitan determinar el periodo que se va a reorganizar.';
      return;
    }
    if (futureSessions.some(session => this.weeklyEditorDirty[session.id]) ||
        (this.editingSessionId !== null && futureSessions.some(session => session.id === this.editingSessionId))) {
      this.splitReplacementErrors[plan.id] = 'Hay cambios de sesión sin guardar. Guarda o descarta esos cambios antes de generar otra distribución.';
      return;
    }
    const lastDate = futureSessions.reduce((latest, session) =>
      session.scheduledDate.slice(0, 10) > latest ? session.scheduledDate.slice(0, 10) : latest, nextMonday);
    const lastWeek = this.weekStartDate(lastDate);
    const durationWeeks = Math.floor((Date.parse(lastWeek + 'T00:00:00Z') - Date.parse(nextMonday + 'T00:00:00Z')) / 604800000) + 1;
    if (durationWeeks < 1 || durationWeeks > 52) {
      this.splitReplacementErrors[plan.id] = 'El periodo futuro debe tener entre una y 52 semanas.';
      return;
    }
    const replaceThroughDate = this.addUtcDays(nextMonday, durationWeeks * 7 - 1);
    if (plan.endDate && replaceThroughDate > plan.endDate.slice(0, 10)) {
      this.splitReplacementErrors[plan.id] = 'El nuevo bloque acabaría después de la fecha final del plan. Amplía primero el periodo.';
      return;
    }
    const completedInRange = (this.sessions[plan.id] || []).some(session =>
      (session.completedAt || session.status === 'completed') &&
      session.scheduledDate.slice(0, 10) >= nextMonday &&
      session.scheduledDate.slice(0, 10) <= replaceThroughDate);
    if (completedInRange) {
      this.splitReplacementErrors[plan.id] = 'El periodo contiene una sesión completada. Acota el bloque para dejarla fuera; su historial no se sustituye.';
      return;
    }
    const deloadOverlap = this.macrocyclePhases(plan).some(phase =>
      phase.phaseType === 'deload' && phase.startDate.slice(0, 10) <= replaceThroughDate && phase.endDate.slice(0, 10) >= nextMonday);
    if (deloadOverlap) {
      this.splitReplacementErrors[plan.id] = 'El periodo incluye una fase de descarga. Reorganízala por separado para no convertirla accidentalmente en una semana de carga.';
      return;
    }

    const request: GenerationRequest = {
      goal: this.autoGoal, experience: this.autoExperience, startDate: nextMonday,
      durationWeeks, trainingDays: days, split: draft.splitType,
      maximumSessionDurationMinutes: this.autoMaximumMinutes,
      availableEquipment: [...new Set(this.autoEquipmentCsv.split(',').map(value => value.trim()).filter(Boolean))],
      excludedExerciseIds: [], muscleVolumeTargets: this.buildMuscleVolumeTargets(), isDeload: false
    };
    this.splitReplacementLoading[plan.id] = true;
    this.splitReplacementErrors[plan.id] = '';
    this.splitReplacementPreviews[plan.id] = null;
    this.splitVolumeDeficitAcknowledged[plan.id] = false;
    this.http.post<GeneratedProgram>('/api/training/generate-preview', request).pipe(
      timeout({ first: 30000 }),
      catchError(error => {
        this.splitReplacementErrors[plan.id] = error?.error?.errors?.join?.(' ') ||
          error?.error?.message || 'No se pudo generar la nueva distribución con el catálogo revisado.';
        return of(null);
      })
    ).subscribe(generated => {
      if (!generated) {
        this.splitReplacementLoading[plan.id] = false;
        this.changeDetector.detectChanges();
        return;
      }
      this.buildSplitReplacementPreview(plan, generated, futureSessions, nextMonday, replaceThroughDate, draft.splitType, days);
    });
  }

  private buildSplitReplacementPreview(
    plan: TrainingPlan,
    generated: GeneratedProgram,
    existingSessions: TrainingSession[],
    replaceFromDate: string,
    replaceThroughDate: string,
    splitType: number,
    trainingDays: number[]
  ): void {
    const oldByWeek = new Map<string, TrainingSession[]>();
    for (const session of existingSessions) {
      const week = this.weekStartDate(session.scheduledDate);
      const rows = oldByWeek.get(week) || [];
      rows.push(session);
      oldByWeek.set(week, rows);
    }
    const generatedByWeek = new Map<number, GeneratedSession[]>();
    for (const session of generated.sessions) {
      const rows = generatedByWeek.get(session.weekNumber) || [];
      rows.push(session);
      generatedByWeek.set(session.weekNumber, rows);
    }

    const replacementSessions: SplitReplacementSession[] = [];
    let preservedManualEdits = 0;
    let retainedCustomExercises = 0;
    const warnings: string[] = [...(generated.qualityReport?.warnings ?? [])];

    for (const [weekNumber, generatedWeek] of [...generatedByWeek.entries()].sort(([a], [b]) => a - b)) {
      generatedWeek.sort((a, b) => ((a.day + 6) % 7) - ((b.day + 6) % 7));
      const weekStart = this.addUtcDays(replaceFromDate, (weekNumber - 1) * 7);
      const oldWeek = (oldByWeek.get(weekStart) || []).slice().sort((a, b) =>
        a.scheduledDate.localeCompare(b.scheduledDate) || a.id - b.id);
      const manualRows = oldWeek.flatMap((session, sourceIndex) =>
        this.parseStructuredExerciseDrafts(session.exercisePrescription).map(row => ({ row, sourceIndex, consumed: false })));
      const rowsByGeneratedSession: StructuredExerciseDraft[][] = generatedWeek.map(() => []);

      generatedWeek.forEach((session, sessionIndex) => {
        for (const exercise of session.exercises) {
          const generatedName = exercise.exerciseNameSnapshot.trim();
          const generatedId = exercise.exerciseId.replace(/^repdb-/, '').toLocaleLowerCase('es');
          const manualIndex = manualRows.findIndex(item => {
            if (item.consumed) return false;
            const catalogItem = this.findUniqueCatalogExerciseByName(item.row.exerciseName);
            const manualId = (item.row.exerciseId ?? catalogItem?.id)?.replace(/^repdb-/, '').toLocaleLowerCase('es');
            return item.row.exerciseId
              ? manualId === generatedId
              : !!catalogItem && item.row.exerciseName.trim().toLocaleLowerCase('es') === generatedName.toLocaleLowerCase('es');
          });
          if (manualIndex >= 0) {
            const manual = manualRows[manualIndex];
            manual.consumed = true;
            const generatedReps = exercise.minRepetitions === exercise.maxRepetitions
              ? String(exercise.minRepetitions)
              : exercise.minRepetitions + '–' + exercise.maxRepetitions;
            const differs = manual.row.sets !== exercise.sets ||
              manual.row.repetitions !== generatedReps ||
              manual.row.rir !== exercise.targetRepetitionsInReserve ||
              manual.row.restSeconds !== exercise.restSeconds;
            if (differs) preservedManualEdits++;
            rowsByGeneratedSession[sessionIndex].push({
              ...manual.row,
              exerciseName: generatedName,
              exerciseId: exercise.exerciseId,
              catalogVersion: exercise.catalogVersion,
              progressionRule: exercise.progressionRule,
              primaryMuscleGroups: exercise.primaryMuscleGroups,
              secondaryMuscleGroups: exercise.secondaryMuscleGroups,
              smallestLoadIncrementKg: exercise.smallestLoadIncrementKg,
              maximumLoadIncreasePercent: exercise.maximumLoadIncreasePercent,
              selectionRationale: exercise.selectionRationale,
              warmupSets: exercise.warmupSets, warmupNote: exercise.warmupNote,
              targetLoadKg: exercise.targetLoadKg, loadBasis: exercise.loadBasis,
              adaptationOriginalSets: exercise.adaptationOriginalSets
            });
          } else {
            rowsByGeneratedSession[sessionIndex].push({
              exerciseName: generatedName, sets: exercise.sets,
              repetitions: exercise.minRepetitions === exercise.maxRepetitions
                ? String(exercise.minRepetitions)
                : exercise.minRepetitions + '–' + exercise.maxRepetitions,
              rir: exercise.targetRepetitionsInReserve ?? null,
              restSeconds: exercise.restSeconds, notes: '',
              exerciseId: exercise.exerciseId, catalogVersion: exercise.catalogVersion,
              progressionRule: exercise.progressionRule,
              primaryMuscleGroups: exercise.primaryMuscleGroups,
              secondaryMuscleGroups: exercise.secondaryMuscleGroups,
              smallestLoadIncrementKg: exercise.smallestLoadIncrementKg,
              maximumLoadIncreasePercent: exercise.maximumLoadIncreasePercent,
              selectionRationale: exercise.selectionRationale,
              warmupSets: exercise.warmupSets, warmupNote: exercise.warmupNote,
              targetLoadKg: exercise.targetLoadKg, loadBasis: exercise.loadBasis,
              adaptationOriginalSets: exercise.adaptationOriginalSets
            });
          }
        }
      });

      manualRows.forEach(item => {
        if (item.consumed) return;
        const targetIndex = item.sourceIndex % generatedWeek.length;
        rowsByGeneratedSession[targetIndex].push(item.row);
        retainedCustomExercises++;
      });

      generatedWeek.forEach((session, sessionIndex) => {
        const dayOffset = session.day === 0 ? 6 : session.day - 1;
        const scheduledDate = this.addUtcDays(weekStart, dayOffset);
        const sourceSession = oldWeek[sessionIndex] || oldWeek[0];
        const exerciseRows = rowsByGeneratedSession[sessionIndex];
        const prescription = this.serializeStructuredExerciseDrafts(exerciseRows);
        const duration = session.estimatedDurationMinutes;
        replacementSessions.push({
          scheduledDate,
          title: session.title,
          sessionType: 'strength',
          durationMinutes: duration,
          intensityTarget: sourceSession?.intensityTarget ?? null,
          instructions: sourceSession?.instructions ?? 'Nueva distribución generada. Revisión profesional pendiente.',
          exercisePrescription: prescription,
          exercisePrescriptionData: this.buildStructuredPrescriptionData(scheduledDate, duration, exerciseRows)
        });
      });
    }

    if (retainedCustomExercises > 0)
      warnings.push(retainedCustomExercises + ' ejercicios manuales no coincidentes se han conservado en la sesión equivalente por orden; comprueba que su ubicación y carga sigan teniendo sentido.');
    if (preservedManualEdits > 0)
      warnings.push('Se han conservado ' + preservedManualEdits + ' ajustes de series, repeticiones, RIR o descanso en ejercicios que aparecen también en la nueva propuesta. Comprueba la duración resultante.');
    if (retainedCustomExercises > 0)
      warnings.push('La duración estimada del generador no incluye el tiempo adicional de los ejercicios manuales conservados; revisa los límites de cada sesión.');
    const volumeSessions = replacementSessions.map(session => ({
      weekNumber: Math.floor((Date.parse(this.weekStartDate(session.scheduledDate) + 'T00:00:00Z') -
        Date.parse(replaceFromDate + 'T00:00:00Z')) / 604800000) + 1,
      day: new Date(session.scheduledDate + 'T00:00:00Z').getUTCDay(),
      title: session.title,
      exercises: this.parseManualVolumeExercises(session.exercisePrescription)
    }));

    this.http.post<ManualVolumePreview>('/api/training/volume-preview', { sessions: volumeSessions }).subscribe({
      next: volumePreview => {
        this.splitReplacementPreviews[plan.id] = {
          replaceFromDate, replaceThroughDate, sessions: replacementSessions, volumePreview,
          preservedManualEdits, retainedCustomExercises, warnings
        };
        this.splitReplacementLoading[plan.id] = false;
        this.changeDetector.detectChanges();
      },
      error: error => {
        this.splitReplacementErrors[plan.id] = error?.error?.message || 'La propuesta se generó, pero no se pudo calcular su volumen efectivo. No se puede aplicar sin esa comprobación.';
        this.splitReplacementLoading[plan.id] = false;
        this.changeDetector.detectChanges();
      }
    });
  }

  applySplitReplacement(plan: TrainingPlan): void {
    const draft = this.splitReorganizationDrafts[plan.id];
    const preview = this.splitReplacementPreviews[plan.id];
    if (!draft || !preview || !preview.sessions.length) return;
    if (this.hasVolumeDoseDeficit(preview.warnings) && !this.splitVolumeDeficitAcknowledged[plan.id]) {
      this.splitReplacementErrors[plan.id] = 'Revisa y confirma expresamente los déficits de volumen antes de sustituir las sesiones futuras.';
      return;
    }
    const replacedSessions = (this.sessions[plan.id] || []).filter(session =>
      session.scheduledDate.slice(0, 10) >= preview.replaceFromDate &&
      session.scheduledDate.slice(0, 10) <= preview.replaceThroughDate);
    if (replacedSessions.some(session => this.weeklyEditorDirty[session.id]) ||
        (this.editingSessionId !== null && replacedSessions.some(session => session.id === this.editingSessionId))) {
      this.splitReplacementErrors[plan.id] = 'Hay cambios de sesión sin guardar. Guarda o descarta esos cambios antes de sustituir el bloque.';
      return;
    }
    this.splitReplacementLoading[plan.id] = true;
    this.http.post<{ sessionsCreated: number }>('/api/training/plans/' + plan.id + '/replace-future-split', {
      replaceFromDate: preview.replaceFromDate,
      replaceThroughDate: preview.replaceThroughDate,
      splitType: draft.splitType,
      trainingDays: draft.trainingDays,
      sessions: preview.sessions
    }).subscribe({
      next: result => {
        this.splitPreferences[plan.id] = { splitType: draft.splitType, trainingDays: [...draft.trainingDays] };
        this.splitReplacementLoading[plan.id] = false;
        replacedSessions.forEach(session => { delete this.weeklyEditorDrafts[session.id]; delete this.weeklyEditorDirty[session.id]; });
        if (this.editingSessionId !== null && replacedSessions.some(session => session.id === this.editingSessionId)) {
          this.editingSessionId = null;
          this.sessionEditDraft = null;
        }
        this.splitReplacementPreviews[plan.id] = null;
        this.splitVolumeDeficitAcknowledged[plan.id] = false;
        this.notice = 'Nueva distribución aplicada: ' + result.sessionsCreated + ' sesiones. Se han conservado las sesiones completadas y los ajustes manuales coincidentes.';
        this.refreshSessions(plan.id);
        this.changeDetector.detectChanges();
      },
      error: error => {
        this.splitReplacementLoading[plan.id] = false;
        this.splitReplacementErrors[plan.id] = error?.error?.message || 'No se pudo aplicar la nueva distribución. La versión anterior se conserva íntegra.';
        this.changeDetector.detectChanges();
      }
    });
  }

  weeklyDraft(session: TrainingSession): WeeklySessionDraft {
    if (!this.weeklyEditorDrafts[session.id]) {
      this.weeklyEditorDrafts[session.id] = {
        date: session.scheduledDate.slice(0, 10), title: session.title, type: session.sessionType,
        duration: session.durationMinutes ?? 60, intensity: session.intensityTarget ?? '',
        instructions: session.instructions ?? '', exercises: this.parseStructuredExerciseDrafts(session.exercisePrescription, session.exercisePrescriptionData)
      };
    }
    return this.weeklyEditorDrafts[session.id];
  }

  private parseStructuredExerciseDrafts(
    prescription: string | null | undefined,
    prescriptionData?: Record<string, unknown> | null
  ): StructuredExerciseDraft[] {
    const structuredItems = Array.isArray(prescriptionData?.['exercises'])
      ? prescriptionData['exercises'] as Array<Record<string, unknown>>
      : [];
    const hasCompleteSnapshots = structuredItems.length > 0 &&
      structuredItems.every(item => item !== null && typeof item === 'object' &&
        typeof item['exerciseNameSnapshot'] === 'string' &&
        String(item['exerciseNameSnapshot']).trim().length > 0);

    if (hasCompleteSnapshots) {
      return structuredItems.map(item => {
        const exerciseName = String(item['exerciseNameSnapshot']).trim();
        const minRepetitions = typeof item['minRepetitions'] === 'number' ? item['minRepetitions'] : null;
        const maxRepetitions = typeof item['maxRepetitions'] === 'number' ? item['maxRepetitions'] : minRepetitions;
        const repetitions = minRepetitions !== null && minRepetitions >= 1
          ? (maxRepetitions !== null && maxRepetitions > minRepetitions
            ? `${minRepetitions}-${maxRepetitions}` : String(minRepetitions))
          : '';
        const sets = typeof item['sets'] === 'number' && Number.isFinite(item['sets'])
          ? Math.max(0, Math.min(10, Math.round(item['sets']))) : 0;
        const restSeconds = typeof item['restSeconds'] === 'number' && Number.isFinite(item['restSeconds'])
          ? Math.max(0, Math.min(3600, Math.round(item['restSeconds']))) : 0;
        const targetRir = item['targetRepetitionsInReserve'];
        const rawProgressionRule = item['progressionRule'];
        const progressionRule = rawProgressionRule === null || rawProgressionRule === undefined ||
          rawProgressionRule === 1 || rawProgressionRule === '1' || rawProgressionRule === 'DoubleProgression'
          ? 'DoubleProgression' : 'None';
        return {
          exerciseName,
          originalExerciseName: exerciseName,
          sets,
          repetitions,
          rir: typeof targetRir === 'number' && Number.isFinite(targetRir)
            ? Math.max(0, Math.min(10, targetRir)) : null,
          restSeconds,
          notes: typeof item['notes'] === 'string' ? item['notes'] : '',
          exerciseId: typeof item['exerciseId'] === 'string' ? item['exerciseId'] : undefined,
          catalogVersion: typeof item['catalogVersion'] === 'string' ? item['catalogVersion'] : undefined,
          progressionRule,
          primaryMuscleGroups: Array.isArray(item['primaryMuscleGroups'])
            ? item['primaryMuscleGroups'] as Array<string | number> : undefined,
          secondaryMuscleGroups: Array.isArray(item['secondaryMuscleGroups'])
            ? item['secondaryMuscleGroups'] as Array<string | number> : undefined,
          smallestLoadIncrementKg: typeof item['smallestLoadIncrementKg'] === 'number'
            ? item['smallestLoadIncrementKg'] : undefined,
          maximumLoadIncreasePercent: typeof item['maximumLoadIncreasePercent'] === 'number'
            ? item['maximumLoadIncreasePercent'] : undefined,
          warmupSets: typeof item['warmupSets'] === 'number' ? item['warmupSets'] : undefined,
          warmupNote: typeof item['warmupNote'] === 'string' ? item['warmupNote'] : undefined,
          targetLoadKg: typeof item['targetLoadKg'] === 'number' ? item['targetLoadKg'] : undefined,
          loadBasis: typeof item['loadBasis'] === 'string' ? item['loadBasis'] : undefined,
          adaptationOriginalSets: typeof item['adaptationOriginalSets'] === 'number' ? item['adaptationOriginalSets'] : undefined,
          selectionRationale: typeof item['selectionRationale'] === 'string'
            ? item['selectionRationale'] : undefined
        };
      });
    }

    // Legacy text is only a fallback when no usable structured snapshot exists.
    return this.prescriptionLines(prescription).map(line => {
      const match = line.match(/^(.*?)\s*[·|—-]\s*(\d+)\s*[×x]\s*([^·|—]+?)(?=\s*[·|—]|$)(.*)$/i);
      const tail = match?.[4] ?? line;
      const restMatch = tail.match(/descanso\s*(\d+)\s*(s|seg(?:undos?)?|min(?:utos?)?)?/i);
      const rirMatch = tail.match(/RIR\s*(\d+(?:[.,]\d+)?)/i);
      const exerciseName = (match?.[1] ?? line).trim().replace(/^\d+\.\s*/, '');
      return {
        exerciseName,
        originalExerciseName: exerciseName,
        sets: match ? Math.max(1, Math.min(10, Number(match[2]))) : 0,
        repetitions: (match?.[3] ?? '').trim(),
        rir: rirMatch ? Number(rirMatch[1].replace(',', '.')) : (match ? 2 : null),
        restSeconds: restMatch ? Number(restMatch[1]) * (/min/i.test(restMatch[2] ?? '') ? 60 : 1) : (match ? 90 : 0),
        progressionRule: 'DoubleProgression',
        notes: match ? '' : line
      };
    }).filter(item => item.exerciseName.length > 0);
  }
  private buildStructuredPrescriptionData(
    date: string,
    duration: number | null,
    exercises: StructuredExerciseDraft[]
  ): Record<string, unknown> | null {
    const entered = exercises.filter(item => item.exerciseName.trim());
    if (!entered.length || entered.some(item => item.sets < 1 || !item.repetitions.trim())) return null;
    const structured = entered;
    return {
      engineVersion: 'manual-editor-v1',
      goal: 'Manual',
      weekNumber: 1,
      day: this.dayName(new Date(date.slice(0, 10) + 'T00:00:00Z').getUTCDay()),
      estimatedDurationMinutes: duration ?? 60,
      exercises: structured.map((item, index) => {
        const catalogItem = this.findUniqueCatalogExerciseByName(item.exerciseName);
        const repetitions = item.repetitions.match(/(\d+)(?:\s*[-–]\s*(\d+))?/);
        const minRepetitions = repetitions ? Number(repetitions[1]) : undefined;
        const maxRepetitions = repetitions ? Number(repetitions[2] || repetitions[1]) : undefined;
        return {
          exerciseId: item.exerciseId?.trim() || catalogItem?.id.replace(/^repdb-/, ''),
          catalogVersion: item.catalogVersion?.trim() ||
            (catalogItem?.source === 'repdb' ? 'repdb' : item.exerciseId?.trim() ? 'unknown' : 'manual'),
          exerciseNameSnapshot: item.exerciseName.trim(),
          order: index + 1,
          sets: item.sets,
          minRepetitions,
          maxRepetitions,
          targetRepetitionsInReserve: item.rir,
          restSeconds: item.restSeconds,
          progressionRule: item.progressionRule ?? 'DoubleProgression',
          primaryMuscleGroups: item.primaryMuscleGroups,
          secondaryMuscleGroups: item.secondaryMuscleGroups,
          smallestLoadIncrementKg: item.smallestLoadIncrementKg,
          maximumLoadIncreasePercent: item.maximumLoadIncreasePercent,
          selectionRationale: item.selectionRationale,
          warmupSets: item.warmupSets, warmupNote: item.warmupNote,
          targetLoadKg: item.targetLoadKg, loadBasis: item.loadBasis,
          adaptationOriginalSets: item.adaptationOriginalSets,
          notes: item.notes.trim() || undefined
        };
      })
    };
  }

  private serializeStructuredExerciseDrafts(exercises: StructuredExerciseDraft[]): string {
    return exercises.filter(item => item.exerciseName.trim()).map(item => {
      if (item.sets < 1 || !item.repetitions.trim()) return item.notes || item.exerciseName.trim();
      return item.exerciseName.trim() + ' · ' + Math.max(1, Math.min(10, Math.round(Number(item.sets) || 1))) +
        ' × ' + item.repetitions.trim() + ' · descanso ' +
        Math.max(0, Math.min(3600, Math.round(Number(item.restSeconds) || 0))) + ' s' +
        (item.rir === null || !Number.isFinite(item.rir) ? '' : ' · RIR ' + Math.max(0, Math.min(10, item.rir))) +
        (item.notes.trim() ? ' · Nota: ' + item.notes.trim() : '');
    }).join('\n');
  }

  markWeeklyDraftDirty(session: TrainingSession): void {
    this.weeklyEditorDirty[session.id] = true;
  }

  onWeeklyDraftChanged(plan: TrainingPlan, session: TrainingSession): void {
    const draft = this.weeklyDraft(session);
    for (const exercise of draft.exercises) {
      const normalizeName = (value: string) => value.trim().replace(/\s+/g, ' ').toLocaleLowerCase('es');
      const originalName = exercise.originalExerciseName ?? exercise.exerciseName;
      if (normalizeName(originalName) !== normalizeName(exercise.exerciseName)) {
        // A renamed exercise must never keep the previous exercise's stable ID or anatomy.
        exercise.exerciseId = undefined;
        exercise.catalogVersion = undefined;
        exercise.primaryMuscleGroups = undefined;
        exercise.secondaryMuscleGroups = undefined;
        exercise.smallestLoadIncrementKg = undefined;
        exercise.maximumLoadIncreasePercent = undefined;
        exercise.selectionRationale = undefined;
        exercise.originalExerciseName = exercise.exerciseName;
      }
    }
    this.weeklyEditorDirty[session.id] = true;
    this.refreshManualVolumePreview(plan, draft.date, this.serializeStructuredExerciseDrafts(draft.exercises), session.id, draft.title, draft.exercises);
  }

  addWeeklyExercise(plan: TrainingPlan, session: TrainingSession): void {
    this.weeklyDraft(session).exercises.push({ exerciseName: '', sets: 3, repetitions: '8–12', rir: 2, restSeconds: 90, progressionRule: 'DoubleProgression', notes: '' });
    this.onWeeklyDraftChanged(plan, session);
  }

  removeWeeklyExercise(plan: TrainingPlan, session: TrainingSession, index: number): void {
    this.weeklyDraft(session).exercises.splice(index, 1);
    this.onWeeklyDraftChanged(plan, session);
  }

  moveWeeklyExercise(plan: TrainingPlan, session: TrainingSession, index: number, offset: number): void {
    const rows = this.weeklyDraft(session).exercises;
    const destination = index + offset;
    if (index < 0 || destination < 0 || destination >= rows.length) return;
    [rows[index], rows[destination]] = [rows[destination], rows[index]];
    this.onWeeklyDraftChanged(plan, session);
  }

  saveWeeklySession(plan: TrainingPlan, session: TrainingSession): void {
    const draft = this.weeklyDraft(session);
    const prescription = this.serializeStructuredExerciseDrafts(draft.exercises);
    if (!draft.title.trim() || !draft.date || draft.duration === null || !Number.isInteger(draft.duration) || draft.duration < 1 || draft.duration > 1440) {
      this.error = 'Revisa el nombre, la fecha y la duración de la sesión.';
      return;
    }
    if (draft.exercises.some(item => item.exerciseName.trim() && (!Number.isInteger(item.sets) || item.sets < 1 || item.sets > 10 || !item.repetitions.trim() || !Number.isInteger(item.restSeconds) || item.restSeconds < 0 || item.restSeconds > 3600 || (item.rir !== null && (item.rir < 0 || item.rir > 10))))) {
      this.error = 'Revisa series (1–10), repeticiones, descanso (0–3600 s) y RIR (0–10).';
      return;
    }
    this.saving = true;
    this.http.put('/api/training/sessions/' + session.id, {
      scheduledDate: draft.date, title: draft.title.trim(), sessionType: draft.type,
      durationMinutes: draft.duration, intensityTarget: draft.intensity.trim() || null,
      instructions: draft.instructions.trim() || null, exercisePrescription: prescription || null,
      exercisePrescriptionData: this.buildStructuredPrescriptionData(draft.date, draft.duration, draft.exercises)
    }).subscribe({
      next: () => {
        this.saving = false;
        delete this.weeklyEditorDrafts[session.id];
        delete this.weeklyEditorDirty[session.id];
        this.notice = 'Sesión actualizada. El registro de realización se ha conservado.';
        this.refreshSessions(plan.id);
      },
      error: error => { this.saving = false; this.error = error?.error?.message || 'No se pudo guardar la sesión.'; }
    });
  }

  canDeleteWeeklySession(session: TrainingSession): boolean {
    return !session.completedAt && session.scheduledDate >= this.localDateInputValue();
  }

  confirmWeeklySessionDeletion(plan: TrainingPlan, session: TrainingSession): void {
    if (!this.canDeleteWeeklySession(session)) return;
    this.weeklyDeleteConfirmIds[plan.id] = session.id;
    delete this.weeklyDeleteErrors[plan.id];
  }

  deleteWeeklySession(plan: TrainingPlan, session: TrainingSession): void {
    if (this.weeklyDeleteConfirmIds[plan.id] !== session.id || !this.canDeleteWeeklySession(session)) return;
    this.saving = true;
    this.http.delete('/api/training/sessions/' + session.id).subscribe({
      next: () => {
        this.saving = false;
        this.weeklyDeleteConfirmIds[plan.id] = null;
        delete this.weeklyEditorDrafts[session.id];
        delete this.weeklyEditorDirty[session.id];
        this.sessions[plan.id] = (this.sessions[plan.id] || []).filter(item => item.id !== session.id);
        delete this.weeklyDeleteErrors[plan.id];
        this.notice = 'Sesión eliminada. El historial de realización permanece protegido.';
        this.refreshManualVolumePreview(plan);
        this.refreshSessions(plan.id);
      },
      error: error => {
        this.saving = false;
        this.weeklyDeleteErrors[plan.id] = error?.error?.message || 'No se pudo eliminar la sesión.';
      }
    });
  }

  prepareWeeklySessionCreation(plan: TrainingPlan): void {
    const draft = this.sessionDrafts[plan.id];
    if (!draft) return;
    const hasUnsavedContent = !!(
      draft.title.trim() || draft.exercisePrescription.trim() || draft.instructions.trim() ||
      draft.intensity.trim() || draft.duration !== 60 || draft.type !== 'strength' ||
      draft.date !== this.defaultSessionDate(plan)
    );
    if (hasUnsavedContent) {
      this.notice = 'Ya hay un borrador de sesión sin guardar. Guárdalo o vacíalo antes de preparar otra sesión.';
    } else {
      const weekDates = this.weekDays(plan).map(day => day.date);
      const occupiedDates = new Set(this.weekSessions(plan.id, plan).map(session => session.scheduledDate));
      const currentDateIsFree = weekDates.includes(draft.date) && !occupiedDates.has(draft.date);
      draft.date = currentDateIsFree
        ? draft.date
        : weekDates.find(date => !occupiedDates.has(date)) || weekDates[0] || plan.startDate;
      draft.title = '';
      draft.type = 'strength';
      draft.duration = 60;
      draft.intensity = '';
      draft.instructions = '';
      draft.exercisePrescription = '';
      draft.exercisePrescriptionData = null;
      this.notice = 'Formulario preparado para añadir una sesión en esta semana.';
    }
    document.getElementById('session-editor-' + plan.id)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  weekPlannedMinutes(planId: number, plan: TrainingPlan): number {
    return this.weekSessions(planId, plan).reduce((total, session) => total + (session.durationMinutes ?? 0), 0);
  }

  weekCompletedCount(planId: number, plan: TrainingPlan): number {
    return this.weekSessions(planId, plan).filter(session => !!session.completedAt).length;
  }

  editSessionFromCalendar(session: TrainingSession): void {
    this.setSessionView(session.planId, 'list');
    this.beginEditSession(session);
  }

  recordSessionFromCalendar(session: TrainingSession): void {
    this.setSessionView(session.planId, 'list');
    this.openRecord(session);
  }

  cancelEditSession(plan: TrainingPlan): void {
    this.editingSessionId = null;
    this.sessionEditDraft = null;
    this.refreshManualVolumePreview(plan);
  }

  beginEditSession(session: TrainingSession): void {
    if (this.editingSessionId === session.id) {
      this.editingSessionId = null;
      this.sessionEditDraft = null;
      const plan = this.plans.find(item => item.id === session.planId);
      if (plan) this.refreshManualVolumePreview(plan);
      return;
    }
    this.editingSessionId = session.id;
    this.recordingSessionId = null;
    this.sessionEditDraft = { date: session.scheduledDate, title: session.title, type: session.sessionType,
      duration: session.durationMinutes ?? 60, intensity: session.intensityTarget ?? '', instructions: session.instructions ?? '',
      exercisePrescription: session.exercisePrescription ?? '', exercisePrescriptionData: session.exercisePrescriptionData ?? null };
    const structuredExercises = session.exercisePrescriptionData
      ? this.parseStructuredExerciseDrafts(session.exercisePrescription, session.exercisePrescriptionData)
      : undefined;
    this.refreshManualVolumePreview(
      (this.plans || []).find(plan => plan.id === session.planId) ?? { id: session.planId } as TrainingPlan,
      session.scheduledDate, session.exercisePrescription ?? '', session.id, undefined, structuredExercises);
  }

  saveSession(session: TrainingSession): void {
    const d = this.sessionEditDraft;
    if (!d || !d.title.trim() || !d.date || d.duration === null || !Number.isFinite(d.duration) || d.duration < 1 || d.duration > 1440) {
      this.error = 'Completa el nombre, la fecha y una duración entre 1 y 1440 minutos.';
      return;
    }
    this.saving = true; this.error = '';
    this.http.put(`/api/training/sessions/${session.id}`, { scheduledDate: d.date, title: d.title.trim(), sessionType: d.type,
      durationMinutes: d.duration, intensityTarget: d.intensity.trim() || null, instructions: d.instructions.trim() || null,
      exercisePrescription: d.exercisePrescription.trim() || null,
      exercisePrescriptionData: this.buildStructuredPrescriptionData(d.date, d.duration,
        this.parseStructuredExerciseDrafts(d.exercisePrescription, d.exercisePrescriptionData)) }).subscribe({
      next: () => { this.editingSessionId = null; this.sessionEditDraft = null; this.saving = false; this.refreshSessions(session.planId); },
      error: e => { this.error = e?.error?.message || 'No se pudo actualizar la sesión.'; this.saving = false; }
    });
  }

  private refreshSessions(planId: number): void {
    this.http.get<TrainingSession[]>(`/api/training/plans/${planId}/sessions`).subscribe({
      next: rows => {
        this.sessions[planId] = rows;
        const plan = this.plans.find(item => item.id === planId);
        if (plan) this.refreshManualVolumePreview(plan);
        this.changeDetector.detectChanges();
      },
      error: () => { this.error = 'La operación se guardó, pero no se pudo actualizar la lista. Recarga la página.'; this.changeDetector.detectChanges(); }
    });
  }

  defaultSessionDate(plan: TrainingPlan): string {
    const today = new Date().toISOString().slice(0, 10);
    return today < plan.startDate ? plan.startDate : plan.endDate && today > plan.endDate ? plan.endDate : today;
  }

  sessionTypeLabel(type: string): string {
    return ({ endurance: 'Resistencia', strength: 'Fuerza', mobility: 'Movilidad', recovery: 'Recuperación', health: 'Salud', general: 'Entrenamiento general', other: 'Otro' } as Record<string, string>)[type] || type;
  }

  sessionTypeIcon(type: string): string {
    return ({ endurance: '↗', strength: '✦', mobility: '↔', recovery: '☾', health: '♡', general: '◉', other: '•' } as Record<string, string>)[type] || '•';
  }

  openRecord(session: TrainingSession): void {
    if (this.recordingSessionId === session.id) { this.recordingSessionId = null; return; }
    this.recordingSessionId = session.id;
    this.recordDrafts[session.id] = {
      duration: session.actualDurationMinutes ?? session.durationMinutes ?? null,
      rpe: session.sessionRpe ?? 5,
      notes: session.recordNotes ?? ''
    };
  }

  record(session: TrainingSession): void {
    const draft = this.recordDrafts[session.id];
    if (!draft || draft.duration === null || !Number.isFinite(draft.duration) || draft.duration < 1 || draft.duration > 1440
      || draft.rpe === null || !Number.isFinite(draft.rpe) || draft.rpe < 0 || draft.rpe > 10) {
      this.error = 'Introduce una duración entre 1 y 1440 minutos y un RPE entre 0 y 10.';
      return;
    }
    this.saving = true; this.error = '';
    this.http.post(`/api/training/sessions/${session.id}/record`, {
      completedAt: new Date().toISOString(),
      actualDurationMinutes: draft.duration,
      sessionRpe: draft.rpe,
      notes: draft.notes.trim() || null
    }).subscribe({
      next: () => {
        this.saving = false;
        this.recordingSessionId = null;
        this.http.get<TrainingSession[]>(`/api/training/plans/${session.planId}/sessions`).subscribe({
          next: rows => { this.sessions[session.planId] = rows; this.changeDetector.detectChanges(); },
          error: () => { this.error = 'La realización se guardó, pero no se pudo actualizar la lista. Recarga la página.'; this.changeDetector.detectChanges(); }
        });
      },
      error: e => { this.error = e?.error?.message || 'No se pudo registrar la sesión.'; this.saving = false; }
    });
  }

  generateMacrocycleProposal(plan: TrainingPlan): void {
    const requestedWeeks = Math.max(3, Math.min(52, Math.round(this.macroProposalWeeks[plan.id] || 12)));
    const start = plan.startDate.slice(0, 10);
    let weeks = requestedWeeks;
    if (plan.endDate) {
      const availableDays = Math.floor((Date.parse(plan.endDate.slice(0, 10) + 'T00:00:00Z') - Date.parse(start + 'T00:00:00Z')) / 86400000) + 1;
      const availableWeeks = Math.floor(availableDays / 7);
      if (availableWeeks < 3) {
        this.macroProposals[plan.id] = [];
        this.notice = 'El periodo del plan es inferior a tres semanas. Amplía sus fechas antes de generar el macrociclo.';
        this.error = '';
        return;
      }
      weeks = Math.min(requestedWeeks, availableWeeks);
    }
    this.macroProposalWeeks[plan.id] = weeks;
    // The draft comes from the server (internal product heuristic, editable). Deload weeks are
    // never added unless the professional asks for them with the checkbox below.
    this.saving = true;
    this.http.post<{ phases: Array<{ name: string; sequenceNumber: number; startDate: string; endDate: string; objective?: string | null; phaseType: MacrocyclePhase['phaseType'] }> }>(
      '/api/training/macrocycle-draft',
      {
        startDate: start, totalWeeks: weeks, goal: this.autoGoal, experience: this.autoExperience,
        includeDeloadPhases: this.macroIncludeDeload, deloadEveryTrainingWeeks: this.macroDeloadEvery
      }
    ).subscribe({
      next: draft => {
        this.macroProposals[plan.id] = draft.phases.map(phase => ({
          name: phase.name, objective: phase.objective ?? '', phaseType: phase.phaseType,
          sequence: phase.sequenceNumber, startDate: phase.startDate, endDate: phase.endDate
        }));
        this.saving = false;
        this.notice = 'Borrador creado sin modificar mesociclos ni sesiones. Es una propuesta editable basada en criterios internos del producto; revisa fases y fechas antes de guardar.';
        this.error = '';
      },
      error: () => {
        this.saving = false;
        this.error = 'No se pudo preparar el borrador de macrociclo. Revisa la duración y el intervalo de descarga.';
      }
    });
  }

  addMacroPhase(plan: TrainingPlan): void {
    const phases = [...(this.macroProposals[plan.id] || [])];
    if (phases.length >= 12) return;
    const last = phases[phases.length - 1];
    const start = last ? this.addDays(last.endDate, 1) : plan.startDate.slice(0, 10);
    const sequence = phases.length + 1;
    phases.push({ name: 'Nueva fase', objective: '', phaseType: 'general', sequence, startDate: start, endDate: this.addDays(start, 27) });
    this.macroProposals[plan.id] = phases;
  }

  removeMacroPhase(planId: number, index: number): void {
    const phases = [...(this.macroProposals[planId] || [])];
    phases.splice(index, 1);
    this.macroProposals[planId] = phases.map((phase, phaseIndex) => ({ ...phase, sequence: phaseIndex + 1 }));
  }

  editMacrocycle(plan: TrainingPlan): void {
    const macrocycle = this.macrocyclePlans[plan.id];
    if (!macrocycle?.phases?.length) return;
    this.macroProposals[plan.id] = macrocycle.phases.map(phase => ({
      name: phase.name, sequence: phase.sequenceNumber, startDate: phase.startDate, endDate: phase.endDate,
      objective: phase.objective || '', phaseType: phase.phaseType || 'general'
    }));
    this.macroProposalWeeks[plan.id] = macrocycle.phases.reduce((total, phase) => total + this.macroPhaseDurationWeeks(phase), 0);
    this.notice = 'Editando la planificación guardada. Los cambios no se aplicarán hasta guardar.';
    this.error = '';
  }

  saveMacrocycleProposal(plan: TrainingPlan): void {
    const phases = [...(this.macroProposals[plan.id] || [])];
    if (!phases.length || phases.some(phase => !phase.name.trim() || !phase.startDate || !phase.endDate || phase.endDate < phase.startDate)) {
      this.error = 'Revisa el nombre y las fechas de cada fase antes de guardar.';
      return;
    }
    const ordered = [...phases].sort((a, b) => a.startDate.localeCompare(b.startDate));
    for (let index = 0; index < ordered.length; index++) {
      const phase = ordered[index];
      if (phase.startDate < plan.startDate.slice(0, 10) || (plan.endDate && phase.endDate > plan.endDate.slice(0, 10))) {
        this.error = 'La fase «' + phase.name + '» queda fuera de las fechas del plan. Ajusta el periodo o amplía el plan antes de guardar.';
        return;
      }
      if (index > 0 && phase.startDate <= ordered[index - 1].endDate) {
        this.error = 'Las fases del macrociclo se solapan. Corrige las fechas antes de guardar.';
        return;
      }
    }
    this.saving = true;
    this.error = '';
    const endDate = phases.reduce((value, phase) => phase.endDate > value ? phase.endDate : value, phases[0].endDate);
    this.http.put<MacrocyclePlan>(`/api/training/plans/${plan.id}/macrocycle`, {
      name: this.macrocyclePlans[plan.id]?.name || 'Macrociclo principal',
      periodizationModel: plan.periodizationModel || 'custom',
      startDate: plan.startDate.slice(0, 10),
      endDate: plan.endDate || endDate,
      notes: this.macrocyclePlans[plan.id]?.notes || null,
      phases: phases.map(phase => ({
        name: phase.name.trim(), sequenceNumber: phase.sequence, startDate: phase.startDate, endDate: phase.endDate,
        objective: phase.objective.trim() || null, phaseType: phase.phaseType || 'general'
      }))
    }).subscribe({
      next: macrocycle => {
        this.macrocyclePlans[plan.id] = macrocycle;
        this.selectedMacroPhaseId[plan.id] = macrocycle.phases[0]?.id || 0;
        this.macroProposals[plan.id] = [];
        this.saving = false;
        this.notice = 'Macrociclo guardado. La planificación de fases está separada de mesociclos, microciclos y sesiones.';
        this.error = '';
        this.changeDetector.detectChanges();
      },
      error: e => {
        this.error = e?.error?.message || 'No se pudo guardar el macrociclo. La propuesta se conserva para que no pierdas los cambios.';
        this.saving = false;
      }
    });
  }

  /** Datos de la gráfica de carga: semanas reales de cada fase, en orden, más los mesociclos como carril aparte. */
  macroChartModel(plan: TrainingPlan): { phases: MacroChartPhase[]; weeks: MacroChartWeek[]; mesocycles: MacroChartMesocycle[] } {
    return this.memo(`chart:${plan.id}`, [this.macrocyclePlans[plan.id], this.mesocycles[plan.id], this.sessions[plan.id]], () => {
      const phases = this.macrocyclePhases(plan);
      const weeks: MacroChartWeek[] = [];
      for (const phase of phases) {
        for (const week of this.macroPhaseWeeks(plan, phase)) {
          weeks.push({
            phaseId: phase.id, weekInPhase: week.weekNumber, startDate: week.startDate, endDate: week.endDate,
            sessions: week.sessions, completedSessions: week.completedSessions, plannedMinutes: week.plannedMinutes,
            sets: week.sets, rpe: week.intensityLabel === 'N/D' ? null : week.intensityPct / 10, isDeload: week.isDeload
          });
        }
      }
      const hasStoredMacrocycle = !!this.macrocyclePlans[plan.id]?.phases?.length;
      return {
        phases: phases.map(phase => ({
          id: phase.id, name: phase.name, sequenceNumber: phase.sequenceNumber, kind: this.macroPhaseKind(phase),
          startDate: phase.startDate, endDate: phase.endDate
        })),
        weeks,
        mesocycles: hasStoredMacrocycle
          ? (this.mesocycles[plan.id] || []).map(meso => ({ id: meso.id, name: meso.name, startDate: meso.startDate, endDate: meso.endDate || meso.startDate }))
          : []
      };
    });
  }

  /** Siguiente paso recomendado del flujo macrociclo → mesociclos → rutinas. */
  planStatusLabel(status: string): string {
    return status === 'draft' ? 'Borrador' : status === 'active' ? 'Activo' : status === 'paused' ? 'Pausado' : status;
  }

  canPublishPlan(plan: TrainingPlan): boolean {
    if (plan.status !== 'draft' || !this.sessions[plan.id] || !this.mesocycles[plan.id]) return false;
    if (!this.sessions[plan.id].length) return false;
    return this.mesocycles[plan.id].every(meso => meso.reviewStatus === 'reviewed');
  }

  publishPlanReason(plan: TrainingPlan): string {
    if (plan.status !== 'draft') return '';
    if (!this.sessions[plan.id] || !this.mesocycles[plan.id]) return 'Cargando sesiones y revisiones…';
    if (!this.sessions[plan.id].length) return 'Añade al menos una sesión antes de publicar.';
    const pending = this.mesocycles[plan.id].filter(meso => meso.reviewStatus !== 'reviewed').length;
    if (pending) return pending === 1 ? 'Queda un mesociclo pendiente de revisión profesional.' : `Quedan ${pending} mesociclos pendientes de revisión profesional.`;
    return 'Listo para publicar. El cliente recibirá un aviso y podrá consultar el plan activo.';
  }

  publishPlan(plan: TrainingPlan): void {
    if (!this.canPublishPlan(plan) || this.saving) return;
    if (!confirm(`¿Publicar «${plan.name}»? El plan pasará a estado activo y se notificará al cliente.`)) return;
    this.saving = true;
    this.error = '';
    this.http.post<{ id: number; status: string; alreadyPublished: boolean }>(`/api/training/plans/${plan.id}/publish`, {}).subscribe({
      next: result => {
        this.plans = this.plans.map(item => item.id === plan.id ? { ...item, status: result.status } : item);
        this.saving = false;
        this.notice = result.alreadyPublished ? 'El plan ya estaba publicado.' : 'Plan publicado y notificación enviada al cliente.';
        this.changeDetector.detectChanges();
      },
      error: e => {
        this.error = e?.error?.message || 'No se pudo publicar el plan.';
        this.saving = false;
      }
    });
  }

  planNextStep(plan: TrainingPlan): { workspace: 'macrocycle' | 'mesocycles' | 'routines'; title: string; text: string; action: string } | null {
    const hasMacro = !!this.macrocyclePlans[plan.id]?.phases?.length;
    const mesoCount = this.mesocycles[plan.id]?.length || 0;
    const sessionCount = this.sessions[plan.id]?.length || 0;
    if (!hasMacro && !mesoCount && !sessionCount) return { workspace: 'macrocycle', title: 'Empieza por el macrociclo', text: 'Define las fases de la temporada; después podrás generar los bloques y las rutinas automáticamente.', action: 'Diseñar macrociclo' };
    if (hasMacro && !mesoCount) return { workspace: 'macrocycle', title: 'Genera los mesociclos y rutinas', text: 'El macrociclo ya tiene fases. Conviértelas en bloques con sus semanas y sesiones.', action: 'Generar bloques' };
    if (mesoCount && !sessionCount) return { workspace: 'routines', title: 'Añade sesiones a tus bloques', text: 'Hay mesociclos sin rutinas. Crea sesiones o genera una propuesta.', action: 'Ir a rutinas' };
    return null;
  }

  macrocycleLoadWeeks(plan: TrainingPlan): MacroPhaseLoadWeek[] {
    const phases = this.macrocyclePhases(plan);
    if (!phases.length) return [];
    const startDate = phases.reduce((value, phase) => phase.startDate < value ? phase.startDate : value, phases[0].startDate);
    const endDate = phases.reduce((value, phase) => phase.endDate > value ? phase.endDate : value, phases[0].endDate);
    return this.macroPhaseWeeks(plan, {
      id: 0, name: 'Macrociclo completo', sequenceNumber: 0, startDate, endDate,
      phaseType: 'general'
    }).map((week, index) => ({ ...week, weekNumber: index + 1 }));
  }

  macrocycleVolumeChart(plan: TrainingPlan): {
    phases: Array<{ id: number; name: string; weeks: Array<{ weekNumber: number; startDate: string; endDate: string; sets: number | null }> }>;
    maxSets: number; totalSets: number; hasVolume: boolean;
  } {
    const phases = this.macrocyclePhases(plan).slice().sort((a, b) => a.sequenceNumber - b.sequenceNumber).map(phase => ({
      id: phase.id,
      name: 'Fase ' + phase.sequenceNumber + ' · ' + phase.name,
      weeks: this.macroPhaseWeeks(plan, phase).map(week => ({
        weekNumber: week.weekNumber, startDate: week.startDate, endDate: week.endDate, sets: week.sets
      }))
    }));
    const values = phases.flatMap(phase => phase.weeks.map(week => week.sets).filter((sets): sets is number => sets !== null));
    return {
      phases,
      maxSets: Math.max(1, ...values),
      totalSets: values.reduce((total, sets) => total + sets, 0),
      hasVolume: values.length > 0 && values.some(sets => sets > 0)
    };
  }

  mesocycleLoadSummary(plan: TrainingPlan): Array<{
    id: number; name: string; startDate: string; endDate: string; sessions: number;
    completedSessions: number; plannedMinutes: number; sets: number | null;
    volumePct: number; frequencyPct: number;
  }> {
    return this.memo(`mesoSummary:${plan.id}`, [this.mesocycles[plan.id], this.sessions[plan.id]], () => this.computeMesocycleLoadSummary(plan));
  }

  private computeMesocycleLoadSummary(plan: TrainingPlan): Array<{
    id: number; name: string; startDate: string; endDate: string; sessions: number;
    completedSessions: number; plannedMinutes: number; sets: number | null;
    volumePct: number; frequencyPct: number;
  }> {
    const rows = (this.mesocycles[plan.id] || []).map(meso => {
      const weeks = this.macroPhaseWeeks(plan, meso);
      const hasSets = weeks.some(week => week.sets !== null);
      return {
        id: meso.id, name: meso.name, startDate: meso.startDate, endDate: meso.endDate || meso.startDate,
        sessions: weeks.reduce((total, week) => total + week.sessions, 0),
        completedSessions: weeks.reduce((total, week) => total + week.completedSessions, 0),
        plannedMinutes: weeks.reduce((total, week) => total + week.plannedMinutes, 0),
        sets: hasSets ? weeks.reduce((total, week) => total + (week.sets ?? 0), 0) : null,
        volumePct: 0, frequencyPct: 0
      };
    });
    const maxSets = Math.max(1, ...rows.map(row => row.sets ?? 0));
    const maxSessions = Math.max(1, ...rows.map(row => row.sessions));
    return rows.map(row => ({
      ...row,
      volumePct: row.sets === null ? 0 : Math.round(row.sets / maxSets * 100),
      frequencyPct: Math.round(row.sessions / maxSessions * 100)
    }));
  }

  async generateAllMacrocycleMesocycles(plan: TrainingPlan, skipConfirm = false): Promise<void> {
    const phases = this.macrocyclePhases(plan).slice().sort((a, b) => a.sequenceNumber - b.sequenceNumber);
    if (!phases.length || this.saving || this.generatingPlan || this.bulkGenerationProgress[plan.id]) return;
    const days = [...new Set(this.autoTrainingDaysCsv.split(',').map(value => Number(value.trim())).filter(value => Number.isInteger(value) && value >= 0 && value <= 6))];
    if (days.length < 2 || days.length > 6 || !this.isSplitDayCountValid(days.length) || this.autoMaximumMinutes < 35 || this.autoMaximumMinutes > 180) {
      this.generationErrors[plan.id] = 'Revisa los días compatibles con la distribución elegida (2–6 días) y la duración máxima de 35 a 180 minutos.';
      return;
    }
    if (!skipConfirm && !confirm('Se generarán mesociclos, microciclos y sesiones para todas las fases del macrociclo que todavía no tengan planificación. Los bloques existentes con contenido se conservarán. ¿Continuar?')) return;
    this.generationErrors[plan.id] = '';
    this.generatingPlan = true;
    this.bulkGenerationProgress[plan.id] = { completed: 0, total: phases.length, phaseName: 'Preparando…' };
    this.changeDetector.detectChanges();
    try {
      let created = 0;
      let skipped = 0;
      for (const phase of phases) {
        this.bulkGenerationProgress[plan.id] = { completed: created + skipped, total: phases.length, phaseName: phase.name };
        this.changeDetector.detectChanges();
        const result = await this.generateMacrocyclePhase(plan, phase, days);
        if (result === 'created') created++;
        else skipped++;
        this.bulkGenerationProgress[plan.id] = { completed: created + skipped, total: phases.length, phaseName: phase.name };
        this.changeDetector.detectChanges();
      }
      this.notice = `Generación completada: ${created} mesociclos creados y ${skipped} fases conservadas porque ya tenían planificación.`;
      this.error = '';
      this.load();
    } catch (error: any) {
      this.generationErrors[plan.id] = error?.message || 'La generación se detuvo. Los mesociclos ya guardados se conservan; puedes reanudar sin duplicarlos.';
      this.error = '';
      this.load();
    } finally {
      delete this.bulkGenerationProgress[plan.id];
      this.generatingPlan = false;
      this.changeDetector.detectChanges();
    }
  }

  async generateMacrocyclePhaseMesocycle(plan: TrainingPlan, phase: MacrocyclePhase): Promise<void> {
    if (this.saving || this.generatingPlan || this.bulkGenerationProgress[plan.id]) return;
    const days = [...new Set(this.autoTrainingDaysCsv.split(',').map(value => Number(value.trim())).filter(value => Number.isInteger(value) && value >= 0 && value <= 6))];
    if (days.length < 2 || days.length > 6 || !this.isSplitDayCountValid(days.length) || this.autoMaximumMinutes < 35 || this.autoMaximumMinutes > 180) {
      this.generationErrors[plan.id] = 'Revisa los días compatibles con la distribución elegida (2–6 días) y la duración máxima de 35 a 180 minutos.';
      return;
    }
    this.generatingPlan = true;
    this.generationErrors[plan.id] = '';
    this.changeDetector.detectChanges();
    try {
      const result = await this.generateMacrocyclePhase(plan, phase, days);
      this.notice = result === 'created' ? 'Mesociclo y rutinas generados para «' + phase.name + '».' : 'La fase «' + phase.name + '» ya tenía planificación y se ha conservado.';
      this.load();
    } catch (error: any) {
      this.generationErrors[plan.id] = error?.message || 'No se pudo generar el mesociclo.';
    } finally {
      this.generatingPlan = false;
      this.changeDetector.detectChanges();
    }
  }

  private async generateMacrocyclePhase(plan: TrainingPlan, phase: MacrocyclePhase, days: number[]): Promise<'created' | 'skipped'> {
    const totalDays = Math.floor((Date.parse(phase.endDate.slice(0, 10) + 'T00:00:00Z') - Date.parse(phase.startDate.slice(0, 10) + 'T00:00:00Z')) / 86400000) + 1;
    const durationWeeks = Math.floor(totalDays / 7);
    if (durationWeeks < 1) throw new Error('La fase «' + phase.name + '» dura menos de siete días y no admite un mesociclo automático.');
    if (phase.phaseType === 'deload' && durationWeeks !== 1)
      throw new Error('La fase «' + phase.name + '» está marcada como descarga y debe durar exactamente una semana. Divide la fase antes de generar.');
    const startDate = phase.startDate.slice(0, 10);
    const generatedEnd = this.addDays(startDate, durationWeeks * 7 - 1);
    if (startDate < plan.startDate.slice(0, 10) || (plan.endDate && generatedEnd > plan.endDate.slice(0, 10)))
      throw new Error('La fase «' + phase.name + '» queda fuera de las fechas del plan.');
    const overlapping = (this.mesocycles[plan.id] || []).filter(meso =>
      startDate <= (meso.endDate || meso.startDate).slice(0, 10) && generatedEnd >= meso.startDate.slice(0, 10));
    const existing = overlapping.find(meso =>
      meso.startDate.slice(0, 10) <= startDate &&
      (meso.endDate || meso.startDate).slice(0, 10) >= generatedEnd);
    const hasSessions = (this.sessions[plan.id] || []).some(session =>
      session.scheduledDate.slice(0, 10) >= startDate && session.scheduledDate.slice(0, 10) <= generatedEnd);
    if (existing && ((this.microcycles[existing.id] || []).length > 0 || hasSessions)) return 'skipped';
    if (hasSessions && !existing)
      throw new Error('La fase «' + phase.name + '» ya contiene sesiones fuera de un mesociclo vacío. Se conservan y no se generarán sesiones duplicadas.');
    if (overlapping.length && !existing)
      throw new Error('La fase «' + phase.name + '» se solapa con un mesociclo existente. Ajusta sus fechas antes de continuar.');
    const generation: GenerationRequest = {
      goal: this.autoGoal, experience: this.autoExperience, startDate, durationWeeks, trainingDays: days,
      maximumSessionDurationMinutes: this.autoMaximumMinutes,
      availableEquipment: [...new Set(this.autoEquipmentCsv.split(',').map(value => value.trim()).filter(Boolean))],
      excludedExerciseIds: [],
      muscleVolumeTargets: this.buildMuscleVolumeTargets(),
      isDeload: phase.phaseType === 'deload',
      split: this.autoSplit
    };
    const sequenceNumber = existing?.sequenceNumber ??
      Math.max(0, ...(this.mesocycles[plan.id] || []).map(meso => meso.sequenceNumber)) + 1;
    const response = await firstValueFrom(this.http.post<{ mesocycleId: number; sessionsCreated: number }>(
      `/api/training/plans/${plan.id}/generate-mesocycle`,
      {
        name: existing?.name || phase.name,
        sequenceNumber,
        generation,
        existingMesocycleId: existing?.id ?? null,
        notes: 'Generado automáticamente desde el macrociclo. Requiere revisión profesional antes de asignarlo al paciente.'
      }
    ).pipe(timeout({ first: 60000 })));
    const createdMeso: TrainingMesocycle = {
      id: response.mesocycleId, planId: plan.id, name: existing?.name || phase.name, sequenceNumber,
      startDate, endDate: generatedEnd, objective: phase.objective, notes: 'Generado automáticamente.'
    };
    this.mesocycles[plan.id] = [...(this.mesocycles[plan.id] || []).filter(meso => meso.id !== createdMeso.id), createdMeso];
    return 'created';
  }

  private addDays(value: string, days: number): string {
    const date = new Date(value + 'T12:00:00Z');
    date.setUTCDate(date.getUTCDate() + days);
    return date.toISOString().slice(0, 10);
  }

  generatedLoadWeeks(program: GeneratedProgram): Array<{ weekNumber: number; sessions: number; sets: number; averageRir: number | null; volumePct: number; frequencyPct: number; intensityPct: number; loadIndex: number }> {
    const weeks = Array.from({ length: Math.max(1, program.durationWeeks) }, (_, index) => {
      const weekNumber = index + 1;
      const sessions = program.sessions.filter(session => session.weekNumber === weekNumber);
      const sets = sessions.reduce((sum, session) => sum + session.exercises.reduce((exerciseSum, exercise) => exerciseSum + Math.max(0, exercise.sets || 0), 0), 0);
      const rirValues = sessions.flatMap(session => session.exercises.map(exercise => exercise.targetRepetitionsInReserve).filter((value): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 10));
      const averageRir = rirValues.length ? Math.round((rirValues.reduce((sum, value) => sum + value, 0) / rirValues.length) * 10) / 10 : null;
      const intensity = averageRir === null ? null : Math.max(0, Math.min(10, 10 - averageRir));
      const load = sets * (intensity === null ? 1 : 0.7 + intensity / 10 * 0.6) * (1 + Math.max(0, sessions.length - 2) * 0.05);
      return { weekNumber, sessions: sessions.length, sets, averageRir, intensity, load };
    });
    const maxSets = Math.max(1, ...weeks.map(week => week.sets));
    const maxSessions = Math.max(1, ...weeks.map(week => week.sessions));
    const maxLoad = Math.max(1, ...weeks.map(week => week.load));
    return weeks.map(week => ({ weekNumber: week.weekNumber, sessions: week.sessions, sets: week.sets, averageRir: week.averageRir,
      volumePct: Math.round(week.sets / maxSets * 100), frequencyPct: Math.round(week.sessions / maxSessions * 100),
      intensityPct: week.intensity === null ? 0 : Math.round(week.intensity / 10 * 100), loadIndex: Math.round(week.load / maxLoad * 100) }));
  }

  // Static state survives component recreation, preventing a render/recreate loop from resetting the report budget.
  private static readonly viewDiagnosticLastSent = new Map<string, number>();
  private static readonly viewDiagnosticSendTimes: number[] = [];

  /** Template-safe name access that records the exact binding when malformed state reaches the view. */
  viewName(value: unknown, binding: string): string {
    if (value && typeof value === 'object') {
      const name = (value as { name?: unknown }).name;
      if (typeof name === 'string' && name.trim()) return name;
    }
    this.reportMissingViewName(binding, value);
    return '[dato no disponible]';
  }

  /** Keeps ngModel bindings editable while validating the target before writing to it. */
  updateName(value: unknown, nextName: string, binding: string): void {
    if (!value || typeof value !== 'object') {
      this.reportMissingViewName(binding + '.ngModelChange', value);
      return;
    }
    (value as { name?: unknown }).name = typeof nextName === 'string' ? nextName.slice(0, 160) : '';
  }

  private reportMissingViewName(binding: string, value: unknown): void {
    const now = Date.now();
    const previous = TrainingPeriodizationComponent.viewDiagnosticLastSent.get(binding);
    if (previous !== undefined && now - previous < 30_000) return;
    while (TrainingPeriodizationComponent.viewDiagnosticSendTimes.length && now - TrainingPeriodizationComponent.viewDiagnosticSendTimes[0] >= 60_000) TrainingPeriodizationComponent.viewDiagnosticSendTimes.shift();
    if (TrainingPeriodizationComponent.viewDiagnosticSendTimes.length >= 5) return;
    TrainingPeriodizationComponent.viewDiagnosticLastSent.set(binding, now);
    TrainingPeriodizationComponent.viewDiagnosticSendTimes.push(now);

    const shape = value === null ? 'null' : typeof value;
    const context = [
      'Binding=' + binding,
      'ValueType=' + shape,
      'clientId=' + this.clientId,
      'plans=' + this.plans.length,
      'trainingTemplates=' + this.trainingTemplates.length,
      'sessionTemplates=' + this.sessionTemplates.length,
      'adaptationProposals=' + this.adaptationProposals.length,
      'exerciseCatalog=' + this.exerciseCatalog.length,
      'macroProposalCounts=' + Object.entries(this.macroProposals).map(([id, rows]) => id + ':' + (Array.isArray(rows) ? rows.length : 'invalid')).join(',')
    ].join('; ');
    const timestamp = new Date().toISOString();
    this.http.post('/api/client-diagnostics/runtime-error', {
      eventId: 'DEX-VIEW-' + now.toString(36).toUpperCase(),
      path: typeof window !== 'undefined' ? window.location.pathname : '/training',
      occurredAtUtc: timestamp,
      errorName: 'TrainingViewMissingNameBinding',
      errorMessage: 'Missing or invalid name binding: ' + binding,
      stackTrace: context
    }).subscribe({ error: () => undefined });
  }

  modelLabel(model: string): string {
    return ({ linear: 'Lineal', undulating: 'Ondulante', block: 'Bloques', reverse: 'Inversa', continuous_competition: 'Competición continua', autoregulatory: 'Autoregulado', custom: 'Personalizado' } as Record<string,string>)[model] || model;
  }
}
