import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnDestroy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, firstValueFrom, forkJoin, of, Subscription, timeout } from 'rxjs';
import { DateInputDirective } from '../../directives/date-input.directive';

interface TrainingProfile {
  id: number; clientId: number; enabled: boolean; primaryGoal: string; experienceLevel: string; managementMode: 'none' | 'self' | 'collaboration'; notes?: string | null; canManage?: boolean;
}
interface TrainingPlan {
  id: number; name: string; periodizationModel: string; startDate: string; endDate?: string | null; status: string; version: number; notes?: string | null;
}
interface GeneratedExercise {
  exerciseId: string; catalogVersion: string; exerciseNameSnapshot: string; order: number;
  sets: number; minRepetitions: number; maxRepetitions: number; targetRepetitionsInReserve?: number | null;
  restSeconds: number; progressionRule: string; selectionRationale: string;
}
interface GeneratedSession {
  weekNumber: number; day: number; title: string; estimatedDurationMinutes: number; exercises: GeneratedExercise[];
}
interface GeneratedProgram {
  engineVersion: string; goal: string; startDate: string; durationWeeks: number; sessions: GeneratedSession[];
  qualityReport?: {
    methodology: string;
    weeklyMuscleVolumes: Array<{ weekNumber: number; muscles: Array<{ muscleGroup: string; primarySets: number; secondarySets: number; primaryFrequencySessions: number; secondaryFrequencySessions: number; estimatedEffectiveSets: number }> }>;
    warnings: string[];
    sessionVolumes?: Array<{ weekNumber: number; day: number; title: string; muscles: Array<{ muscleGroup: string; estimatedEffectiveSets: number }> }>;
  } | null;
}
interface GenerationRequest {
  goal: number; experience: number; startDate: string; durationWeeks: number; trainingDays: number[]; split: number;
  maximumSessionDurationMinutes: number; availableEquipment: string[]; excludedExerciseIds: string[];
  muscleVolumeTargets?: Array<{ muscleGroup: string; priority: number; targetWeeklyPrimarySets?: number | null }>;
  isDeload?: boolean;
}
interface ManualVolumePreview {
  qualityReport: NonNullable<GeneratedProgram['qualityReport']>;
  unclassifiedExerciseNames: string[];
  sessionReports: Array<{
    weekNumber: number; day: number; title: string;
    qualityReport: NonNullable<GeneratedProgram['qualityReport']>;
    unclassifiedExerciseNames: string[];
  }>;
}
interface MuscleVolumePreferenceDraft {
  muscleGroup: string; label: string; enabled: boolean; priority: number; targetWeeklyPrimarySets: number | null;
}
interface TrainingExerciseVolumeRecommendation {
  exerciseId: string; action: string; explanation: string; suggestedWorkingSets?: number | null; exposuresAnalyzed: number;
}
interface TrainingAdaptationProposal {
  id: number; clientId: number; planId: number; sourceSessionId: number; sourceRecordId: number;
  action: string; explanation: string; status: string; createdAt: string; reviewNotes?: string | null;
  appliedFutureSessions: number; sessionDate: string; sessionTitle: string;
  exerciseVolumeRecommendations?: TrainingExerciseVolumeRecommendation[];
}
interface TrainingSession {
  id: number; planId: number; scheduledDate: string; title: string; sessionType: string; durationMinutes?: number | null;
  intensityTarget?: string | null; instructions?: string | null; status: string; completedAt?: string | null;
  actualDurationMinutes?: number | null; sessionRpe?: number | null; recordNotes?: string | null; exercisePrescription?: string | null; exercisePrescriptionData?: Record<string, unknown> | null;
}
interface StructuredExerciseDraft {
  exerciseName: string; sets: number; repetitions: string; rir: number | null; restSeconds: number; notes: string;
}
interface WeeklySessionDraft {
  date: string; title: string; type: string; duration: number | null; intensity: string; instructions: string;
  exercises: StructuredExerciseDraft[];
}
interface SplitReorganizationMove { sessionId: number; title: string; oldDate: string; scheduledDate: string; }
interface SplitReorganizationDraft { splitType: number; trainingDays: number[]; moves: SplitReorganizationMove[] | null; error: string; }
interface SplitReplacementSession {
  scheduledDate: string; title: string; sessionType: string; durationMinutes: number;
  intensityTarget: string | null; instructions: string | null; exercisePrescription: string;
  exercisePrescriptionData: Record<string, unknown> | null;
}
interface SplitReplacementPreview {
  replaceFromDate: string; replaceThroughDate: string; sessions: SplitReplacementSession[];
  volumePreview: ManualVolumePreview;
  preservedManualEdits: number; retainedCustomExercises: number; warnings: string[];
}
interface TrainingLoadWeek { weekStart: string; completedSessions: number; plannedMinutes: number; actualMinutes: number; loadScore: number; }
interface TrainingMesocycle { id: number; planId: number; name: string; sequenceNumber: number; startDate: string; endDate?: string | null; objective?: string | null; notes?: string | null; }
interface MacrocyclePhase { id: number; name: string; sequenceNumber: number; startDate: string; endDate: string; objective?: string | null; phaseType: 'accumulation' | 'intensification' | 'deload' | 'realization' | 'general'; }
interface MacroPhaseLoadWeek { weekNumber: number; startDate: string; endDate: string; sessions: number; completedSessions: number; plannedMinutes: number; sets: number | null; intensityLabel: string; intensityPct: number; volumePct: number; frequencyPct: number; isDeload: boolean; }
interface MacrocyclePlan { id: number; planId: number; name: string; periodizationModel: string; startDate: string; endDate?: string | null; notes?: string | null; phases: MacrocyclePhase[]; }
interface TrainingMicrocycle { id: number; mesocycleId: number; name: string; sequenceNumber: number; startDate: string; endDate?: string | null; focus?: string | null; notes?: string | null; }
interface TrainingCollaboration {
  id: number; professionalUserId?: number; professionalName?: string; email?: string; accessScope: string;
  status: string; createdAt: string; acceptedAt?: string | null; revokedAt?: string | null;
}
interface TrainingInvitation {
  id: number; clientId: number; clientName: string; accessScope: string; createdAt: string; invitedBy: string;
}
interface TrainingTemplate {
  id: number; name: string; periodizationModel: string; notes?: string | null; createdAt: string; sessionCount: number;
}
interface TrainingSessionTemplate {
  id: number; name: string; sessionType: string; durationMinutes?: number | null; intensityTarget?: string | null;
  instructions?: string | null; exercisePrescription?: string | null; exercisePrescriptionData?: Record<string, unknown> | null; createdAt?: string;
}
interface RepDbExercise {
  id: string;
  nameEs?: string;
  nameEn: string;
  equipment?: string;
  bodyPart?: string;
  category?: string;
  goals?: string[];
  primaryMuscles?: string[];
  secondaryMuscles?: string[];
  instructionsEs?: string[];
  descriptionEs?: string;
  images?: { flat?: { start?: string; main?: string; peak?: string } };
}

@Component({
  selector: 'app-training-periodization',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, DateInputDirective],
  template: `
    <main class="training-page">
      <a *ngIf="clientId > 0" class="back-link" [routerLink]="['/clients', clientId]">← Volver al expediente</a>
      <header><div><p class="eyebrow">MÓDULO OPCIONAL</p><h1>{{ clientId > 0 ? 'Planificación deportiva' : 'Entrenamientos y colaboraciones' }}</h1><p *ngIf="clientId > 0">Actívala solo para clientes que quieran trabajar objetivos de actividad física o entrenamiento.</p><p *ngIf="clientId === 0">Gestiona las invitaciones de colaboración. Para crear o consultar un plan, entra en la ficha del cliente y abre su sección de planificación deportiva.</p></div></header>
      <p *ngIf="loading" role="status">{{ clientId > 0 ? 'Cargando planificación…' : 'Cargando invitaciones…' }}</p>
      <p *ngIf="error" class="error" role="alert">{{ error }}</p>
            <p *ngIf="notice" class="success" role="status">{{ notice }}</p>
      <section class="panel adaptation-review-panel" *ngIf="clientId > 0 && profile?.enabled && profile.canManage && !loading">
        <div class="section-heading"><div><h2>Revisión de adaptación por feedback</h2><p>Las recomendaciones del paciente no cambian el plan hasta que las revises. Las sesiones pasadas nunca se modifican.</p></div><span class="status">{{ adaptationProposals.length }} pendientes</span></div>
        <p *ngIf="!adaptationProposals.length" class="muted">No hay propuestas pendientes. Cuando el paciente registre feedback relevante, aparecerá aquí para su revisión.</p>
        <article *ngFor="let proposal of adaptationProposals" class="template-row adaptation-proposal-row">
          <div class="adaptation-proposal-content">
            <strong>{{ adaptationActionLabel(proposal.action) }}</strong>
            <p>{{ proposal.sessionTitle }} · {{ proposal.sessionDate | date:'dd/MM/yyyy' }} · {{ proposal.createdAt | date:'dd/MM/yyyy HH:mm' }}</p>
            <p>{{ proposal.explanation }}</p>
            <small *ngIf="proposal.action === 'review_and_reduce_training_stress'">Solo se proponen reducciones cuando hay señales repetidas de fatiga en un ejercicio concreto. Revisa la evidencia y selecciona los ejercicios que quieras adaptar.</small>
            <div *ngIf="proposal.action === 'review_and_reduce_training_stress'" class="adaptation-exercise-picker">
              <p *ngIf="!proposal.exerciseVolumeRecommendations?.length">No hay registros por ejercicio suficientes para proponer una reducción específica. No se recortará volumen de forma global.</p>
              <label *ngFor="let recommendation of proposal.exerciseVolumeRecommendations">
                <input *ngIf="recommendation.action === 'ProposeReduceOneSet'" type="checkbox"
                  [checked]="isAdaptationExerciseSelected(proposal.id, recommendation.exerciseId)"
                  (change)="toggleAdaptationExercise(proposal.id, recommendation.exerciseId, $any($event.target).checked)" />
                <span *ngIf="recommendation.action === 'ProposeReduceOneSet'">
                  {{ recommendation.exerciseId }} — {{ recommendation.explanation }}
                </span>
                <span *ngIf="recommendation.action !== 'ProposeReduceOneSet'">
                  {{ recommendation.exerciseId }} — {{ recommendation.explanation }}
                </span>
              </label>
            </div>
            <small *ngIf="proposal.action === 'hold_progression_and_review'">Al aprobar: pausa la progresión en sesiones futuras no realizadas.</small>
            <small *ngIf="proposal.action === 'pause_affected_exercise_and_review'">Por seguridad no se reanuda automáticamente el ejercicio asociado al dolor; requiere ajuste profesional explícito.</small>
            <label>Nota de revisión (opcional)<textarea [name]="'adaptationReviewNotes' + proposal.id" [(ngModel)]="adaptationReviewNotes[proposal.id]" rows="2" maxlength="2000" placeholder="Motivo de la decisión o ajustes manuales pendientes"></textarea></label>
          </div>
          <div class="cycle-actions">
            <button type="button" (click)="reviewAdaptationProposal(proposal, 'approved')" [disabled]="saving">Revisar y aprobar</button>
            <button type="button" class="secondary" (click)="reviewAdaptationProposal(proposal, 'rejected')" [disabled]="saving">Rechazar</button>
          </div>
        </article>
      </section>
      <p *ngIf="acceptedClientId" role="status">Colaboración aceptada. <a [routerLink]="['/clients', acceptedClientId, 'training']">Abrir el entrenamiento</a></p>
      <section class="panel template-library" *ngIf="!loading">
        <div class="section-heading"><div><h2>Plantillas de entrenamiento</h2><p>Guarda planes como modelos reutilizables y aplícalos a otros clientes sin alterar el original.</p></div><span class="status">{{ trainingTemplates.length }} plantillas</span></div>
        <p *ngIf="!trainingTemplates.length" class="muted">Todavía no tienes plantillas. Abre un plan de un cliente y pulsa «Guardar como plantilla» para empezar tu biblioteca.</p>
        <article *ngFor="let template of trainingTemplates" class="template-row">
          <div><strong>{{ viewName(template, 'trainingTemplates.template') }}</strong><p>{{ modelLabel(template.periodizationModel) }} · {{ template.sessionCount }} sesiones · {{ template.createdAt | date:'mediumDate' }}</p><small *ngIf="template.notes">{{ template.notes }}</small></div>
          <span class="template-chip">Reutilizable</span>
        </article>
        <form *ngIf="clientId > 0 && profile?.enabled && profile.canManage && trainingTemplates.length" class="form-grid template-apply" (ngSubmit)="createPlanFromTemplate()">
          <h3 class="full">Crear un plan a partir de una plantilla</h3>
          <label>Plantilla<select name="selectedTrainingTemplate" [(ngModel)]="selectedTemplateId" required><option [ngValue]="null" disabled>Selecciona una plantilla</option><option *ngFor="let template of trainingTemplates" [ngValue]="template.id">{{ viewName(template, 'trainingTemplates.template') }} · {{ template.sessionCount }} sesiones</option></select></label>
          <label>Nombre del nuevo plan<input name="templatePlanName" [(ngModel)]="templatePlanName" required maxlength="160" placeholder="Ej. Fuerza · bloque inicial" /></label>
          <label>Fecha de inicio<input type="text" appDateInput name="templateStartDate" [(ngModel)]="templateStartDate" required /></label>
          <div class="full"><button type="submit" [disabled]="saving || selectedTemplateId === null">Crear plan desde plantilla</button></div>
        </form>
      </section>
      <section *ngIf="clientId === 0 && !loading" class="panel standalone-session-template-panel">
        <div class="section-heading"><div><h2>Crear plantilla de sesión</h2><p>Diseña aquí una sesión de gimnasio reutilizable, sin tener que abrir antes el expediente de un cliente ni crear una sesión en su calendario.</p></div><span class="status">{{ sessionTemplates.length }} plantillas</span></div>
        <form class="standalone-session-template-form" (ngSubmit)="saveStandaloneSessionTemplate()">
          <label>Nombre de la plantilla<input name="standaloneSessionTemplateName" [(ngModel)]="standaloneSessionTemplateDraft.name" required maxlength="160" placeholder="Ej. Torso A · hipertrofia" /></label>
          <div class="standalone-session-template-fields">
            <label>Tipo de sesión<select name="standaloneSessionTemplateType" [(ngModel)]="standaloneSessionTemplateDraft.type"><option value="strength">Fuerza / gimnasio</option><option value="endurance">Resistencia</option><option value="mobility">Movilidad</option><option value="recovery">Recuperación</option><option value="health">Salud</option><option value="general">Entrenamiento general</option><option value="other">Otro</option></select></label>
            <label>Duración (minutos)<input name="standaloneSessionTemplateDuration" type="number" [(ngModel)]="standaloneSessionTemplateDraft.duration" required min="1" max="1440" /></label>
            <label>Intensidad objetivo<input name="standaloneSessionTemplateIntensity" [(ngModel)]="standaloneSessionTemplateDraft.intensity" maxlength="120" placeholder="Ej. RPE 7 · moderada-alta" /></label>
          </div>
          <section class="exercise-library standalone-exercise-library" aria-label="Biblioteca de ejercicios para la plantilla">
            <div class="section-heading"><div><p class="eyebrow">BIBLIOTECA DE EJERCICIOS</p><h3>Construye la sesión con ejercicios del catálogo</h3><p>Busca por nombre, grupo muscular, objetivo o material y añade cada ejercicio a la prescripción.</p></div><span class="session-count">{{ filteredExercises.length }} ejercicios</span></div>
            <div class="exercise-filters">
              <label>Buscar<input name="standaloneExerciseSearch" [(ngModel)]="exerciseSearch" (ngModelChange)="loadExpandedExerciseCatalog()" placeholder="Ej. sentadilla, remo, movilidad…" /></label>
              <label>Objetivo<select name="standaloneExerciseGoal" [(ngModel)]="exerciseGoalFilter"><option value="all">Todos</option><option value="strength">Fuerza</option><option value="endurance">Resistencia</option><option value="mobility">Movilidad</option><option value="core">Core</option></select></label>
              <label>Material<select name="standaloneExerciseEquipment" [(ngModel)]="exerciseEquipmentFilter"><option value="all">Todo el material</option><option value="bodyweight">Peso corporal</option><option value="dumbbell">Mancuernas</option><option value="barbell">Barra</option><option value="machine">Máquina</option><option value="band">Bandas</option><option value="cardio">Cardio</option></select></label>
              <label>Grupo muscular<select name="standaloneExerciseMuscle" [(ngModel)]="exerciseMuscleFilter"><option value="all">Todos los grupos</option><option *ngFor="let muscle of exerciseMuscleGroups" [ngValue]="muscle">{{ muscle }}</option></select></label>
            </div>
            <div class="prescription-quick-settings" aria-label="Prescripción para el próximo ejercicio">
              <label>Series<input type="number" name="standaloneExerciseSets" [(ngModel)]="standaloneExerciseSets" min="1" max="20" /></label>
              <label>Repeticiones<input name="standaloneExerciseReps" [(ngModel)]="standaloneExerciseReps" maxlength="30" placeholder="8–12" /></label>
              <label>Descanso<input name="standaloneExerciseRest" [(ngModel)]="standaloneExerciseRest" maxlength="30" placeholder="90 s" /></label>
              <p>Estos valores se aplican al próximo ejercicio de fuerza o core; después puedes ajustarlos individualmente en la prescripción.</p>
            </div>
            <div class="exercise-results standalone-exercise-results">
              <article *ngFor="let exercise of filteredExercises.slice(0, 80)" class="exercise-result">
                <img *ngIf="exercise.imageUrl" class="exercise-thumb" [src]="exercise.imageUrl" [alt]="'Ilustración de ' + viewName(exercise, 'exercise.imageAlt')" loading="lazy" />
                <div><strong>{{ viewName(exercise, 'exercise.result') }}</strong><p>{{ exercise.muscles }} · {{ exercise.equipmentLabel }}</p><small>{{ exercise.instructions }}</small><small *ngIf="exercise.source === 'repdb'" class="exercise-source">Biblioteca RepDB</small></div>
                <button type="button" class="secondary" (click)="addExerciseToStandaloneDraft(exercise)" [disabled]="saving">＋ Añadir</button>
              </article>
              <p *ngIf="!filteredExercises.length" class="muted">No hay ejercicios con esos filtros. Prueba otra búsqueda.</p>
              <p *ngIf="filteredExercises.length > 80" class="muted">Mostrando los primeros 80 resultados. Refina la búsqueda para encontrar otros ejercicios.</p>
            </div>
            <p class="exercise-attribution">Datos e ilustraciones ampliados: <a href="https://repdb.co" target="_blank" rel="noopener noreferrer">Exercise data by RepDB</a>. Si el catálogo externo no está disponible, se muestra la biblioteca básica.</p>
          </section>
          <label>Ejercicios y prescripción<textarea name="standaloneSessionTemplateExercises" [(ngModel)]="standaloneSessionTemplateDraft.exercisePrescription" rows="7" maxlength="12000" placeholder="Añade ejercicios desde la biblioteca o escribe aquí series, repeticiones, cargas y descansos…"></textarea></label>
          <div *ngIf="muscleSetSummary(standaloneSessionTemplateDraft.exercisePrescription).length" class="muscle-set-summary"><strong>Resumen de series por grupo muscular</strong><span *ngFor="let item of muscleSetSummary(standaloneSessionTemplateDraft.exercisePrescription)">{{ item.muscle }}: {{ item.sets }} series</span><small>Cuenta las series prescritas de los ejercicios reconocidos en el catálogo; un ejercicio puede contribuir a varios grupos musculares.</small></div>
          <label>Instrucciones y notas<textarea name="standaloneSessionTemplateInstructions" [(ngModel)]="standaloneSessionTemplateDraft.instructions" rows="3" maxlength="6000" placeholder="Calentamiento, técnica, progresión, adaptaciones…"></textarea></label>
          <div class="standalone-session-template-actions"><p class="muted">La plantilla se guardará en tu biblioteca y podrás aplicarla al programar sesiones en cualquier plan que gestiones.</p><button type="submit" [disabled]="saving || !standaloneSessionTemplateDraft.name.trim() || (!standaloneSessionTemplateDraft.exercisePrescription.trim() && !standaloneSessionTemplateDraft.instructions.trim())">Guardar plantilla de sesión</button></div>
        </form>
        <div class="standalone-session-template-library">
          <h3>Mi biblioteca de plantillas de sesión</h3>
          <p *ngIf="!sessionTemplates.length" class="muted">Todavía no hay plantillas de sesión guardadas.</p>
          <article *ngFor="let template of sessionTemplates" class="template-row">
            <div><strong>{{ viewName(template, 'trainingTemplates.template') }}</strong><p>{{ sessionTypeLabel(template.sessionType) }}<span *ngIf="template.durationMinutes"> · {{ template.durationMinutes }} min</span><span *ngIf="template.intensityTarget"> · {{ template.intensityTarget }}</span></p><small *ngIf="template.exercisePrescription">{{ template.exercisePrescription }}</small><small *ngIf="template.instructions">{{ template.instructions }}</small></div>
            <button type="button" class="secondary" (click)="deleteSessionTemplate(template)" [disabled]="saving" [attr.aria-label]="'Eliminar plantilla ' + viewName(template, 'sessionTemplates.deleteLabel')">Eliminar</button>
          </article>
        </div>
      </section>
      <section *ngIf="clientId === 0 && !invitations.length && !loading" class="panel">
        <h2>No tienes invitaciones pendientes</h2>
        <p>Aquí aparecerán las invitaciones de colaboración pendientes. Para crear un plan de entrenamiento o revisar el de un cliente, abre su ficha y entra en «Planificación deportiva».</p>
        <a class="back-link" routerLink="/clients">Ir a clientes →</a>
      </section>
      <section *ngIf="invitations.length" class="panel">
        <h2>Invitaciones de colaboración</h2>
        <p>Revisa las solicitudes de acceso al entrenamiento de otros profesionales. Solo tendrás acceso de lectura.</p>
        <article *ngFor="let invitation of invitations" class="collaboration-row">
          <div><strong>{{ invitation.clientName }}</strong><p>Invita: {{ invitation.invitedBy }} · {{ invitation.createdAt | date:'mediumDate' }}</p></div>
          <button type="button" (click)="acceptInvitation(invitation)" [disabled]="saving">Aceptar invitación</button>
        </article>
      </section>
      <section *ngIf="clientId > 0 && !loading && !profile && !loadFailed" class="panel">
        <h2>Este cliente no utiliza el módulo deportivo</h2>
        <p>La planificación deportiva es opcional. Activarla no modifica el plan nutricional ni obliga al cliente a entrenar.</p>
        <button type="button" (click)="activate()" [disabled]="saving">Activar para este cliente</button>
      </section>
      <ng-container *ngIf="profile as current">
        <section class="panel">
          <div class="section-heading"><div><h2>Perfil deportivo</h2><p>El expediente nutricional permanece disponible aunque desactives este módulo.</p></div><button *ngIf="current.canManage" type="button" class="secondary" (click)="toggleEnabled()" [disabled]="saving">{{ current.enabled ? 'Desactivar módulo' : 'Reactivar módulo' }}</button></div>
          <div class="form-grid">
            <label>Objetivo principal<select [(ngModel)]="current.primaryGoal" [disabled]="!current.canManage" name="primaryGoal"><option value="endurance">Resistencia</option><option value="strength">Fuerza</option><option value="hypertrophy">Hipertrofia</option><option value="weight_loss">Pérdida de peso</option><option value="health">Salud</option><option value="general_fitness">Condición física general</option><option value="other">Otro</option></select></label>
            <label>Experiencia<select [(ngModel)]="current.experienceLevel" [disabled]="!current.canManage" name="experience"><option value="beginner">Principiante</option><option value="intermediate">Intermedio</option><option value="advanced">Avanzado</option><option value="unknown">Sin evaluar</option></select></label>
            <label>Gestión del entrenamiento<select [(ngModel)]="trainingManagementMode" [disabled]="!current.canManage" name="trainingManagementMode"><option value="self">Lo llevo yo</option><option value="collaboration">En colaboración con otro profesional</option><option value="none">Solo seguimiento nutricional</option></select></label>
            <label class="full">Notas<textarea [(ngModel)]="current.notes" [disabled]="!current.canManage" name="notes" rows="2"></textarea></label>
          </div>
          <button *ngIf="current.canManage" type="button" (click)="saveProfile()" [disabled]="saving">Guardar perfil</button>
          <p *ngIf="!current.canManage" class="muted">Acceso de colaboración: puedes consultar el plan, pero no modificarlo.</p>
        </section>
        <section class="panel" *ngIf="current.enabled && current.canManage && trainingManagementMode === 'collaboration'">
          <h2>Colaboración con otro profesional</h2>
          <p>Busca por el correo de un profesional registrado en la misma clínica. La invitación quedará disponible en su área de invitaciones de DietoExpress; el aviso por correo automático aún no está integrado. Al aceptar podrá consultar el entrenamiento, pero no modificarlo.</p>
          <form class="invite-form" (ngSubmit)="inviteCollaborator()">
            <label>Correo del profesional<input type="email" name="collaboratorEmail" [(ngModel)]="collaboratorEmail" required maxlength="254" placeholder="profesional@clinica.es" /></label>
            <button type="submit" [disabled]="saving">Crear invitación</button>
          </form>
          <p *ngIf="collaborations.length === 0" class="muted">Todavía no hay invitaciones ni colaboradores para este expediente.</p>
          <div *ngFor="let collaboration of collaborations" class="collaboration-row">
            <div><strong>{{ collaboration.professionalName }}</strong><p>{{ collaboration.email }} · <span class="status">{{ collaboration.status === 'active' ? 'Activa · solo lectura' : collaboration.status === 'pending' ? 'Pendiente de aceptación' : 'Revocada' }}</span></p></div>
            <button *ngIf="collaboration.status !== 'revoked'" type="button" class="secondary" (click)="revokeCollaboration(collaboration)" [disabled]="saving">Revocar acceso</button>
          </div>
        </section>
        <section class="panel" *ngIf="current.enabled">
          <h2>Planes de entrenamiento</h2>
          <div class="training-create-actions" *ngIf="current.canManage">
            <button type="button" class="secondary create-plan-toggle" (click)="showCreatePlanForm = !showCreatePlanForm; showQuickRoutineForm = false" [attr.aria-expanded]="showCreatePlanForm">＋ Nueva planificación</button>
            <button type="button" class="create-plan-toggle" (click)="showQuickRoutineForm = !showQuickRoutineForm; showCreatePlanForm = false" [attr.aria-expanded]="showQuickRoutineForm" [disabled]="trainingManagementMode !== 'self'">＋ Rutina sencilla</button>
          </div>
          <form *ngIf="showCreatePlanForm" (ngSubmit)="createPlan()" class="form-grid create-plan-form">
            <h3 class="full">¿Qué quieres planificar?</h3>
            <div class="full plan-type-choice">
              <button type="button" [class.active]="newPlanTarget === 'macrocycle'" (click)="newPlanTarget = 'macrocycle'">Macrociclo</button>
              <button type="button" [class.active]="newPlanTarget === 'mesocycles'" (click)="newPlanTarget = 'mesocycles'">Mesociclo</button>
            </div>
            <label>Nombre del plan<input name="planName" [(ngModel)]="planName" maxlength="160" required /></label>
            <label>Modelo de periodización<select name="periodizationModel" [(ngModel)]="periodizationModel"><option value="custom">Personalizado</option><option value="linear">Lineal</option><option value="undulating">Ondulante</option><option value="block">Bloques</option><option value="reverse">Inversa</option><option value="continuous_competition">Competición continua</option><option value="autoregulatory">Autoregulado</option></select></label>
            <label>Fecha inicial<input type="text" appDateInput name="startDate" [(ngModel)]="startDate" required /></label>
            <label>Fecha final (opcional)<input type="text" appDateInput name="endDate" [(ngModel)]="endDate" /></label>
            <label class="full">Notas<textarea name="planNotes" [(ngModel)]="planNotes" rows="2"></textarea></label>
            <div class="full"><button type="submit" [disabled]="saving">Crear {{ newPlanTarget === 'macrocycle' ? 'macrociclo' : 'mesociclo' }}</button><button type="button" class="secondary" (click)="showCreatePlanForm = false" [disabled]="saving">Cancelar</button></div>
          </form>
          <form *ngIf="showQuickRoutineForm" class="form-grid quick-routine-form" (ngSubmit)="createQuickRoutineFromTrainingPage()">
            <h3 class="full">Crear una rutina sin periodización</h3>
            <p class="full muted">Se creará un plan sencillo con su primera sesión. Podrás añadir más sesiones después, sin macrociclos ni mesociclos.</p>
            <label class="full">Nombre de la rutina<input name="quickRoutineName" [(ngModel)]="quickRoutineName" maxlength="160" required placeholder="Ej. Fuerza general" /></label>
            <label>Fecha<input type="date" name="quickRoutineDate" [(ngModel)]="quickRoutineDate" required /></label>
            <label>Tipo<select name="quickRoutineType" [(ngModel)]="quickRoutineType"><option value="strength">Fuerza / gimnasio</option><option value="endurance">Resistencia</option><option value="mobility">Movilidad</option><option value="recovery">Recuperación</option><option value="health">Salud general</option><option value="general">General</option></select></label>
            <label>Duración (minutos)<input type="number" name="quickRoutineDuration" [(ngModel)]="quickRoutineDuration" min="1" max="1440" required /></label>
            <label>Intensidad<input name="quickRoutineIntensity" [(ngModel)]="quickRoutineIntensity" maxlength="120" placeholder="Ej. Moderada · RPE 6–7" /></label>
            <label class="full">Ejercicios<textarea name="quickRoutineExercises" [(ngModel)]="quickRoutineExercises" rows="4" maxlength="6000" placeholder="Sentadilla · 3 × 8–12 · 90 s"></textarea></label>
            <label class="full">Indicaciones<textarea name="quickRoutineInstructions" [(ngModel)]="quickRoutineInstructions" rows="3" maxlength="4000"></textarea></label>
            <div class="full"><button type="submit" [disabled]="saving || trainingManagementMode !== 'self' || !quickRoutineName.trim() || !quickRoutineDate || quickRoutineDuration < 1">Crear y asignar rutina</button><button type="button" class="secondary" (click)="showQuickRoutineForm = false" [disabled]="saving">Cancelar</button></div>
          </form>
          <p *ngIf="plans.length === 0" class="muted">Todavía no hay planes de entrenamiento.</p>
          <article *ngFor="let plan of plans" class="plan-card">
            <div class="section-heading"><div><h3>{{ viewName(plan, 'plans.card') }}</h3><p>{{ plan.startDate | date:'mediumDate' }} <span *ngIf="plan.endDate">— {{ plan.endDate | date:'mediumDate' }}</span> · {{ modelLabel(plan.periodizationModel) }}</p></div><span class="status">{{ plan.status }}</span></div>
            <p *ngIf="plan.notes">{{ plan.notes }}</p>
            <div class="plan-card-actions" *ngIf="current.canManage">
              <button type="button" class="secondary" (click)="viewPlan(plan)">Ver</button>
              <button type="button" class="secondary" (click)="beginEditPlan(plan)" [disabled]="saving">Editar</button>
              <button type="button" class="secondary danger-action" (click)="deletePlan(plan)" [disabled]="saving">Eliminar</button>
            </div>
            <form *ngIf="editingPlanId === plan.id && planEditDraft as edit" class="form-grid plan-edit-form" (ngSubmit)="savePlan(plan)">
              <label>Nombre<input name="editPlanName{{plan.id}}" [(ngModel)]="edit.name" maxlength="160" required /></label>
              <label>Modelo<select name="editPlanModel{{plan.id}}" [(ngModel)]="edit.periodizationModel"><option value="custom">Personalizado</option><option value="linear">Lineal</option><option value="undulating">Ondulante</option><option value="block">Bloques</option><option value="reverse">Inversa</option><option value="continuous_competition">Competición continua</option><option value="autoregulatory">Autoregulado</option></select></label>
              <label>Fecha inicial<input type="text" appDateInput name="editPlanStart{{plan.id}}" [(ngModel)]="edit.startDate" required /></label>
              <label>Fecha final<input type="text" appDateInput name="editPlanEnd{{plan.id}}" [(ngModel)]="edit.endDate" /></label>
              <label class="full">Notas<textarea name="editPlanNotes{{plan.id}}" [(ngModel)]="edit.notes" rows="2"></textarea></label>
              <div class="full"><button type="submit" [disabled]="saving">Guardar cambios</button><button type="button" class="secondary" (click)="editingPlanId = null; planEditDraft = null" [disabled]="saving">Cancelar</button></div>
            </form>
            <nav class="training-workspace-nav" aria-label="Secciones de planificación">
              <button type="button" [class.is-active]="!activePlanWorkspace[plan.id] || activePlanWorkspace[plan.id] === 'overview'" (click)="setPlanWorkspace(plan.id, 'overview')">Resumen</button>
              <button type="button" [class.is-active]="activePlanWorkspace[plan.id] === 'macrocycle'" (click)="setPlanWorkspace(plan.id, 'macrocycle')">Macrociclo <span>{{ macrocyclePhases(plan).length }}</span></button>
              <button type="button" [class.is-active]="activePlanWorkspace[plan.id] === 'mesocycles'" (click)="setPlanWorkspace(plan.id, 'mesocycles')">Mesociclos <span>{{ mesocycles[plan.id]?.length || 0 }}</span></button>
              <button type="button" [class.is-active]="activePlanWorkspace[plan.id] === 'routines'" (click)="setPlanWorkspace(plan.id, 'routines')">Rutinas <span>{{ sessions[plan.id]?.length || 0 }}</span></button>
            </nav>
            <section *ngIf="!activePlanWorkspace[plan.id] || activePlanWorkspace[plan.id] === 'overview'" class="training-assignment-overview">
              <div><strong>Planificación asignada</strong><p>{{ macrocyclePhases(plan).length ? 'Macrociclo con ' + macrocyclePhases(plan).length + ' fases' : 'Sin macrociclo definido' }}</p></div>
              <div><strong>{{ mesocycles[plan.id]?.length || 0 }}</strong><small>mesociclos</small></div>
              <div><strong>{{ sessions[plan.id]?.length || 0 }}</strong><small>sesiones / rutinas</small></div>
              <p class="muted">Elige qué parte quieres consultar o gestionar. Cada herramienta tiene ahora su propio espacio para evitar mezclar la planificación global con el trabajo semanal y las sesiones.</p>
            </section>
                        <ng-container *ngIf="activePlanWorkspace[plan.id] === 'macrocycle'"><section class="macro-generator" *ngIf="current.canManage">
              <div class="macro-generator-heading"><div><span class="macro-kicker">PLANIFICACIÓN GLOBAL</span><h4>Diseña tu macrociclo</h4><p>La estructura y las fechas se guardan de forma independiente de los mesociclos, microciclos y sesiones.</p></div><span class="macro-generator-icon">✦</span></div>
              <div class="macro-generator-controls"><label>Duración total (semanas)<input type="number" min="3" max="52" [name]="'macroWeeks' + plan.id" [ngModel]="macroProposalWeeks[plan.id] || 12" (ngModelChange)="macroProposalWeeks[plan.id] = $event" /></label><button type="button" (click)="generateMacrocycleProposal(plan)" [disabled]="saving">✦ Generar propuesta de macrociclo</button><button *ngIf="macrocyclePlans[plan.id]?.phases?.length" type="button" class="secondary" (click)="editMacrocycle(plan)" [disabled]="saving">Editar macrociclo guardado</button></div>
              <div *ngIf="macroProposals[plan.id] as proposal" class="macro-proposal-editor">
                <div class="macro-proposal-top"><strong>Propuesta editable · {{ proposal.length }} fases</strong><span>Independiente de las rutinas</span></div>
                <article class="macro-proposal-phase" *ngFor="let phase of proposal; let phaseIndex = index">
                  <span class="macro-phase-number">{{ phase.sequence | number:'2.0' }}</span>
                  <div class="macro-proposal-fields">
                    <label>Nombre de la fase<input [name]="'macroPhaseName' + plan.id + phaseIndex" [ngModel]="viewName(phase, 'macroProposals.phaseEditor')" (ngModelChange)="updateName(phase, $event, 'macroProposals.phaseEditor')" maxlength="160" required /></label>
                    <label>Objetivo<input [name]="'macroPhaseObjective' + plan.id + phaseIndex" [(ngModel)]="phase.objective" maxlength="500" /></label>
                    <label>Tipo de fase<select [name]="'macroPhaseType' + plan.id + phaseIndex" [(ngModel)]="phase.phaseType"><option value="accumulation">Acumulación</option><option value="intensification">Intensificación</option><option value="deload">Descarga / recuperación</option><option value="realization">Realización / puesta a punto</option><option value="general">General / personalizada</option></select></label>
                    <label>Inicio<input type="date" [name]="'macroPhaseStart' + plan.id + phaseIndex" [(ngModel)]="phase.startDate" required /></label>
                    <label>Fin<input type="date" [name]="'macroPhaseEnd' + plan.id + phaseIndex" [(ngModel)]="phase.endDate" required /></label>
                  </div>
                  <button type="button" class="secondary macro-remove-phase" (click)="removeMacroPhase(plan.id, phaseIndex)" [disabled]="proposal.length <= 1 || saving" aria-label="Eliminar fase">×</button>
                </article>
                <div class="macro-proposal-actions"><button type="button" class="secondary" (click)="addMacroPhase(plan)" [disabled]="saving || proposal.length >= 24">＋ Añadir fase</button><button type="button" (click)="saveMacrocycleProposal(plan)" [disabled]="saving || !proposal.length">Guardar macrociclo</button><button type="button" class="secondary" (click)="macroProposals[plan.id] = []" [disabled]="saving">Descartar</button></div>
                <p class="macro-footnote">Guardar aquí no crea rutinas ni modifica mesociclos existentes. Los bloques de trabajo se planifican en su sección y las sesiones se generan después.</p>
              </div>
            </section>
            <section class="macro-overview" *ngIf="macrocyclePhases(plan).length">
              <div class="macro-overview-heading"><div><span class="macro-kicker">VISTA GLOBAL</span><h4>{{ macrocyclePlans[plan.id]?.name || 'Tu macrociclo' }}</h4><p>Fases de periodización independientes de los bloques de sesiones.</p></div><span class="macro-total">{{ macrocyclePhases(plan).length }} fases</span></div>
              <div class="macro-timeline" role="list" aria-label="Fases del macrociclo"><button type="button" class="macro-phase macro-phase-select" role="listitem" *ngFor="let phase of macrocyclePhases(plan)" [class.is-selected]="isMacroPhaseSelected(plan.id, phase)" [ngClass]="'phase-' + macroPhaseKind(phase)" (click)="selectMacroPhase(plan.id, phase.id)" [attr.aria-pressed]="isMacroPhaseSelected(plan.id, phase)">
                <span class="macro-phase-number">{{ phase.sequenceNumber | number:'2.0' }}</span><div class="macro-phase-content"><strong>{{ viewName(phase, 'macroProposals.timeline') }}</strong><p>{{ phase.objective || 'Objetivo por definir' }}</p><small>{{ phase.startDate | date:'dd/MM/yyyy' }} — {{ phase.endDate | date:'dd/MM/yyyy' }}</small><span class="macro-phase-duration">{{ macroPhaseDurationWeeks(phase) }} semanas</span></div>
              </button></div>
              <section class="macro-load-dashboard" style="margin-top:18px;padding:18px;border:1px solid var(--border-color,#dfe9e4);border-radius:16px;background:var(--surface-container,#f8fbf9)">
                <div class="macro-overview-heading"><div><span class="macro-kicker">CARGA DEL MACROCICLO</span><h4>Distribución semanal y por mesociclo</h4><p>Datos calculados a partir de las sesiones y prescripciones guardadas. Los datos ausentes se muestran como N/D.</p></div></div>
                <div *ngIf="macrocycleLoadWeeks(plan).length" style="display:flex;gap:10px;overflow-x:auto;padding:10px 0">
                  <article *ngFor="let week of macrocycleLoadWeeks(plan)" style="flex:1 0 112px;min-width:112px;padding:12px;border:1px solid var(--border-color,#e2ebe6);border-radius:12px;background:var(--surface-color,#fff)">
                    <strong style="font-size:.82rem">Semana {{ week.weekNumber }}</strong>
                    <small style="display:block;color:var(--secondary-text-color,#687385)">{{ week.startDate | date:'dd/MM' }} – {{ week.endDate | date:'dd/MM' }}</small>
                    <div style="margin-top:10px;font-size:.75rem">Volumen <strong style="float:right">{{ week.sets === null ? 'N/D' : week.sets + ' series' }}</strong></div>
                    <div style="height:7px;background:var(--surface-container,#edf2ef);border-radius:8px;overflow:hidden"><span style="display:block;height:100%;background:#16845b" [style.width.%]="week.volumePct"></span></div>
                    <div style="margin-top:8px;font-size:.75rem">Frecuencia <strong style="float:right">{{ week.sessions }}</strong></div>
                    <div style="height:7px;background:var(--surface-container,#edf2ef);border-radius:8px;overflow:hidden"><span style="display:block;height:100%;background:#438fca" [style.width.%]="week.frequencyPct"></span></div>
                    <div style="margin-top:8px;font-size:.75rem">Intensidad <strong style="float:right">{{ week.intensityLabel }}</strong></div>
                    <div style="height:7px;background:var(--surface-container,#edf2ef);border-radius:8px;overflow:hidden"><span style="display:block;height:100%;background:#df9d43" [style.width.%]="week.intensityPct"></span></div>
                  </article>
                </div>
                <p *ngIf="!macrocycleLoadWeeks(plan).length" class="muted">Todavía no hay semanas suficientes para calcular la carga.</p>
                <section *ngIf="macrocycleVolumeChart(plan) as volumeChart" class="macro-volume-chart" style="margin-top:22px;padding:16px;border:1px solid var(--border-color,#dfe9e4);border-radius:14px;background:var(--surface-color,#fff)">
                  <div class="macro-overview-heading"><div><span class="macro-kicker">VOLUMEN SEMANAL</span><h5 style="margin:4px 0">Series por semana y fase</h5><p>Una barra por semana, agrupada por fase. El volumen corresponde a las series de las rutinas generadas y guardadas.</p></div><strong>{{ volumeChart.hasVolume ? volumeChart.totalSets + ' series' : 'Sin datos' }}</strong></div>
                  <div *ngIf="volumeChart.hasVolume" role="img" aria-label="Gráfica de barras verticales del volumen semanal, agrupado por fase" style="display:flex;align-items:stretch;gap:14px;overflow-x:auto;padding:16px 4px 4px">
                    <section *ngFor="let phase of volumeChart.phases" style="flex:0 0 auto;min-width:0;padding:0 10px;border-left:1px solid var(--border-color,#dfe9e4)">
                      <strong style="display:block;font-size:.82rem;margin-bottom:12px">{{ phase.name }}</strong>
                      <div style="display:flex;align-items:stretch;gap:8px;height:190px">
                        <div *ngFor="let week of phase.weeks" style="width:48px;display:flex;flex-direction:column;align-items:center;justify-content:flex-end">
                          <strong style="font-size:.72rem;margin-bottom:5px">{{ week.sets === null ? 'N/D' : week.sets }}</strong>
                          <div style="height:140px;width:26px;display:flex;align-items:flex-end;background:var(--surface-container,#edf2ef);border-radius:6px 6px 0 0;overflow:hidden">
                            <span [style.height.%]="week.sets === null ? 0 : (week.sets / volumeChart.maxSets * 100)" style="display:block;width:100%;min-height:0;background:#16845b;border-radius:5px 5px 0 0" [title]="phase.name + ' · Semana ' + week.weekNumber + ': ' + (week.sets === null ? 'sin datos' : week.sets + ' series')"></span>
                          </div>
                          <small style="font-size:.68rem;margin-top:7px">S{{ week.weekNumber }}</small>
                          <small style="font-size:.62rem;color:var(--secondary-text-color,#687385)">{{ week.startDate | date:'dd/MM' }}</small>
                        </div>
                      </div>
                    </section>
                  </div>
                  <p *ngIf="!volumeChart.hasVolume" class="muted">La gráfica aparecerá cuando las rutinas generadas incluyan prescripciones de series.</p>
                </section>
                <h5 style="margin:18px 0 10px">Resumen por mesociclo</h5>
                <div *ngIf="mesocycleLoadSummary(plan).length" style="display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:10px">
                  <article *ngFor="let meso of mesocycleLoadSummary(plan)" style="padding:13px;border:1px solid var(--border-color,#e2ebe6);border-radius:12px;background:var(--surface-color,#fff)">
                    <strong>{{ meso.name }}</strong><small style="display:block;color:var(--secondary-text-color,#687385)">{{ meso.startDate | date:'dd/MM/yyyy' }} – {{ meso.endDate | date:'dd/MM/yyyy' }}</small>
                    <div style="display:flex;justify-content:space-between;margin-top:10px;font-size:.78rem"><span>Volumen</span><strong>{{ meso.sets === null ? 'N/D' : meso.sets + ' series' }}</strong></div>
                    <div style="height:8px;background:var(--surface-container,#edf2ef);border-radius:8px;overflow:hidden"><span style="display:block;height:100%;background:#16845b" [style.width.%]="meso.volumePct"></span></div>
                    <div style="display:flex;justify-content:space-between;margin-top:8px;font-size:.78rem"><span>Sesiones</span><strong>{{ meso.sessions }}</strong></div>
                    <div style="height:8px;background:var(--surface-container,#edf2ef);border-radius:8px;overflow:hidden"><span style="display:block;height:100%;background:#438fca" [style.width.%]="meso.frequencyPct"></span></div>
                    <small style="display:block;margin-top:8px;color:var(--secondary-text-color,#687385)">{{ meso.completedSessions }} realizadas · {{ meso.plannedMinutes }} min planificados</small>
                  </article>
                </div>
                <p *ngIf="!mesocycleLoadSummary(plan).length" class="muted">Los mesociclos aparecerán aquí cuando se hayan creado.</p>
              </section>
              <section *ngIf="current.canManage" style="margin-top:16px;padding:16px;border:1px solid var(--border-color,#dfe9e4);border-radius:14px">
                <div class="macro-overview-heading"><div><span class="macro-kicker">PASO 2</span><h4>Generar mesociclos y rutinas</h4><p>Usa las fases guardadas como bloques. Puedes generar todo el macrociclo de una vez o trabajar fase por fase.</p></div></div>
                <div class="form-grid">
                  <label>Objetivo<select name="macroAutoGoal{{plan.id}}" [(ngModel)]="autoGoal"><option [ngValue]="0">Forma física general</option><option [ngValue]="1">Hipertrofia</option><option [ngValue]="2">Fuerza</option></select></label>
                  <label>Nivel<select name="macroAutoExperience{{plan.id}}" [(ngModel)]="autoExperience"><option [ngValue]="0">Principiante</option><option [ngValue]="1">Intermedio</option><option [ngValue]="2">Avanzado</option></select></label>
                  <label>Distribución semanal<select name="macroAutoSplit{{plan.id}}" [(ngModel)]="autoSplit"><option [ngValue]="0">Automática (según días)</option><option [ngValue]="1">Cuerpo completo</option><option [ngValue]="2">Torso / Pierna</option><option [ngValue]="3">Push / Pull / Legs</option></select><small>Se aplicará a las rutinas de las fases que todavía no tengan planificación.</small></label>
                  <label>Duración máxima por sesión<input type="number" name="macroAutoMinutes{{plan.id}}" [(ngModel)]="autoMaximumMinutes" min="35" max="180" /></label>
                  <label>Material disponible (opcional)<input name="macroAutoEquipment{{plan.id}}" [(ngModel)]="autoEquipmentCsv" placeholder="barbell, dumbbell, machine, cable, kettlebell, pull-up-bar" /><small>Lista separada por comas. Guiones y guiones bajos son equivalentes; «machine» incluye máquinas específicas. Para restringir, usa «leg-press-machine», «hack-squat-machine» o «smith-machine». Vacío = todo el material.</small></label>
                  <fieldset class="auto-day-picker full"><legend>Días de entrenamiento</legend><div class="auto-day-options"><label *ngFor="let day of autoTrainingDayOptions"><input type="checkbox" [name]="'macroAutoDay' + plan.id + day.value" [(ngModel)]="autoTrainingDaySelection[day.value]" (ngModelChange)="syncAutoTrainingDays()" /><span>{{ day.label }}</span></label></div></fieldset>
                </div>
                <div style="display:flex;gap:10px;align-items:center;flex-wrap:wrap">
                  <button type="button" (click)="generateAllMacrocycleMesocycles(plan)" [disabled]="saving || generatingPlan || !!bulkGenerationProgress[plan.id]">Generar todos los mesociclos y rutinas</button>
                  <span *ngIf="bulkGenerationProgress[plan.id]" role="status">Generando {{ bulkGenerationProgress[plan.id].completed }}/{{ bulkGenerationProgress[plan.id].total }} · {{ bulkGenerationProgress[plan.id].phaseName }}</span>
                </div>
                <p *ngIf="generationErrors[plan.id]" class="error" role="alert">{{ generationErrors[plan.id] }}</p>
                <p class="macro-footnote">Cada mesociclo se guarda con sus microciclos y sesiones en una operación transaccional. Si uno falla, se conserva lo ya generado y podrás reanudar sin duplicarlo.</p>
              </section>
              <ng-container *ngFor="let phase of macrocyclePhases(plan)">
                <section class="macro-phase-detail" *ngIf="isMacroPhaseSelected(plan.id, phase)" [ngClass]="'phase-detail-' + macroPhaseKind(phase)">
                  <div class="macro-detail-heading"><div><span class="macro-kicker">FASE {{ phase.sequenceNumber | number:'2.0' }} · DETALLE DE CARGA</span><h5>{{ viewName(phase, 'macroProposals.detail') }}</h5><p>{{ phase.objective || 'Objetivo pendiente de definir' }}</p></div><span class="macro-detail-dates">{{ phase.startDate | date:'dd/MM/yyyy' }} — {{ phase.endDate | date:'dd/MM/yyyy' }}</span></div>
                  <div class="macro-load-legend"><span><i class="load-dot volume-dot"></i>Volumen: series prescritas</span><span><i class="load-dot frequency-dot"></i>Frecuencia: sesiones</span><span><i class="load-dot intensity-dot"></i>Intensidad: RPE si está disponible</span></div>
                  <div class="macro-phase-actions" *ngIf="current.canManage"><button type="button" (click)="generateMacrocyclePhaseMesocycle(plan, phase)" [disabled]="saving || generatingPlan || !!bulkGenerationProgress[plan.id]">Generar este mesociclo y sus rutinas</button><button type="button" class="secondary" (click)="preparePhaseRoutineBlock(plan, phase)" [disabled]="saving">Preparar bloque vacío</button></div>
                  <div class="macro-microcycle-grid">
                    <article class="macro-microcycle" *ngFor="let week of macroPhaseWeeks(plan, phase)" [class.week-deload]="week.isDeload">
                      <div class="macro-microcycle-top"><strong>Microciclo {{ week.weekNumber }}</strong><span>{{ week.sessions }} sesiones</span></div>
                      <small>{{ week.startDate | date:'dd/MM' }} — {{ week.endDate | date:'dd/MM' }}</small>
                      <div class="macro-load-row"><span>Volumen</span><strong>{{ week.sets === null ? 'N/D' : week.sets + ' series' }}</strong></div><div class="macro-load-track"><span class="macro-load-bar volume-bar" [style.width.%]="week.volumePct"></span></div>
                      <div class="macro-load-row"><span>Frecuencia</span><strong>{{ week.sessions }} sesiones</strong></div><div class="macro-load-track"><span class="macro-load-bar frequency-bar" [style.width.%]="week.frequencyPct"></span></div>
                      <div class="macro-load-row"><span>Intensidad</span><strong>{{ week.intensityLabel }}</strong></div><div class="macro-load-track"><span class="macro-load-bar intensity-bar" [style.width.%]="week.intensityPct"></span></div>
                      <p class="macro-microcycle-note">{{ week.completedSessions }} realizadas · {{ week.plannedMinutes }} min planificados</p>
                    </article>
                  </div>
                  <p class="macro-footnote">La carga se calcula con las sesiones programadas dentro de las fechas de cada microciclo. «N/D» indica que la prescripción no contiene datos suficientes; no se inventan valores. Las barras se normalizan dentro de la fase.</p>
                </section>
              </ng-container>
            </section>
            </ng-container>
            <ng-container *ngIf="activePlanWorkspace[plan.id] === 'mesocycles'"><section *ngIf="current.canManage" class="auto-generator-panel">
              <div class="auto-generator-heading"><span class="auto-generator-icon">✦</span><div><h4>Atajo: crear mesociclo y rutinas</h4><p>Este flujo genera el bloque, sus semanas y las sesiones de entrenamiento en una sola operación, con una vista previa antes de guardar.</p><small>¿Quieres planificar primero sin crear rutinas? Usa los formularios de mesociclos y microciclos de abajo; después podrás trabajar las sesiones por separado.</small></div></div>
              <div class="form-grid">
                <label>Destino de las rutinas<select name="autoRoutineTarget{{plan.id}}" [ngModel]="autoTargetMesocycleIds[plan.id] ?? null" (ngModelChange)="selectRoutineTarget(plan, $event)"><option [ngValue]="null">Crear un mesociclo nuevo</option><option *ngFor="let target of mesocycles[plan.id]" [ngValue]="target.id" [disabled]="(microcycles[target.id]?.length || 0) > 0">{{ target.sequenceNumber }}. {{ viewName(target, 'mesocycles.routineTarget') }} · {{ target.startDate | date:'dd/MM/yyyy' }} — {{ (target.endDate || target.startDate) | date:'dd/MM/yyyy' }}</option></select><small>Elige un bloque vacío para generar sus microciclos y sesiones sin crear otro mesociclo.</small></label>
                <label>Nombre del bloque (opcional)<input name="autoMesoName{{plan.id}}" [(ngModel)]="autoMesoNames[plan.id]" maxlength="160" placeholder="Ej. Hipertrofia · bloque 1" [disabled]="autoTargetMesocycleIds[plan.id] != null" /><small *ngIf="autoTargetMesocycleIds[plan.id] == null">Si lo dejas vacío, se asignará un nombre automáticamente.</small><small *ngIf="autoTargetMesocycleIds[plan.id] != null">Se conservará el nombre del mesociclo seleccionado.</small></label>
                <label>Objetivo<select name="autoGoal{{plan.id}}" [(ngModel)]="autoGoal"><option [ngValue]="0">Forma física general</option><option [ngValue]="1">Hipertrofia</option><option [ngValue]="2">Fuerza</option></select></label>
                <label>Nivel<select name="autoExperience{{plan.id}}" [(ngModel)]="autoExperience"><option [ngValue]="0">Principiante</option><option [ngValue]="1">Intermedio</option><option [ngValue]="2">Avanzado</option></select></label>
                <label>Inicio del mesociclo<input type="date" name="autoStartDate{{plan.id}}" [ngModel]="autoStartDates[plan.id] || nextMesocycleStart(plan)" (ngModelChange)="autoStartDates[plan.id] = $event" required /></label>
                <label>Duración (semanas)<input type="number" name="autoWeeks{{plan.id}}" [(ngModel)]="autoDurationWeeks" min="1" max="52" required /></label>
                <label class="auto-deload-option"><span><input type="checkbox" name="autoIsDeload{{plan.id}}" [(ngModel)]="autoIsDeload" /> Generar este bloque como descarga activa</span><small>Úsalo para un bloque/fase de descarga, preferentemente de una semana. Reduce las series aproximadamente un 40–50 % y deja más RIR.</small></label>
                <label>Distribución semanal<select name="autoSplit{{plan.id}}" [(ngModel)]="autoSplit"><option [ngValue]="0">Automática (según días)</option><option [ngValue]="1">Cuerpo completo</option><option [ngValue]="2">Torso / Pierna</option><option [ngValue]="3">Push / Pull / Legs</option></select><small>La distribución se elige independientemente del calendario, con combinaciones equilibradas: cuerpo completo 2–3 días, torso/pierna 2 o 4 y PPL 3 o 6.</small></label>
                <fieldset class="auto-day-picker full"><legend>¿Qué días entrenará?</legend><p>Elige entre 2 y 6 días. La combinación debe ser compatible con la distribución elegida.</p><div class="auto-day-options"><label *ngFor="let day of autoTrainingDayOptions"><input type="checkbox" [name]="'autoDay' + plan.id + day.value" [(ngModel)]="autoTrainingDaySelection[day.value]" (ngModelChange)="syncAutoTrainingDays()" /><span>{{ day.label }}</span></label></div></fieldset>
                <details class="auto-advanced-settings full"><summary>Opciones avanzadas de la sesión</summary><div class="auto-advanced-grid"><label>Duración máxima por sesión<input type="number" name="autoMinutes{{plan.id}}" [(ngModel)]="autoMaximumMinutes" min="35" max="180" required /><small>Entre 35 y 180 minutos.</small></label><label>Material disponible (opcional)<input name="autoEquipment{{plan.id}}" [(ngModel)]="autoEquipmentCsv" placeholder="barbell, dumbbell, machine, cable, kettlebell, pull-up-bar" /><small>Guiones y guiones bajos son equivalentes. «machine» incluye máquinas específicas; puedes usar «leg-press-machine» o «smith-machine» para restringir. Vacío = todo el material.</small></label></div></details>
                <section class="muscle-preference-editor full">
                  <h5>Prioridades musculares (opcional)</h5>
                  <p>Personaliza la dosis semanal de series efectivas estimadas. Se cuentan las contribuciones musculares relevantes de cada ejercicio, no solo la etiqueta primaria del catálogo. El objetivo explícito prevalece sobre la prioridad.</p>
                  <div class="muscle-preference-list">
                    <div *ngFor="let muscle of autoMusclePreferences" class="muscle-preference-row">
                      <label class="muscle-preference-enable"><input type="checkbox" [name]="'muscleEnabled' + plan.id + muscle.muscleGroup" [(ngModel)]="muscle.enabled" />{{ muscle.label }}</label>
                      <label>Prioridad<select [name]="'musclePriority' + plan.id + muscle.muscleGroup" [(ngModel)]="muscle.priority" [disabled]="!muscle.enabled"><option [ngValue]="0">Normal</option><option [ngValue]="1">+1</option><option [ngValue]="2">+2</option><option [ngValue]="3">+3</option></select></label>
                      <label>Objetivo semanal<input type="number" [name]="'muscleTarget' + plan.id + muscle.muscleGroup" [(ngModel)]="muscle.targetWeeklyPrimarySets" [disabled]="!muscle.enabled" min="1" max="30" placeholder="Automático" /></label>
                    </div>
                  </div>
                  <small>La prioridad añade dos series al objetivo central por nivel (máximo predeterminado 20). Para hipertrofia (grupos principales): principiante 8–12, intermedio 10–16 y avanzado 12–20. Los isquiotibiales tienen una dosis predeterminada secundaria (mitad del objetivo base, mínimo dos series), que puedes elevar con prioridad u objetivo explícito. Los objetivos priorizados o explícitos usan ±2 y los explícitos admiten 1–30 series. Si el tiempo o el catálogo no permiten alcanzar el rango, el motor informa del déficit.</small>
                </section>
                <div class="full auto-generate-action"><button type="button" aria-label="Generar mesociclo automático" (click)="previewGeneratedMesocycle(plan)" [disabled]="saving || generatingPlan">{{ generatingPlan ? 'Preparando propuesta…' : '✦ Generar propuesta de entrenamiento' }}</button><small>Se crearán {{ autoDurationWeeks }} semanas. Podrás revisar las sesiones y la cobertura muscular antes de guardar.</small></div>
              </div>
              <p *ngIf="generationErrors[plan.id]" class="error" role="alert">{{ generationErrors[plan.id] }}</p>
              <div *ngIf="generatedPreviews[plan.id] as generated" class="generated-preview">
                <div class="generated-preview-heading"><span class="preview-check">✓</span><div><h5>Tu propuesta está lista</h5><p>{{ generated.durationWeeks }} semanas · {{ generated.sessions.length }} sesiones en total</p></div></div>
                <section class="load-dashboard" aria-label="Gráficas de carga semanal">
                  <div class="load-dashboard-heading"><div><span class="macro-kicker">PLANIFICACIÓN DE LA CARGA</span><h5>Cómo evoluciona el esfuerzo</h5><p>Compara volumen, frecuencia e intensidad prevista.</p></div><span class="load-legend-dot"></span></div>
                  <div class="load-week-grid"><article class="load-week-card" *ngFor="let week of generatedLoadWeeks(generated)">
                    <div class="load-week-top"><strong>Semana {{ week.weekNumber }}</strong><span>{{ week.sessions }} sesiones</span></div>
                    <div class="load-metric"><div class="load-metric-label"><span>Volumen</span><strong>{{ week.sets }} series</strong></div><div class="load-track"><span class="load-fill load-volume" [style.width.%]="week.volumePct"></span></div></div>
                    <div class="load-metric"><div class="load-metric-label"><span>Frecuencia</span><strong>{{ week.sessions }} días</strong></div><div class="load-track"><span class="load-fill load-frequency" [style.width.%]="week.frequencyPct"></span></div></div>
                    <div class="load-metric"><div class="load-metric-label"><span>Intensidad · RIR</span><strong>{{ week.averageRir === null ? 'Sin dato' : 'RIR ' + week.averageRir }}</strong></div><div class="load-track"><span class="load-fill load-intensity" [style.width.%]="week.intensityPct"></span></div></div>
                    <div class="load-index"><span>Índice de carga orientativo</span><strong>{{ week.loadIndex }}</strong></div>
                  </article></div>
                  <p class="load-disclaimer">El índice combina series, sesiones y RIR cuando está definido. Compara semanas de esta propuesta; no sustituye al tonelaje real ni a la valoración profesional.</p>
                </section>
                <section *ngIf="generated.qualityReport" class="quality-report">
                  <strong>Chequeos orientativos de volumen y cobertura</strong>
                  <p class="muted">{{ generated.qualityReport.methodology }}</p>
                  <ul *ngIf="generated.qualityReport.warnings.length; else noQualityWarnings">
                    <li *ngFor="let warning of generated.qualityReport.warnings">{{ warning }}</li>
                  </ul>
                  <ng-template #noQualityWarnings><p>No se han detectado alertas automáticas. La revisión profesional sigue siendo obligatoria.</p></ng-template>
                  <div *ngFor="let week of generated.qualityReport.weeklyMuscleVolumes" class="volume-week-card" style="margin-top:12px;padding:14px;border:1px solid #dbe3ed;border-radius:12px;background:#fff">
                    <h6 style="margin:0 0 10px;font-size:14px">Volumen semanal · semana {{ week.weekNumber }}</h6>
                    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:8px">
                      <div *ngFor="let muscle of week.muscles" style="display:flex;justify-content:space-between;gap:10px;padding:9px 10px;border:1px solid #e7edf4;border-radius:9px">
                        <span>{{ muscleLabel(muscle.muscleGroup) }}</span><strong>{{ muscle.estimatedEffectiveSets }} series</strong>
                        <small style="color:#64748b">P {{ muscle.primarySets }} · S {{ muscle.secondarySets }} · frecuencia P {{ muscle.primaryFrequencySessions }} / S {{ muscle.secondaryFrequencySessions }}</small>
                      </div>
                    </div>
                    <p style="margin:10px 0 0;color:#64748b;font-size:11px">El total usa la auditoría de estímulo efectivo. P/S son las etiquetas originales del catálogo; una exposición puede aparecer en ambas frecuencias.</p>
                  </div>
                </section>
                <article *ngFor="let session of generated.sessions" class="generated-session">
                  <strong>Semana {{ session.weekNumber }} · {{ dayName(session.day) }} · {{ session.title }}</strong>
                  <small>{{ session.estimatedDurationMinutes }} min</small>
                  <ul><li *ngFor="let exercise of session.exercises">{{ exercise.order }}. {{ exercise.exerciseNameSnapshot }} — {{ exercise.sets }} × {{ exercise.minRepetitions }}–{{ exercise.maxRepetitions }} · descanso {{ exercise.restSeconds }} s</li></ul>
                  <div *ngIf="sessionVolume(generated, session) as volume" style="margin-top:8px;padding:10px;border:1px solid #e7edf4;border-radius:9px;background:#fff">
                    <strong style="font-size:12px">Volumen efectivo de esta sesión</strong>
                    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(135px,1fr));gap:4px;margin-top:6px">
                      <span *ngFor="let muscle of volume.muscles" [hidden]="muscle.estimatedEffectiveSets === 0" style="display:flex;justify-content:space-between;gap:8px;font-size:12px"><span>{{ muscleLabel(muscle.muscleGroup) }}</span><b>{{ muscle.estimatedEffectiveSets }}</b></span>
                    </div>
                  </div>
                </article>
                <div class="generated-preview-actions"><button type="button" (click)="saveGeneratedMesocycle(plan)" [disabled]="saving || generatingPlan">Guardar mesociclo y sus sesiones</button><button type="button" class="secondary" aria-label="Descartar vista previa" (click)="discardGeneratedPreview(plan.id)" [disabled]="saving">Descartar propuesta</button></div><p class="muted">Se guardará como borrador para que puedas ajustar las semanas y las rutinas.</p>
              </div>
            </section>
            <form *ngIf="current.canManage" class="template-save-row" (ngSubmit)="savePlanAsTemplate(plan)">
              <label>Guardar este plan como plantilla<input name="templateName{{plan.id}}" [(ngModel)]="templateNameDrafts[plan.id]" required maxlength="160" [placeholder]="viewName(plan, 'plans.templatePlaceholder') + ' · plantilla'" /></label>
              <button type="submit" class="secondary" [disabled]="saving">Guardar como plantilla</button>
            </form>
            <section class="cycle-section">
              <h4>Mesociclos</h4>
              <form *ngIf="current.canManage && mesocycleDrafts[plan.id]" class="cycle-form" (ngSubmit)="createMesocycle(plan)">
                <label>Nombre<input name="mesoName{{plan.id}}" [(ngModel)]="mesocycleDrafts[plan.id].name" required maxlength="160" /></label>
                <label>N.º<input type="number" name="mesoSeq{{plan.id}}" [(ngModel)]="mesocycleDrafts[plan.id].sequence" required min="1" /></label>
                <label>Inicio<input type="text" appDateInput name="mesoStart{{plan.id}}" [(ngModel)]="mesocycleDrafts[plan.id].startDate" required /></label>
                <label>Fin<input type="text" appDateInput name="mesoEnd{{plan.id}}" [(ngModel)]="mesocycleDrafts[plan.id].endDate" /></label>
                <label class="cycle-wide">Objetivo<input name="mesoObj{{plan.id}}" [(ngModel)]="mesocycleDrafts[plan.id].objective" maxlength="500" /></label>
                <button type="submit" [disabled]="saving">Añadir mesociclo</button>
              </form>
              <p *ngIf="!mesocycles[plan.id]?.length" class="muted">Organiza el plan en bloques de varias semanas.</p>
              <article *ngFor="let meso of mesocycles[plan.id]" class="mesocycle-card">
                <ng-container *ngIf="editingMesocycleId !== meso.id; else editMesocycleForm">
                  <div class="section-heading"><strong>{{ meso.sequenceNumber }}. {{ viewName(meso, 'mesocycles.card') }}</strong><div class="cycle-actions" *ngIf="current.canManage">
                    <button type="button" class="secondary" (click)="moveMesocycle(plan, meso, -1)" [disabled]="saving || mesocycles[plan.id]?.[0]?.id === meso.id" aria-label="Subir mesociclo">↑</button>
                    <button type="button" class="secondary" (click)="moveMesocycle(plan, meso, 1)" [disabled]="saving || mesocycles[plan.id]?.[mesocycles[plan.id].length - 1]?.id === meso.id" aria-label="Bajar mesociclo">↓</button>
                    <button type="button" class="secondary" (click)="beginEditMesocycle(meso)" [disabled]="saving">Editar</button>
                    <button type="button" class="secondary" (click)="deleteMesocycle(meso)" [disabled]="saving">Eliminar</button>
                  </div></div>
                  <p>{{ meso.startDate | date:'mediumDate' }} <span *ngIf="meso.endDate">— {{ meso.endDate | date:'mediumDate' }}</span></p>
                  <p *ngIf="meso.objective">{{ meso.objective }}</p>
                </ng-container>
                <ng-template #editMesocycleForm>
                  <form *ngIf="mesocycleEditDraft as edit" class="cycle-form" (ngSubmit)="saveMesocycle(meso)">
                    <label>Nombre<input name="editMesoName{{meso.id}}" [(ngModel)]="edit.name" required maxlength="160" /></label>
                    <label>N.º<input type="number" name="editMesoSeq{{meso.id}}" [(ngModel)]="edit.sequence" min="1" required /></label>
                    <label>Inicio<input type="text" appDateInput name="editMesoStart{{meso.id}}" [(ngModel)]="edit.startDate" required /></label>
                    <label>Fin<input type="text" appDateInput name="editMesoEnd{{meso.id}}" [(ngModel)]="edit.endDate" /></label>
                    <label class="cycle-wide">Objetivo<input name="editMesoObjective{{meso.id}}" [(ngModel)]="edit.objective" maxlength="500" /></label>
                    <label class="cycle-wide">Notas<textarea name="editMesoNotes{{meso.id}}" [(ngModel)]="edit.notes" rows="2"></textarea></label>
                    <button type="submit" [disabled]="saving">Guardar cambios</button><button type="button" class="secondary" (click)="editingMesocycleId = null; mesocycleEditDraft = null">Cancelar</button>
                  </form>
                </ng-template>
                <form *ngIf="current.canManage && microcycleDrafts[meso.id]" class="cycle-form" (ngSubmit)="createMicrocycle(meso)">
                  <label>Nombre<input name="microName{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].name" required maxlength="160" /></label>
                  <label>N.º<input type="number" name="microSeq{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].sequence" required min="1" /></label>
                  <label>Inicio<input type="text" appDateInput name="microStart{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].startDate" required /></label>
                  <label>Fin<input type="text" appDateInput name="microEnd{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].endDate" /></label>
                  <label class="cycle-wide">Enfoque<input name="microFocus{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].focus" maxlength="500" /></label>
                  <button type="submit" [disabled]="saving">Añadir microciclo</button>
                </form>
                <ul *ngIf="microcycles[meso.id]?.length"><li *ngFor="let micro of microcycles[meso.id]; let microIndex = index">
                  <ng-container *ngIf="editingMicrocycleId !== micro.id; else editMicrocycleForm">
                    <div class="section-heading"><span><strong>{{ micro.sequenceNumber }}. {{ viewName(micro, 'microcycles.card') }}</strong> · {{ micro.startDate | date:'mediumDate' }}<span *ngIf="micro.endDate"> — {{ micro.endDate | date:'mediumDate' }}</span><span *ngIf="micro.focus"> · {{ micro.focus }}</span></span>
                    <div class="cycle-actions" *ngIf="current.canManage">
                      <button type="button" class="secondary" (click)="moveMicrocycle(meso, micro, -1)" [disabled]="saving || microIndex === 0" aria-label="Subir microciclo">↑</button>
                      <button type="button" class="secondary" (click)="moveMicrocycle(meso, micro, 1)" [disabled]="saving || microIndex === microcycles[meso.id].length - 1" aria-label="Bajar microciclo">↓</button>
                      <button type="button" class="secondary" (click)="beginEditMicrocycle(micro)" [disabled]="saving">Editar</button>
                      <button type="button" class="secondary" (click)="deleteMicrocycle(micro)" [disabled]="saving">Eliminar</button>
                    </div></div>
                  </ng-container>
                  <ng-template #editMicrocycleForm>
                    <form *ngIf="microcycleEditDraft as edit" class="cycle-form" (ngSubmit)="saveMicrocycle(micro)">
                      <label>Nombre<input name="editMicroName{{micro.id}}" [(ngModel)]="edit.name" required maxlength="160" /></label>
                      <label>N.º<input type="number" name="editMicroSeq{{micro.id}}" [(ngModel)]="edit.sequence" min="1" required /></label>
                      <label>Inicio<input type="text" appDateInput name="editMicroStart{{micro.id}}" [(ngModel)]="edit.startDate" required /></label>
                      <label>Fin<input type="text" appDateInput name="editMicroEnd{{micro.id}}" [(ngModel)]="edit.endDate" /></label>
                      <label class="cycle-wide">Enfoque<input name="editMicroFocus{{micro.id}}" [(ngModel)]="edit.focus" maxlength="500" /></label>
                      <label class="cycle-wide">Notas<textarea name="editMicroNotes{{micro.id}}" [(ngModel)]="edit.notes" rows="2"></textarea></label>
                      <button type="submit" [disabled]="saving">Guardar cambios</button><button type="button" class="secondary" (click)="editingMicrocycleId = null; microcycleEditDraft = null">Cancelar</button>
                    </form>
                  </ng-template>
                </li></ul>
              </article>
            </section>
            </ng-container>
            <ng-container *ngIf="activePlanWorkspace[plan.id] === 'routines'"><section class="session-workspace">
              <div class="session-workspace-heading">
                <div><p class="eyebrow">TRABAJO DIARIO</p><h4>Calendario de entrenamiento</h4><p>Planifica la semana de un vistazo y entra en cada sesión para ajustar sus detalles.</p></div>
                <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap"><span class="session-count">{{ sessions[plan.id]?.length || 0 }} sesiones</span><button type="button" class="secondary" (click)="weeklyEditorOpen[plan.id] = !weeklyEditorOpen[plan.id]">{{ weeklyEditorOpen[plan.id] ? 'Cerrar editor semanal' : 'Editor semanal' }}</button><button type="button" class="secondary" (click)="toggleSplitReorganization(plan)">{{ splitReorganizationOpen[plan.id] ? 'Cerrar reorganización' : 'Reorganizar split' }}</button><button type="button" class="secondary" (click)="exportSessions(plan)" [disabled]="!sessions[plan.id]?.length">Exportar CSV</button></div>
              </div>
              <section *ngIf="loadTrends[plan.id]?.length" class="load-trend" style="margin:16px 0;padding:14px;border:1px solid #e2e8f0;border-radius:12px">
                <h5 style="margin:0 0 4px">Evolución de carga · últimas semanas</h5>
                <p style="margin:0 0 10px;color:#64748b;font-size:13px">Carga orientativa = minutos realizados × RPE. Es descriptiva, no una recomendación automática.</p>
                <div style="display:flex;align-items:flex-end;gap:10px;overflow-x:auto;min-height:130px;padding:8px 0">
                  <div *ngFor="let week of loadTrends[plan.id]" [title]="(week.weekStart | date:'d MMM') + ': ' + week.loadScore + ' puntos de carga'" style="min-width:42px;display:flex;flex-direction:column;align-items:center;gap:4px">
                    <strong style="font-size:12px">{{ week.loadScore | number:'1.0-0' }}</strong>
                    <span [style.height.px]="trendBarPixels(plan.id, week.loadScore)" style="display:block;width:24px;min-height:2px;background:#0d9488;border-radius:5px 5px 0 0"></span>
                    <small>{{ week.weekStart | date:'d/M' }}</small>
                  </div>
                </div>
              </section>
              <div class="calendar-toolbar">
                <div class="view-switch" role="group" aria-label="Vista de sesiones">
                  <button type="button" [class.active]="sessionViews[plan.id] !== 'list'" (click)="setSessionView(plan.id, 'week')">▦ Semana</button>
                  <button type="button" [class.active]="sessionViews[plan.id] === 'list'" (click)="setSessionView(plan.id, 'list')">☷ Lista</button>
                </div>
                <div *ngIf="sessionViews[plan.id] !== 'list'" class="week-navigation">
                  <button type="button" class="secondary" (click)="shiftWeek(plan, -1)" aria-label="Semana anterior">←</button>
                  <strong>{{ weekDays(plan)[0]?.date | date:'d MMM' }} – {{ weekDays(plan)[6]?.date | date:'d MMM y' }}</strong>
                  <button type="button" class="secondary" (click)="shiftWeek(plan, 1)" aria-label="Semana siguiente">→</button>
                  <button type="button" class="secondary" (click)="goToCurrentWeek(plan)">Semana actual</button>
                </div>
              </div>
              <div *ngIf="sessionViews[plan.id] !== 'list'" class="week-summary">
                <div><span class="summary-dot planned-dot"></span><span><strong>{{ weekSessions(plan.id, plan).length }}</strong><small>sesiones esta semana</small></span></div>
                <div><span class="summary-dot minutes-dot"></span><span><strong>{{ weekPlannedMinutes(plan.id, plan) }} min</strong><small>duración planificada</small></span></div>
                <div><span class="summary-dot completed-dot"></span><span><strong>{{ weekCompletedCount(plan.id, plan) }} / {{ weekSessions(plan.id, plan).length }}</strong><small>sesiones realizadas</small></span></div>
              </div>
              <section *ngIf="current.canManage && splitReorganizationOpen[plan.id]" class="split-reorganization-panel" style="margin:14px 0;padding:16px;border:1px solid var(--border-color,#dbe3ed);border-radius:14px;background:var(--surface-container,#f8fafc)">
                <div class="section-heading"><div><h5>Reorganizar distribución semanal</h5><p>Solo se moverán sesiones futuras sin realización registrada. No se cambian ejercicios, series, cargas ni sesiones completadas. La semana en curso queda intacta.</p></div></div>
                <div class="session-editor-grid">
                  <label>Split<select [name]="'reorgSplit' + plan.id" [(ngModel)]="splitReorganizationDrafts[plan.id].splitType" (ngModelChange)="splitReorganizationDrafts[plan.id].moves = null; splitReorganizationDrafts[plan.id].error = ''"><option [ngValue]="0">Automático</option><option [ngValue]="1">Cuerpo completo</option><option [ngValue]="2">Torso / Pierna</option><option [ngValue]="3">Push / Pull / Legs</option></select></label>
                  <fieldset class="auto-day-picker"><legend>Días de entrenamiento</legend><div class="auto-day-options"><label *ngFor="let day of autoTrainingDayOptions"><input type="checkbox" [name]="'reorgDay' + plan.id + day.value" [checked]="splitReorganizationDrafts[plan.id].trainingDays.includes(day.value)" (change)="toggleReorganizationDay(plan, day.value, $any($event.target).checked)" /><span>{{ day.label }}</span></label></div></fieldset>
                </div>
                <div class="session-editor-grid" style="margin-top:10px">
                  <label>Objetivo de la nueva propuesta<select [name]="'reorgGoal' + plan.id" [(ngModel)]="autoGoal"><option [ngValue]="0">Forma física general</option><option [ngValue]="1">Hipertrofia</option><option [ngValue]="2">Fuerza</option></select></label>
                  <label>Experiencia<select [name]="'reorgExperience' + plan.id" [(ngModel)]="autoExperience"><option [ngValue]="0">Principiante</option><option [ngValue]="1">Intermedio</option><option [ngValue]="2">Avanzado</option></select></label>
                  <label>Duración máxima (min)<input type="number" [name]="'reorgMinutes' + plan.id" [(ngModel)]="autoMaximumMinutes" min="35" max="180" /></label>
                </div>
                <p class="muted">«Preparar propuesta conservando sesiones» solo recoloca los días del mismo split y exige la misma frecuencia semanal. «Generar propuesta con este split» permite cambiar también la frecuencia: crea nuevas sesiones, conserva ajustes manuales de ejercicios que coinciden y mantiene los ejercicios personalizados no coincidentes para que puedas revisarlos. Las semanas de descarga deben reorganizarse por separado.</p>
                <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap"><button type="button" (click)="prepareSplitReorganization(plan)" [disabled]="saving">Preparar propuesta conservando sesiones</button><button type="button" class="secondary" (click)="generateSplitReplacementPreview(plan)" [disabled]="saving || splitReplacementLoading[plan.id]">{{ splitReplacementLoading[plan.id] ? 'Generando propuesta…' : 'Generar propuesta con este split' }}</button><button type="button" class="secondary" (click)="splitReorganizationDrafts[plan.id].moves = null; splitReorganizationDrafts[plan.id].error = ''; splitReplacementPreviews[plan.id] = null">Limpiar propuesta</button></div>
                <p *ngIf="splitReorganizationDrafts[plan.id].error" class="error" role="alert">{{ splitReorganizationDrafts[plan.id].error }}</p>
                <div *ngIf="splitReorganizationDrafts[plan.id].moves as moves" style="margin-top:12px">
                  <strong>Propuesta · {{ moves.length }} sesiones</strong>
                  <p *ngIf="!moves.length" class="muted">No hay sesiones futuras pendientes desde la próxima semana. Se guardará esta distribución como preferencia para las próximas rutinas.</p>
                  <div *ngIf="moves.length" style="overflow-x:auto"><table style="width:100%;border-collapse:collapse;margin-top:8px"><thead><tr><th style="text-align:left;padding:7px">Sesión</th><th style="text-align:left;padding:7px">Fecha actual</th><th style="text-align:left;padding:7px">Nueva fecha</th></tr></thead><tbody><tr *ngFor="let move of moves"><td style="padding:7px">{{ move.title }}</td><td style="padding:7px">{{ move.oldDate | date:'dd/MM/yyyy' }}</td><td style="padding:7px"><strong>{{ move.scheduledDate | date:'EEEE d MMMM' }}</strong></td></tr></tbody></table></div>
                  <p *ngIf="moves.length" class="muted">Se conservarán todas las prescripciones y el historial. Las fechas se aplicarán en una única transacción.</p>
                  <button type="button" (click)="applySplitReorganization(plan)" [disabled]="saving || splitReorganizationSaving[plan.id]">{{ splitReorganizationSaving[plan.id] ? 'Aplicando…' : 'Confirmar reorganización' }}</button>
                </div>
                <p *ngIf="splitReplacementErrors[plan.id]" class="error" role="alert">{{ splitReplacementErrors[plan.id] }}</p>
                <section *ngIf="splitReplacementPreviews[plan.id] as replacement" style="margin-top:14px;padding:14px;border:1px solid #cbd5e1;border-radius:12px;background:#fff">
                  <div class="section-heading"><div><h6>Propuesta regenerada · {{ replacement.sessions.length }} sesiones</h6><p>{{ replacement.replaceFromDate | date:'dd/MM/yyyy' }} — {{ replacement.replaceThroughDate | date:'dd/MM/yyyy' }} · {{ replacement.preservedManualEdits }} ajustes manuales coincidentes conservados</p></div></div>
                  <p *ngIf="replacement.retainedCustomExercises" class="muted">{{ replacement.retainedCustomExercises }} ejercicios personalizados que no estaban en la nueva selección se han conservado en sus sesiones ordinales. Revisa su distribución antes de confirmar.</p>
                  <ul *ngIf="replacement.warnings.length"><li *ngFor="let warning of replacement.warnings">{{ warning }}</li></ul>
                  <div *ngFor="let week of replacement.volumePreview.qualityReport.weeklyMuscleVolumes" style="margin-top:12px;padding:12px;border:1px solid #e7edf4;border-radius:10px">
                    <strong>Volumen efectivo · semana {{ week.weekNumber }}</strong>
                    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:6px;margin-top:8px">
                      <div *ngFor="let muscle of week.muscles" style="display:flex;justify-content:space-between;gap:8px;padding:5px 7px;border:1px solid #edf1f5;border-radius:7px"><span>{{ muscleLabel(muscle.muscleGroup) }}</span><b>{{ muscle.estimatedEffectiveSets }}</b></div>
                    </div>
                  </div>
                  <div *ngFor="let comparison of replacement.volumePreview.sessionReports" style="margin-top:10px;padding:9px;background:#f8fafc;border-radius:8px">
                    <strong>Semana {{ comparison.weekNumber }} · {{ dayName(comparison.day) }} · {{ comparison.title }}</strong>
                    <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(140px,1fr));gap:4px;margin-top:5px">
                      <span *ngFor="let muscle of comparison.qualityReport.weeklyMuscleVolumes[0]?.muscles" [hidden]="muscle.estimatedEffectiveSets === 0" style="display:flex;justify-content:space-between;gap:8px;font-size:12px"><span>{{ muscleLabel(muscle.muscleGroup) }}</span><b>{{ muscle.estimatedEffectiveSets }}</b></span>
                    </div>
                  </div>
                  <p *ngIf="replacement.volumePreview.unclassifiedExerciseNames.length" class="muted">No clasificados: {{ replacement.volumePreview.unclassifiedExerciseNames.join(', ') }}</p>
                  <div style="display:flex;gap:8px;align-items:center;flex-wrap:wrap;margin-top:12px"><button type="button" (click)="applySplitReplacement(plan)" [disabled]="saving || splitReplacementLoading[plan.id]">Sustituir sesiones futuras con esta propuesta</button><button type="button" class="secondary" (click)="splitReplacementPreviews[plan.id] = null">Descartar</button></div>
                  <p class="muted">La sustitución solo afecta al periodo futuro mostrado. Las sesiones completadas y sus registros no se borran ni se modifican. Los ajustes manuales coincidentes se incorporan a la propuesta.</p>
                </section>
              </section>
              <section *ngIf="current.canManage && weeklyEditorOpen[plan.id] && sessionViews[plan.id] !== 'list'" class="weekly-structured-editor" style="margin:14px 0;padding:16px;border:1px solid var(--border-color,#dbe3ed);border-radius:14px;background:var(--surface-container,#f8fafc)">
                <div class="section-heading"><div><h5>Editor semanal estructurado</h5><p>Modifica cada sesión y sus ejercicios sin editar líneas de texto. Las sesiones completadas se muestran como solo lectura.</p></div><span class="session-count">{{ weekSessions(plan.id, plan).length }} sesiones</span></div>
                <article *ngFor="let session of weekSessions(plan.id, plan)" style="padding:12px;margin-top:10px;border:1px solid #e2e8f0;border-radius:12px;background:#fff">
                  <div style="display:flex;justify-content:space-between;gap:10px;align-items:center;flex-wrap:wrap"><strong>{{ session.title }}</strong><span class="week-session-status">{{ session.completedAt ? 'Realizada · protegida' : 'Pendiente de revisión' }}</span></div>
                  <ng-container *ngIf="!session.completedAt && weeklyDraft(session) as weekly">
                    <div class="session-editor-grid" style="margin-top:10px">
                      <label>Fecha<input type="date" [name]="'weeklyDate' + session.id" [(ngModel)]="weekly.date" (ngModelChange)="onWeeklyDraftChanged(plan, session)" [min]="plan.startDate" [max]="plan.endDate || null" /></label>
                      <label>Nombre de sesión<input [name]="'weeklyTitle' + session.id" [(ngModel)]="weekly.title" maxlength="160" (ngModelChange)="onWeeklyDraftChanged(plan, session)" /></label>
                      <label>Duración (min)<input type="number" [name]="'weeklyDuration' + session.id" [(ngModel)]="weekly.duration" min="1" max="1440" /></label>
                      <label>Tipo<select [name]="'weeklyType' + session.id" [(ngModel)]="weekly.type"><option value="strength">Fuerza</option><option value="endurance">Resistencia</option><option value="mobility">Movilidad</option><option value="recovery">Recuperación</option><option value="health">Salud</option><option value="general">General</option><option value="other">Otro</option></select></label>
                      <label>Intensidad / RIR objetivo<input [name]="'weeklyIntensity' + session.id" [(ngModel)]="weekly.intensity" maxlength="120" /></label>
                      <label class="session-editor-wide">Instrucciones<textarea [name]="'weeklyInstructions' + session.id" [(ngModel)]="weekly.instructions" rows="2" maxlength="4000"></textarea></label>
                    </div>
                    <div style="margin-top:12px"><strong>Ejercicios de la sesión</strong>
                      <div *ngFor="let exercise of weekly.exercises; let exerciseIndex = index" style="display:grid;grid-template-columns:minmax(160px,2fr) repeat(4,minmax(75px,0.7fr)) auto;gap:7px;align-items:end;padding:9px 0;border-bottom:1px solid #edf1f5">
                        <label>Ejercicio<input [name]="'weeklyExerciseName' + session.id + exerciseIndex" [(ngModel)]="exercise.exerciseName" (ngModelChange)="onWeeklyDraftChanged(plan, session)" maxlength="160" placeholder="Nombre del ejercicio" /></label>
                        <label>Series<input type="number" [name]="'weeklyExerciseSets' + session.id + exerciseIndex" [(ngModel)]="exercise.sets" (ngModelChange)="onWeeklyDraftChanged(plan, session)" min="1" max="10" /></label>
                        <label>Reps<input [name]="'weeklyExerciseReps' + session.id + exerciseIndex" [(ngModel)]="exercise.repetitions" (ngModelChange)="onWeeklyDraftChanged(plan, session)" maxlength="30" placeholder="8–12" /></label>
                        <label>RIR<input type="number" [name]="'weeklyExerciseRir' + session.id + exerciseIndex" [(ngModel)]="exercise.rir" (ngModelChange)="onWeeklyDraftChanged(plan, session)" min="0" max="10" step="0.5" /></label>
                        <label>Descanso (s)<input type="number" [name]="'weeklyExerciseRest' + session.id + exerciseIndex" [(ngModel)]="exercise.restSeconds" (ngModelChange)="onWeeklyDraftChanged(plan, session)" min="0" max="3600" /></label>
                        <div style="display:flex;gap:3px"><button type="button" class="secondary" (click)="moveWeeklyExercise(plan, session, exerciseIndex, -1)" [disabled]="exerciseIndex === 0" aria-label="Subir ejercicio">↑</button><button type="button" class="secondary" (click)="moveWeeklyExercise(plan, session, exerciseIndex, 1)" [disabled]="exerciseIndex === weekly.exercises.length - 1" aria-label="Bajar ejercicio">↓</button><button type="button" class="secondary" (click)="removeWeeklyExercise(plan, session, exerciseIndex)" aria-label="Eliminar ejercicio">×</button></div>
                      </div>
                      <button type="button" class="secondary" style="margin-top:10px" (click)="addWeeklyExercise(plan, session)">＋ Añadir ejercicio</button>
                    </div>
                    <div style="display:flex;justify-content:flex-end;margin-top:12px"><button type="button" (click)="saveWeeklySession(plan, session)" [disabled]="saving">Guardar esta sesión</button></div>
                  </ng-container>
                  <p *ngIf="session.completedAt" class="muted">La prescripción, fecha e historial de realización se mantienen intactos.</p>
                </article>
                <p *ngIf="!weekSessions(plan.id, plan).length" class="muted">No hay sesiones en esta semana. Programa una sesión para comenzar.</p>
              </section>
              <div *ngIf="sessions[plan.id]?.length && sessionViews[plan.id] !== 'list'" class="week-grid">
                <article *ngFor="let day of weekDays(plan)" class="week-day" [class.today]="day.isToday" [class.day-has-sessions]="sessionsForDay(plan.id, day.date).length">
                  <header class="week-day-heading"><span>{{ day.date | date:'EEE' }}</span><strong>{{ day.date | date:'d' }}</strong></header>
                  <div *ngIf="sessionsForDay(plan.id, day.date).length === 0" class="week-day-empty">—</div>
                  <article *ngFor="let session of sessionsForDay(plan.id, day.date)" class="week-session" [ngClass]="'week-' + session.sessionType" [class.week-session-done]="!!session.completedAt">
                    <span class="week-session-type">{{ sessionTypeIcon(session.sessionType) }}</span>
                    <strong>{{ session.title }}</strong>
                    <span class="week-session-meta">{{ session.durationMinutes || '—' }} min · {{ sessionTypeLabel(session.sessionType) }}</span>
                    <span class="week-session-status">{{ session.completedAt ? '✓ Realizada' : '○ Programada' }}</span>
                    <div class="week-session-actions">
                      <button *ngIf="current.canManage" type="button" (click)="editSessionFromCalendar(session)" [disabled]="saving" [attr.aria-label]="'Editar ' + session.title">Editar</button>
                      <button *ngIf="current.canManage" type="button" (click)="recordSessionFromCalendar(session)" [disabled]="saving">{{ session.completedAt ? 'Registro' : 'Realizar' }}</button>
                    </div>
                  </article>
                </article>
              </div>
              <p *ngIf="!sessions[plan.id]?.length && sessionViews[plan.id] !== 'list'" class="session-empty">Tu calendario está vacío. Programa una sesión para empezar; aparecerá automáticamente en su día.</p>
              <form *ngIf="current.canManage" (ngSubmit)="createSession(plan)" class="session-editor">
                <div class="editor-title"><span class="editor-icon">＋</span><div><strong>Programar sesión</strong><p>Añade una sesión al calendario de este plan.</p></div></div>
                <section class="saved-session-templates" aria-label="Plantillas de sesión">
                  <div class="saved-session-heading"><div><strong>Plantillas de sesiones</strong><p>Guarda una sesión de gimnasio y reutilízala en cualquier plan que gestiones.</p></div><span class="session-count">{{ sessionTemplates.length }}</span></div>
                  <div class="saved-session-controls">
                    <label>Aplicar plantilla<select name="savedSessionTemplate{{plan.id}}" [(ngModel)]="selectedSessionTemplateIds[plan.id]"><option [ngValue]="null">Selecciona una plantilla…</option><option *ngFor="let template of sessionTemplates" [ngValue]="template.id">{{ viewName(template, 'sessionTemplates.template') }} · {{ sessionTypeLabel(template.sessionType) }}</option></select></label>
                    <button type="button" class="secondary" (click)="applySavedSessionTemplate(plan)" [disabled]="saving || !selectedSessionTemplateIds[plan.id]">Aplicar</button>
                    <label>Guardar borrador como plantilla<input name="sessionTemplateName{{plan.id}}" [(ngModel)]="sessionTemplateNameDrafts[plan.id]" maxlength="160" placeholder="Ej. Torso A · hipertrofia" /></label>
                    <button type="button" (click)="saveSessionAsTemplate(plan)" [disabled]="saving || !sessionTemplateNameDrafts[plan.id]?.trim()">Guardar plantilla</button>
                  </div>
                  <div *ngIf="sessionTemplates.length" class="saved-session-list">
                    <span *ngFor="let template of sessionTemplates" class="saved-session-chip"><strong>{{ viewName(template, 'sessionTemplates.template') }}</strong><small>{{ sessionTypeLabel(template.sessionType) }}<span *ngIf="template.durationMinutes"> · {{ template.durationMinutes }} min</span></small><button type="button" class="secondary" (click)="deleteSessionTemplate(template)" [disabled]="saving" [attr.aria-label]="'Eliminar plantilla ' + viewName(template, 'sessionTemplates.deleteLabel')">×</button></span>
                  </div>
                  <p *ngIf="!sessionTemplates.length" class="muted">Aún no tienes plantillas de sesión. Rellena los ejercicios y las instrucciones, ponle un nombre y guárdala aquí.</p>
                </section>
                <section class="exercise-library" aria-label="Biblioteca de ejercicios">
                  <div class="section-heading"><div><p class="eyebrow">BIBLIOTECA</p><h5>Construye la sesión</h5><p>Busca ejercicios, filtra por objetivo o material y añádelos con un toque.</p></div><span class="session-count">{{ filteredExercises.length }} ejercicios</span></div>
                  <div class="exercise-filters">
                    <label>Buscar ejercicio<input name="exerciseSearch{{plan.id}}" [(ngModel)]="exerciseSearch" placeholder="Ej. sentadilla, remo, movilidad…" /></label>
                    <label>Objetivo<select name="exerciseGoal{{plan.id}}" [(ngModel)]="exerciseGoalFilter"><option value="all">Todos</option><option value="strength">Fuerza</option><option value="endurance">Resistencia</option><option value="mobility">Movilidad</option><option value="core">Core</option></select></label>
                    <label>Material<select name="exerciseEquipment{{plan.id}}" [(ngModel)]="exerciseEquipmentFilter"><option value="all">Todo el material</option><option value="bodyweight">Peso corporal</option><option value="dumbbell">Mancuernas</option><option value="barbell">Barra</option><option value="machine">Máquina</option><option value="band">Bandas</option><option value="cardio">Cardio</option></select></label>
                    <label>Grupo muscular<select name="exerciseMuscle{{plan.id}}" [(ngModel)]="exerciseMuscleFilter"><option value="all">Todos los grupos</option><option *ngFor="let muscle of exerciseMuscleGroups" [ngValue]="muscle">{{ muscle }}</option></select></label>
                  </div>
                  <div class="prescription-quick-settings" aria-label="Prescripción para el próximo ejercicio">
                    <label>Series<input type="number" name="sessionExerciseSets{{plan.id}}" [(ngModel)]="sessionExerciseSets" min="1" max="20" /></label>
                    <label>Repeticiones<input name="sessionExerciseReps{{plan.id}}" [(ngModel)]="sessionExerciseReps" maxlength="30" placeholder="8–12" /></label>
                    <label>Descanso<input name="sessionExerciseRest{{plan.id}}" [(ngModel)]="sessionExerciseRest" maxlength="30" placeholder="90 s" /></label>
                    <p>Se aplican al próximo ejercicio de fuerza o core; luego puedes modificar cada línea.</p>
                  </div>
                  <div class="exercise-results">
                    <article *ngFor="let exercise of filteredExercises" class="exercise-result">
                      <img *ngIf="exercise.imageUrl" class="exercise-thumb" [src]="exercise.imageUrl" [alt]="'Ilustración de ' + viewName(exercise, 'exercise.imageAlt')" loading="lazy" />
                      <div><strong>{{ viewName(exercise, 'exercise.result') }}</strong><p>{{ exercise.muscles }} · {{ exercise.equipmentLabel }}</p><small>{{ exercise.instructions }}</small><small *ngIf="exercise.source === 'repdb'" class="exercise-source">Biblioteca RepDB</small></div>
                      <button type="button" class="secondary" (click)="addExerciseToDraft(plan, exercise)" [disabled]="saving">＋ Añadir</button>
                    </article>
                    <p class="exercise-attribution">Ilustraciones y datos ampliados: <a href="https://repdb.co" target="_blank" rel="noopener noreferrer">Exercise data by RepDB (repdb.co)</a>. Los ejercicios importados se cargan desde su catálogo público; si no está disponible, se mantiene la biblioteca básica.</p>
                    <p *ngIf="!filteredExercises.length" class="muted">No hay ejercicios con esos filtros. Prueba otra búsqueda.</p>
                  </div>
                  <div class="session-tools">
                    <label>Reutilizar sesión guardada<select name="sessionTemplate{{plan.id}}" [(ngModel)]="templateSessionId"><option [ngValue]="null">Elegir sesión como plantilla…</option><option *ngFor="let saved of sessions[plan.id]" [ngValue]="saved.id">{{ saved.title }} · {{ saved.scheduledDate | date:'shortDate' }}</option></select></label>
                    <button type="button" class="secondary" (click)="applySessionTemplate(plan)" [disabled]="!templateSessionId">Usar como plantilla</button>
                    <label>Generación asistida<select name="generatorGoal{{plan.id}}" [(ngModel)]="generatorGoal"><option value="strength">Fuerza general</option><option value="hypertrophy">Hipertrofia</option><option value="endurance">Resistencia</option><option value="mobility">Movilidad y recuperación</option><option value="general">Condición física general</option></select></label>
                    <label>Nivel<select name="generatorLevel{{plan.id}}" [(ngModel)]="generatorLevel"><option value="beginner">Principiante</option><option value="intermediate">Intermedio</option><option value="advanced">Avanzado</option></select></label>
                    <label>Material disponible<select name="generatorEquipment{{plan.id}}" [(ngModel)]="generatorEquipment"><option value="mixed">Variado</option><option value="bodyweight">Solo peso corporal</option><option value="dumbbell">Mancuernas</option><option value="barbell">Barra y discos</option><option value="band">Bandas elásticas</option><option value="cardio">Cardio</option></select></label>
                    <button type="button" (click)="generateSessionDraft(plan)" [disabled]="saving">✦ Proponer sesión</button>
                  </div>
                  <p class="muted">La propuesta es un borrador editable, no una prescripción clínica ni una recomendación automática de carga. Revísala antes de guardarla.</p>
                </section>
                <div class="session-editor-grid">
                  <label>Nombre de la sesión<input name="sessionTitle{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].title" maxlength="160" placeholder="Ej. Fuerza de tren inferior" required /></label>
                  <label>Fecha<input type="text" appDateInput name="sessionDate{{plan.id}}" [ngModel]="sessionDrafts[plan.id].date" (ngModelChange)="onManualSessionDateChanged(plan, $event)" [min]="plan.startDate" [max]="plan.endDate || null" required /></label>
                  <label>Tipo de entrenamiento<select name="sessionType{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].type"><option value="endurance">Resistencia</option><option value="strength">Fuerza</option><option value="mobility">Movilidad</option><option value="recovery">Recuperación</option><option value="health">Salud</option><option value="general">General</option><option value="other">Otro</option></select></label>
                  <label>Duración estimada (min)<input type="number" name="sessionDuration{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].duration" min="1" max="1440" placeholder="60" /></label>
                  <label>Intensidad / zona objetivo<input name="sessionIntensity{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].intensity" maxlength="120" placeholder="Ej. RPE 7 · Z2 · 250 W · 5:00/km" /></label>
                  <label class="session-editor-wide">Instrucciones<textarea name="sessionInstructions{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].instructions" rows="2" maxlength="4000" placeholder="Calentamiento, bloques principales, pausas y vuelta a la calma…"></textarea></label>
                  <label class="session-editor-wide">Prescripción de ejercicios<textarea name="sessionExercises{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].exercisePrescription" (ngModelChange)="onManualPrescriptionChanged(plan)" rows="3" maxlength="6000" placeholder="Sentadilla · 3 × 8 · descanso 2 min · RPE 7&#10;Peso muerto rumano · 3 × 10 · descanso 90 s"></textarea><small>Indica ejercicio, series, repeticiones, descanso y observaciones. Puedes anotar ritmo o potencia en la zona objetivo.</small></label>
                  <div *ngIf="prescriptionLines(sessionDrafts[plan.id].exercisePrescription).length" class="prescription-line-editor session-editor-wide" style="display:grid;gap:7px;margin:8px 0">
                    <div *ngFor="let line of prescriptionLines(sessionDrafts[plan.id].exercisePrescription); let lineIndex = index" class="prescription-line-row" style="display:grid;grid-template-columns:minmax(0,1fr) repeat(3,auto);gap:6px;align-items:center">
                      <input [name]="'draftExerciseLine' + plan.id + lineIndex" [ngModel]="line" (ngModelChange)="updatePrescriptionLine(sessionDrafts[plan.id], lineIndex, $event, plan)" aria-label="Prescripción del ejercicio" />
                      <button type="button" class="secondary" (click)="movePrescriptionLine(sessionDrafts[plan.id], lineIndex, -1, plan)" [disabled]="lineIndex === 0" aria-label="Subir ejercicio">↑</button>
                      <button type="button" class="secondary" (click)="movePrescriptionLine(sessionDrafts[plan.id], lineIndex, 1, plan)" [disabled]="lineIndex === prescriptionLines(sessionDrafts[plan.id].exercisePrescription).length - 1" aria-label="Bajar ejercicio">↓</button>
                      <button type="button" class="secondary" (click)="removePrescriptionLine(sessionDrafts[plan.id], lineIndex, plan)" aria-label="Quitar ejercicio">×</button>
                    </div>
                  </div>
                  <section class="manual-volume-dashboard session-editor-wide" style="padding:14px;border:1px solid #dbe3ed;border-radius:12px;background:#fff">
                    <div style="display:flex;justify-content:space-between;gap:12px;align-items:center;flex-wrap:wrap"><strong>Volumen muscular efectivo estimado · semana</strong><span *ngIf="manualVolumePreviewLoading[plan.id]" role="status">Calculando…</span></div>
                    <p *ngIf="manualVolumePreviewErrors[plan.id]" class="muted" role="status">{{ manualVolumePreviewErrors[plan.id] }}</p>
                    <div *ngIf="manualVolumePreviews[plan.id] as volume">
                      <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:7px;margin-top:10px">
                        <div *ngFor="let muscle of volume.qualityReport.weeklyMuscleVolumes[0]?.muscles" style="display:flex;justify-content:space-between;gap:8px;padding:8px 10px;border:1px solid #e7edf4;border-radius:9px">
                          <span>{{ muscleLabel(muscle.muscleGroup) }}</span><strong>{{ muscle.estimatedEffectiveSets }} series</strong>
                          <small style="grid-column:1 / -1;color:#64748b">Primarias {{ muscle.primarySets }} · secundarias {{ muscle.secondarySets }} · frecuencia P {{ muscle.primaryFrequencySessions }} / S {{ muscle.secondaryFrequencySessions }}</small>
                        </div>
                      </div>
                      <section *ngIf="volume.sessionReports?.length" style="margin-top:14px">
                        <strong>Comparación por sesión</strong>
                        <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:9px;margin-top:8px">
                          <article *ngFor="let comparison of volume.sessionReports" style="padding:10px;border:1px solid #e7edf4;border-radius:10px">
                            <strong>{{ dayName(comparison.day) }} · {{ comparison.title }}</strong>
                            <div *ngFor="let muscle of comparison.qualityReport.weeklyMuscleVolumes[0]?.muscles" style="display:flex;justify-content:space-between;gap:8px;font-size:12px;padding:3px 0">
                              <span>{{ muscleLabel(muscle.muscleGroup) }}</span><b>{{ muscle.estimatedEffectiveSets }}</b>
                            </div>
                            <small *ngIf="comparison.unclassifiedExerciseNames.length" class="muted">Sin clasificar: {{ comparison.unclassifiedExerciseNames.join(', ') }}</small>
                          </article>
                        </div>
                      </section>
                      <ul *ngIf="volume.qualityReport.warnings.length" style="margin:8px 0;padding-left:18px"><li *ngFor="let warning of volume.qualityReport.warnings">{{ warning }}</li></ul>
                      <p *ngIf="volume.unclassifiedExerciseNames.length" class="muted" style="margin:10px 0 0">Sin volumen calculable por falta de metadatos revisados: {{ volume.unclassifiedExerciseNames.join(', ') }}. No se inventa su contribución.</p>
                      <small>Se usa el mismo motor de estímulo auditado que en la generación automática. Incluye las sesiones programadas de esta semana y el borrador actual.</small>
                    </div>
                    <p *ngIf="!manualVolumePreviews[plan.id] && !manualVolumePreviewLoading[plan.id] && !manualVolumePreviewErrors[plan.id]" class="muted">Añade ejercicios reconocidos para calcular el volumen de la semana.</p>
                  </section>
                </div>
                <div class="editor-footer"><span>La fecha debe estar dentro del periodo del plan.</span><button type="submit" [disabled]="saving || !sessionDrafts[plan.id].title.trim()">＋ Añadir sesión</button></div>
              </form>
              <p *ngIf="!sessions[plan.id]?.length" class="session-empty">Todavía no hay sesiones. Programa la primera arriba para empezar a construir el calendario.</p>
              <div *ngIf="sessions[plan.id]?.length && sessionViews[plan.id] === 'list'" class="session-list">
                <article *ngFor="let session of sessions[plan.id]" class="session-card" [class.session-completed]="!!session.completedAt">
                  <div class="session-date-rail"><strong>{{ session.scheduledDate | date:'dd' }}</strong><span>{{ session.scheduledDate | date:'MMM' }}</span></div>
                  <div class="session-card-main">
                    <div class="session-card-top">
                      <span class="session-type-icon" [ngClass]="'type-' + session.sessionType">{{ sessionTypeIcon(session.sessionType) }}</span>
                      <div class="session-title-block"><div class="session-title-line"><h5>{{ session.title }}</h5><span class="session-status" [class.done]="!!session.completedAt">{{ session.completedAt ? 'Realizada' : 'Programada' }}</span></div><p>{{ sessionTypeLabel(session.sessionType) }} <span *ngIf="session.durationMinutes">· {{ session.durationMinutes }} min</span><span *ngIf="session.intensityTarget"> · {{ session.intensityTarget }}</span></p></div>
                      <button *ngIf="current.canManage" type="button" class="secondary session-edit-button" (click)="beginEditSession(session)" [disabled]="saving">{{ editingSessionId === session.id ? 'Cerrar edición' : 'Editar' }}</button>
                    </div>
                    <p *ngIf="session.instructions" class="session-instructions">{{ session.instructions }}</p>
                    <div *ngIf="session.exercisePrescription" class="exercise-prescription"><strong>Ejercicios, series y descansos</strong><p style="white-space:pre-line">{{ session.exercisePrescription }}</p></div>
                    <div *ngIf="session.completedAt" class="completion-summary"><span class="completion-check">✓</span><span><strong>Realización registrada</strong><small>{{ session.actualDurationMinutes || '—' }} min reales · RPE {{ session.sessionRpe ?? '—' }} / 10</small></span><span *ngIf="session.durationMinutes && session.actualDurationMinutes" class="duration-delta">{{ session.actualDurationMinutes - session.durationMinutes > 0 ? '+' : '' }}{{ session.actualDurationMinutes - session.durationMinutes }} min</span></div>
                    <form *ngIf="current.canManage && editingSessionId === session.id && sessionEditDraft as edit" class="session-edit-form" (ngSubmit)="saveSession(session)">
                      <div class="session-editor-grid">
                        <label>Nombre<input name="editSessionTitle{{session.id}}" [(ngModel)]="edit.title" maxlength="160" required /></label>
                        <label>Fecha<input type="text" appDateInput name="editSessionDate{{session.id}}" [ngModel]="edit.date" (ngModelChange)="onEditedSessionDateChanged(plan, session, $event)" [min]="plan.startDate" [max]="plan.endDate || null" required /></label>
                        <label>Tipo<select name="editSessionType{{session.id}}" [(ngModel)]="edit.type"><option value="endurance">Resistencia</option><option value="strength">Fuerza</option><option value="mobility">Movilidad</option><option value="recovery">Recuperación</option><option value="health">Salud</option><option value="general">General</option><option value="other">Otro</option></select></label>
                        <label>Duración (min)<input type="number" name="editSessionDuration{{session.id}}" [(ngModel)]="edit.duration" min="1" max="1440" required /></label>
                        <label>Intensidad / zona<input name="editSessionIntensity{{session.id}}" [(ngModel)]="edit.intensity" maxlength="120" /></label>
                        <label class="session-editor-wide">Instrucciones<textarea name="editSessionInstructions{{session.id}}" [(ngModel)]="edit.instructions" rows="3" maxlength="4000"></textarea></label>
                        <label class="session-editor-wide">Prescripción de ejercicios<textarea name="editSessionExercises{{session.id}}" [ngModel]="edit.exercisePrescription" (ngModelChange)="onEditedSessionPrescriptionChanged(plan, session, $event)" rows="4" maxlength="6000" placeholder="Ejercicio · series × repeticiones · descanso · notas"></textarea></label>
                        <div *ngIf="prescriptionLines(edit.exercisePrescription).length" class="prescription-line-editor session-editor-wide" style="display:grid;gap:7px;margin:8px 0">
                          <div *ngFor="let line of prescriptionLines(edit.exercisePrescription); let lineIndex = index" class="prescription-line-row" style="display:grid;grid-template-columns:minmax(0,1fr) repeat(3,auto);gap:6px;align-items:center">
                            <input [name]="'editExerciseLine' + session.id + lineIndex" [ngModel]="line" (ngModelChange)="updatePrescriptionLine(edit, lineIndex, $event, plan)" aria-label="Prescripción del ejercicio" />
                            <button type="button" class="secondary" (click)="movePrescriptionLine(edit, lineIndex, -1, plan)" [disabled]="lineIndex === 0" aria-label="Subir ejercicio">↑</button>
                            <button type="button" class="secondary" (click)="movePrescriptionLine(edit, lineIndex, 1, plan)" [disabled]="lineIndex === prescriptionLines(edit.exercisePrescription).length - 1" aria-label="Bajar ejercicio">↓</button>
                            <button type="button" class="secondary" (click)="removePrescriptionLine(edit, lineIndex, plan)" aria-label="Quitar ejercicio">×</button>
                          </div>
                        </div>
                        <section class="manual-volume-dashboard session-editor-wide" style="padding:14px;border:1px solid #dbe3ed;border-radius:12px;background:#fff">
                          <div style="display:flex;justify-content:space-between;gap:12px;align-items:center;flex-wrap:wrap"><strong>Volumen muscular efectivo estimado · semana</strong><span *ngIf="manualVolumePreviewLoading[plan.id]" role="status">Calculando…</span></div>
                          <p *ngIf="manualVolumePreviewErrors[plan.id]" class="muted" role="status">{{ manualVolumePreviewErrors[plan.id] }}</p>
                          <div *ngIf="manualVolumePreviews[plan.id] as volume">
                            <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(190px,1fr));gap:7px;margin-top:10px">
                              <div *ngFor="let muscle of volume.qualityReport.weeklyMuscleVolumes[0]?.muscles" style="display:flex;justify-content:space-between;gap:8px;padding:8px 10px;border:1px solid #e7edf4;border-radius:9px">
                                <span>{{ muscleLabel(muscle.muscleGroup) }}</span><strong>{{ muscle.estimatedEffectiveSets }} series</strong>
                              </div>
                            </div>
                            <section *ngIf="volume.sessionReports?.length" style="margin-top:14px">
                              <strong>Comparación por sesión</strong>
                              <div style="display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:9px;margin-top:8px">
                                <article *ngFor="let comparison of volume.sessionReports" style="padding:10px;border:1px solid #e7edf4;border-radius:10px">
                                  <strong>{{ dayName(comparison.day) }} · {{ comparison.title }}</strong>
                                  <div *ngFor="let muscle of comparison.qualityReport.weeklyMuscleVolumes[0]?.muscles" style="display:flex;justify-content:space-between;gap:8px;font-size:12px;padding:3px 0">
                                    <span>{{ muscleLabel(muscle.muscleGroup) }}</span><b>{{ muscle.estimatedEffectiveSets }}</b>
                                  </div>
                                  <small *ngIf="comparison.unclassifiedExerciseNames.length" class="muted">Sin clasificar: {{ comparison.unclassifiedExerciseNames.join(', ') }}</small>
                                </article>
                              </div>
                            </section>
                            <p *ngIf="volume.unclassifiedExerciseNames.length" class="muted" style="margin:10px 0 0">Sin volumen calculable por falta de metadatos revisados: {{ volume.unclassifiedExerciseNames.join(', ') }}.</p>
                            <small>El cálculo incluye el resto de sesiones de esta semana y esta edición, con el mismo motor que la generación automática.</small>
                          </div>
                          <p *ngIf="!manualVolumePreviews[plan.id] && !manualVolumePreviewLoading[plan.id] && !manualVolumePreviewErrors[plan.id]" class="muted">Añade ejercicios reconocidos para calcular el volumen de la semana.</p>
                        </section>
                      </div>
                      <div class="editor-footer"><span>Los registros de realización se conservan al editar.</span><div><button type="button" class="secondary" (click)="cancelEditSession(plan)">Cancelar</button> <button type="submit" [disabled]="saving">Guardar cambios</button></div></div>
                    </form>
                    <div class="session-card-actions">
                      <button *ngIf="current.canManage" type="button" class="secondary" (click)="openRecord(session)" [disabled]="saving">{{ recordingSessionId === session.id ? 'Cerrar registro' : (session.completedAt ? 'Editar realización' : 'Registrar realización') }}</button>
                    </div>
                    <form *ngIf="current.canManage && recordingSessionId === session.id" class="record-form" (ngSubmit)="record(session)">
                      <label>Duración real (min)<input type="number" [(ngModel)]="recordDrafts[session.id].duration" name="actualDuration{{session.id}}" min="1" max="1440" required /></label>
                      <label>Esfuerzo percibido (RPE)<input type="number" [(ngModel)]="recordDrafts[session.id].rpe" name="actualRpe{{session.id}}" min="0" max="10" step="0.5" required /></label>
                      <label class="record-notes">Notas<textarea [(ngModel)]="recordDrafts[session.id].notes" name="recordNotes{{session.id}}" rows="2" maxlength="2000" placeholder="Cómo te has encontrado, ajustes realizados…"></textarea></label>
                      <button type="submit" [disabled]="saving">Guardar realización</button>
                    </form>
                  </div>
                </article>
              </div>
            </section></ng-container>
          
          </article>
        </section>
      </ng-container>
    </main>
  `,
})
export class TrainingPeriodizationComponent implements OnInit, OnDestroy {
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
  sessionEditDraft: { date: string; title: string; type: string; duration: number | null; intensity: string; instructions: string; exercisePrescription: string } | null = null;
  recordDrafts: Record<number, { duration: number | null; rpe: number | null; notes: string }> = {};
  adaptationProposals: TrainingAdaptationProposal[] = [];
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
  sessions: Record<number, TrainingSession[]> = {};
  loadTrends: Record<number, TrainingLoadWeek[]> = {};
  sessionViews: Record<number, 'week' | 'list'> = {};
  weekOffsets: Record<number, number> = {};
  weeklyEditorOpen: Record<number, boolean> = {};
  weeklyEditorDrafts: Record<number, WeeklySessionDraft> = {};
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
  exerciseCatalog: Array<{ id: string; name: string; muscles: string; goal: string; equipment: string; equipmentLabel: string; instructions: string; prescription: string; imageUrl?: string; source?: string }> = [
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

  constructor(private route: ActivatedRoute, private http: HttpClient, private changeDetector: ChangeDetectorRef) {
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
    this.refreshManualVolumePreview(plan, this.sessionEditDraft.date, value, session.id);
  }

  onEditedSessionDateChanged(plan: TrainingPlan, session: TrainingSession, date: string): void {
    if (!this.sessionEditDraft) return;
    this.sessionEditDraft.date = date;
    this.refreshManualVolumePreview(plan, date, this.sessionEditDraft.exercisePrescription, session.id);
  }

  private refreshManualVolumeForTarget(
    target: { exercisePrescription: string },
    plan: TrainingPlan
  ): void {
    if (target === this.sessionEditDraft && this.sessionEditDraft && this.editingSessionId !== null) {
      this.refreshManualVolumePreview(plan, this.sessionEditDraft.date,
        this.sessionEditDraft.exercisePrescription, this.editingSessionId);
    } else {
      this.refreshManualVolumePreview(plan);
    }
  }

  private parseManualVolumeExercises(prescription: string | null | undefined): Array<{ exerciseId: string | null; exerciseName: string; sets: number }> {
    const parsed: Array<{ exerciseId: string | null; exerciseName: string; sets: number }> = [];
    for (const line of this.prescriptionLines(prescription)) {
      const match = line.match(/^(.*?)\s*[·|—-]\s*(\d+)\s*[×x]\s*\d+/i);
      if (!match) continue;
      const exerciseName = match[1].trim().replace(/^\d+\.\s*/, '');
      const sets = Number(match[2]);
      if (!exerciseName || !Number.isInteger(sets) || sets < 1 || sets > 10) continue;
      const item = this.exerciseCatalog.find(exercise =>
        exercise.name.trim().toLocaleLowerCase('es') === exerciseName.toLocaleLowerCase('es'));
      const stableId = line.match(/\[ID:\s*([^;\]]+)/i)?.[1]?.trim();
      parsed.push({
        exerciseId: stableId || (item ? item.id.replace(/^repdb-/, '') : null),
        exerciseName,
        sets
      });
    }
    return parsed;
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
    overrideTitle?: string
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
      const addSession = (dateValue: string, title: string, prescription: string | null | undefined): void => {
        const date = dateValue.slice(0, 10);
        if (!date || this.weekStartDate(date) !== targetWeek) return;
        const exercises = this.parseManualVolumeExercises(prescription);
        if (!exercises.length) return;
        requestSessions.push({ weekNumber: 1, day: new Date(date + 'T00:00:00Z').getUTCDay(), title, exercises });
      };

      for (const session of this.sessions[planId] || []) {
        if (session.id === excludeSessionId) continue;
        addSession(session.scheduledDate, session.title, session.exercisePrescription);
      }
      const currentPrescription = overridePrescription ?? draft?.exercisePrescription ?? '';
      addSession(overrideDate || draft?.date || targetDate, overrideTitle || draft?.title || 'Sesión en edición', currentPrescription);
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
      const exercise = this.exerciseCatalog.find(item => exerciseName === item.name.toLocaleLowerCase('es'));
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
              imageUrl: typeof imagePath === 'string' ? 'https://exercise-dataset.com/' + imagePath.replace(/^\//, '') : undefined,
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
      pause_affected_exercise_and_review: 'Dolor registrado: revisión prioritaria'
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

  macroPhaseWeeks(plan: TrainingPlan, phase: TrainingMesocycle | MacrocyclePhase): MacroPhaseLoadWeek[] {
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
      split === 1 ? dayCount === 2 || dayCount === 3 :
      split === 2 ? dayCount === 2 || dayCount === 4 :
      split === 3 ? dayCount === 3 || dayCount === 6 : false;
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
        ? 'Cuerpo completo admite 2 o 3 días por semana.'
        : this.autoSplit === 2
          ? 'Torso/Pierna admite 2 o 4 días por semana.'
          : this.autoSplit === 3
            ? 'Push/Pull/Legs admite 3 o 6 días por semana.'
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
      isDeload: this.autoIsDeload
    };
    this.generatingPlan = true;
    this.generationErrors[plan.id] = '';
    this.generationRequests[plan.id] = request;
    this.http.post<GeneratedProgram>('/api/training/generate-preview', request).pipe(
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
    const name = (this.autoMesoNames[plan.id] || ('Mesociclo ' + ((this.mesocycles[plan.id]?.length || 0) + 1))).trim();
    if (!name) { this.generationErrors[plan.id] = 'Indica un nombre para el mesociclo.'; return; }
    const sequenceNumber = Math.max(0, ...(this.mesocycles[plan.id] || []).map(item => item.sequenceNumber)) + 1;
    this.saving = true;
    this.generationErrors[plan.id] = '';
    const existingMesocycleId = this.autoTargetMesocycleIds[plan.id] ?? null;
    this.http.post<{ sessionsCreated: number; durationWeeks: number; requiresProfessionalReview: boolean; addedToExistingMesocycle?: boolean }>(
      `/api/training/plans/${plan.id}/generate-mesocycle`,
      { name, sequenceNumber, generation, existingMesocycleId, notes: 'Generado automáticamente; requiere revisión profesional antes de asignarlo al paciente.' }
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
    this.generationErrors[planId] = '';
  }

  sessionVolume(generated: GeneratedProgram, session: GeneratedSession): { muscles: Array<{ muscleGroup: string; estimatedEffectiveSets: number }> } | null {
    return generated.qualityReport?.sessionVolumes?.find(item =>
      item.weekNumber === session.weekNumber && item.day === session.day && item.title === session.title) ?? null;
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

  toggleReorganizationDay(plan: TrainingPlan, day: number, checked: boolean): void {
    const draft = this.splitReorganizationDrafts[plan.id];
    draft.trainingDays = checked
      ? [...new Set([...draft.trainingDays, day])]
      : draft.trainingDays.filter(value => value !== day);
    draft.moves = null;
    draft.error = '';
  }

  prepareSplitReorganization(plan: TrainingPlan): void {
    const draft = this.splitReorganizationDrafts[plan.id];
    const days = [...draft.trainingDays].sort((a, b) => ((a + 6) % 7) - ((b + 6) % 7));
    if (!this.isSplitDayCountValidFor(draft.splitType, days.length)) {
      draft.error = draft.splitType === 1 ? 'Cuerpo completo admite 2 o 3 días.' :
        draft.splitType === 2 ? 'Torso/Pierna admite 2 o 4 días.' :
        draft.splitType === 3 ? 'Push/Pull/Legs admite 3 o 6 días.' :
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
    this.splitReorganizationSaving[plan.id] = true;
    this.http.post<{ updatedSessions: number }>('/api/training/plans/' + plan.id + '/reorganize-split', {
      splitType: draft.splitType,
      trainingDays: draft.trainingDays,
      moves: draft.moves.map(move => ({ sessionId: move.sessionId, scheduledDate: move.scheduledDate }))
    }).subscribe({
      next: result => {
        this.splitPreferences[plan.id] = { splitType: draft.splitType, trainingDays: [...draft.trainingDays] };
        this.splitReorganizationSaving[plan.id] = false;
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
    const nextMonday = this.addUtcDays(this.weekStartDate(this.localDateInputValue()), 7);
    const futureSessions = (this.sessions[plan.id] || []).filter(session =>
      session.scheduledDate.slice(0, 10) >= nextMonday && !session.completedAt && session.status !== 'completed');
    if (!futureSessions.length) {
      this.splitReplacementErrors[plan.id] = 'No hay sesiones futuras pendientes que permitan determinar el periodo que se va a reorganizar.';
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
    const warnings: string[] = [];

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
            const catalogItem = this.exerciseCatalog.find(candidate =>
              candidate.name.trim().toLocaleLowerCase('es') === item.row.exerciseName.trim().toLocaleLowerCase('es'));
            const manualId = catalogItem?.id.replace(/^repdb-/, '').toLocaleLowerCase('es');
            return item.row.exerciseName.trim().toLocaleLowerCase('es') === generatedName.toLocaleLowerCase('es') ||
              (!!manualId && manualId === generatedId);
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
            rowsByGeneratedSession[sessionIndex].push({ ...manual.row, exerciseName: generatedName });
          } else {
            rowsByGeneratedSession[sessionIndex].push({
              exerciseName: generatedName, sets: exercise.sets,
              repetitions: exercise.minRepetitions === exercise.maxRepetitions
                ? String(exercise.minRepetitions)
                : exercise.minRepetitions + '–' + exercise.maxRepetitions,
              rir: exercise.targetRepetitionsInReserve ?? null,
              restSeconds: exercise.restSeconds, notes: ''
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
      warnings.push('Se han conservado ' + preservedManualEdits + ' ajustes de series, repeticiones, RIR o descanso en ejercicios que aparecen también en la nueva propuesta.');
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
        this.splitReplacementPreviews[plan.id] = null;
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
        instructions: session.instructions ?? '', exercises: this.parseStructuredExerciseDrafts(session.exercisePrescription)
      };
    }
    return this.weeklyEditorDrafts[session.id];
  }

  private parseStructuredExerciseDrafts(prescription: string | null | undefined): StructuredExerciseDraft[] {
    return this.prescriptionLines(prescription).map(line => {
      const match = line.match(/^(.*?)\s*[·|—-]\s*(\d+)\s*[×x]\s*([^·|—]+?)(?=\s*[·|—]|$)(.*)$/i);
      const tail = match?.[4] ?? line;
      const restMatch = tail.match(/descanso\s*(\d+)\s*(s|seg(?:undos?)?|min(?:utos?)?)?/i);
      const rirMatch = tail.match(/RIR\s*(\d+(?:[.,]\d+)?)/i);
      return {
        exerciseName: (match?.[1] ?? line).trim().replace(/^\d+\.\s*/, ''),
        sets: match ? Math.max(1, Math.min(10, Number(match[2]))) : 0,
        repetitions: (match?.[3] ?? '').trim(),
        rir: rirMatch ? Number(rirMatch[1].replace(',', '.')) : (match ? 2 : null),
        restSeconds: restMatch ? Number(restMatch[1]) * (/min/i.test(restMatch[2] ?? '') ? 60 : 1) : (match ? 90 : 0),
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
        const catalogItem = this.exerciseCatalog.find(exercise =>
          exercise.name.trim().toLocaleLowerCase('es') === item.exerciseName.trim().toLocaleLowerCase('es'));
        const repetitions = item.repetitions.match(/(\d+)(?:\s*[-–]\s*(\d+))?/);
        const minRepetitions = repetitions ? Number(repetitions[1]) : undefined;
        const maxRepetitions = repetitions ? Number(repetitions[2] || repetitions[1]) : undefined;
        return {
          exerciseId: catalogItem?.id.replace(/^repdb-/, ''),
          catalogVersion: catalogItem?.source === 'repdb' ? 'repdb' : 'manual',
          exerciseNameSnapshot: item.exerciseName.trim(),
          order: index + 1,
          sets: item.sets,
          minRepetitions,
          maxRepetitions,
          targetRepetitionsInReserve: item.rir,
          restSeconds: item.restSeconds,
          progressionRule: 'None'
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
        (item.rir === null || !Number.isFinite(item.rir) ? '' : ' · RIR ' + Math.max(0, Math.min(10, item.rir)));
    }).join('\n');
  }

  onWeeklyDraftChanged(plan: TrainingPlan, session: TrainingSession): void {
    const draft = this.weeklyDraft(session);
    this.refreshManualVolumePreview(plan, draft.date, this.serializeStructuredExerciseDrafts(draft.exercises), session.id, draft.title);
  }

  addWeeklyExercise(plan: TrainingPlan, session: TrainingSession): void {
    this.weeklyDraft(session).exercises.push({ exerciseName: '', sets: 3, repetitions: '8–12', rir: 2, restSeconds: 90, notes: '' });
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
        this.notice = 'Sesión actualizada. El registro de realización se ha conservado.';
        this.refreshSessions(plan.id);
      },
      error: error => { this.saving = false; this.error = error?.error?.message || 'No se pudo guardar la sesión.'; }
    });
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
      duration: session.durationMinutes ?? 60, intensity: session.intensityTarget ?? '', instructions: session.instructions ?? '', exercisePrescription: session.exercisePrescription ?? '' };
    this.refreshManualVolumePreview(
      (this.plans || []).find(plan => plan.id === session.planId) ?? { id: session.planId } as TrainingPlan,
      session.scheduledDate, session.exercisePrescription ?? '', session.id);
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
      exercisePrescriptionData: this.buildStructuredPrescriptionData(d.date, d.duration, this.parseStructuredExerciseDrafts(d.exercisePrescription)) }).subscribe({
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
          next: rows => this.sessions[session.planId] = rows,
          error: () => this.error = 'La realización se guardó, pero no se pudo actualizar la lista. Recarga la página.'
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
    const phaseDefinitions: Array<{ name: string; objective: string; phaseType: MacrocyclePhase['phaseType'] }> = [
      { name: 'Acumulación y base', objective: 'Construir tolerancia al volumen y consolidar la técnica.', phaseType: 'accumulation' },
      { name: 'Intensificación y progresión', objective: 'Progresar en el estímulo manteniendo una ejecución sólida.', phaseType: 'intensification' },
      { name: 'Descarga y evaluación', objective: 'Reducir fatiga, revisar resultados y preparar el siguiente bloque.', phaseType: 'deload' }
    ];
    const first = Math.floor(weeks / 3);
    const second = Math.floor(weeks / 3);
    const durations = [first, second, weeks - first - second];
    let cursor = start;
    this.macroProposals[plan.id] = phaseDefinitions.map((phase, index) => {
      const phaseStart = cursor;
      const phaseEnd = this.addDays(phaseStart, durations[index] * 7 - 1);
      cursor = this.addDays(phaseEnd, 1);
      return { ...phase, sequence: index + 1, startDate: phaseStart, endDate: phaseEnd };
    });
    this.notice = 'Propuesta creada sin modificar mesociclos ni sesiones. Revisa fases y fechas antes de guardar.';
    this.error = '';
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

  async generateAllMacrocycleMesocycles(plan: TrainingPlan): Promise<void> {
    const phases = this.macrocyclePhases(plan).slice().sort((a, b) => a.sequenceNumber - b.sequenceNumber);
    if (!phases.length || this.saving || this.generatingPlan || this.bulkGenerationProgress[plan.id]) return;
    const days = [...new Set(this.autoTrainingDaysCsv.split(',').map(value => Number(value.trim())).filter(value => Number.isInteger(value) && value >= 0 && value <= 6))];
    if (days.length < 2 || days.length > 6 || !this.isSplitDayCountValid(days.length) || this.autoMaximumMinutes < 35 || this.autoMaximumMinutes > 180) {
      this.generationErrors[plan.id] = 'Revisa los días compatibles con la distribución elegida (2–6 días) y la duración máxima de 35 a 180 minutos.';
      return;
    }
    if (!confirm('Se generarán mesociclos, microciclos y sesiones para todas las fases del macrociclo que todavía no tengan planificación. Los bloques existentes con contenido se conservarán. ¿Continuar?')) return;
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
