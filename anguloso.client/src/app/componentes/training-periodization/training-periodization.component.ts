import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, forkJoin, of, timeout } from 'rxjs';
import { DateInputDirective } from '../../directives/date-input.directive';

interface TrainingProfile {
  id: number; clientId: number; enabled: boolean; primaryGoal: string; experienceLevel: string; managementMode: 'none' | 'self' | 'collaboration'; notes?: string | null; canManage?: boolean;
}
interface TrainingPlan {
  id: number; name: string; periodizationModel: string; startDate: string; endDate?: string | null; status: string; version: number; notes?: string | null;
}
interface TrainingSession {
  id: number; planId: number; scheduledDate: string; title: string; sessionType: string; durationMinutes?: number | null;
  intensityTarget?: string | null; instructions?: string | null; status: string; completedAt?: string | null;
  actualDurationMinutes?: number | null; sessionRpe?: number | null; recordNotes?: string | null; exercisePrescription?: string | null;
}
interface TrainingLoadWeek { weekStart: string; completedSessions: number; plannedMinutes: number; actualMinutes: number; loadScore: number; }
interface TrainingMesocycle { id: number; planId: number; name: string; sequenceNumber: number; startDate: string; endDate?: string | null; objective?: string | null; notes?: string | null; }
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
  instructions?: string | null; exercisePrescription?: string | null; createdAt?: string;
}
interface RepDbExercise {
  id?: string;
  name_es?: string;
  equipment?: string;
  body_part?: string;
  category?: string;
  goals?: string[];
  primary_muscles?: string[];
  instructions_es?: string[];
  description_es?: string;
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
      <p *ngIf="acceptedClientId" role="status">Colaboración aceptada. <a [routerLink]="['/clients', acceptedClientId, 'training']">Abrir el entrenamiento</a></p>
      <section class="panel template-library" *ngIf="!loading">
        <div class="section-heading"><div><h2>Plantillas de entrenamiento</h2><p>Guarda planes como modelos reutilizables y aplícalos a otros clientes sin alterar el original.</p></div><span class="status">{{ trainingTemplates.length }} plantillas</span></div>
        <p *ngIf="!trainingTemplates.length" class="muted">Todavía no tienes plantillas. Abre un plan de un cliente y pulsa «Guardar como plantilla» para empezar tu biblioteca.</p>
        <article *ngFor="let template of trainingTemplates" class="template-row">
          <div><strong>{{ template.name }}</strong><p>{{ modelLabel(template.periodizationModel) }} · {{ template.sessionCount }} sesiones · {{ template.createdAt | date:'mediumDate' }}</p><small *ngIf="template.notes">{{ template.notes }}</small></div>
          <span class="template-chip">Reutilizable</span>
        </article>
        <form *ngIf="clientId > 0 && profile?.enabled && profile.canManage && trainingTemplates.length" class="form-grid template-apply" (ngSubmit)="createPlanFromTemplate()">
          <h3 class="full">Crear un plan a partir de una plantilla</h3>
          <label>Plantilla<select name="selectedTrainingTemplate" [(ngModel)]="selectedTemplateId" required><option [ngValue]="null" disabled>Selecciona una plantilla</option><option *ngFor="let template of trainingTemplates" [ngValue]="template.id">{{ template.name }} · {{ template.sessionCount }} sesiones</option></select></label>
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
              <label>Buscar<input name="standaloneExerciseSearch" [(ngModel)]="exerciseSearch" placeholder="Ej. sentadilla, remo, movilidad…" /></label>
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
                <img *ngIf="exercise.imageUrl" class="exercise-thumb" [src]="exercise.imageUrl" [alt]="'Ilustración de ' + exercise.name" loading="lazy" />
                <div><strong>{{ exercise.name }}</strong><p>{{ exercise.muscles }} · {{ exercise.equipmentLabel }}</p><small>{{ exercise.instructions }}</small><small *ngIf="exercise.source === 'repdb'" class="exercise-source">Biblioteca RepDB</small></div>
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
            <div><strong>{{ template.name }}</strong><p>{{ sessionTypeLabel(template.sessionType) }}<span *ngIf="template.durationMinutes"> · {{ template.durationMinutes }} min</span><span *ngIf="template.intensityTarget"> · {{ template.intensityTarget }}</span></p><small *ngIf="template.exercisePrescription">{{ template.exercisePrescription }}</small><small *ngIf="template.instructions">{{ template.instructions }}</small></div>
            <button type="button" class="secondary" (click)="deleteSessionTemplate(template)" [disabled]="saving" [attr.aria-label]="'Eliminar plantilla ' + template.name">Eliminar</button>
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
          <form (ngSubmit)="createPlan()" class="form-grid">
            <label>Nombre del plan<input name="planName" [(ngModel)]="planName" maxlength="160" required /></label>
            <label>Modelo de periodización<select name="periodizationModel" [(ngModel)]="periodizationModel"><option value="custom">Personalizado</option><option value="linear">Lineal</option><option value="undulating">Ondulante</option><option value="block">Bloques</option><option value="reverse">Inversa</option><option value="continuous_competition">Competición continua</option><option value="autoregulatory">Autoregulado</option></select></label>
            <label>Fecha inicial<input type="text" appDateInput name="startDate" [(ngModel)]="startDate" required /></label>
            <label>Fecha final (opcional)<input type="text" appDateInput name="endDate" [(ngModel)]="endDate" /></label>
            <label class="full">Notas<textarea name="planNotes" [(ngModel)]="planNotes" rows="2"></textarea></label>
            <div class="full"><button type="submit" [disabled]="saving">Crear plan</button></div>
          </form>
          <p *ngIf="plans.length === 0" class="muted">Todavía no hay planes de entrenamiento.</p>
          <article *ngFor="let plan of plans" class="plan-card">
            <div class="section-heading"><div><h3>{{ plan.name }}</h3><p>{{ plan.startDate | date:'mediumDate' }} <span *ngIf="plan.endDate">— {{ plan.endDate | date:'mediumDate' }}</span> · {{ modelLabel(plan.periodizationModel) }}</p></div><span class="status">{{ plan.status }}</span></div>
            <p *ngIf="plan.notes">{{ plan.notes }}</p>
            <form *ngIf="current.canManage" class="template-save-row" (ngSubmit)="savePlanAsTemplate(plan)">
              <label>Guardar este plan como plantilla<input name="templateName{{plan.id}}" [(ngModel)]="templateNameDrafts[plan.id]" required maxlength="160" [placeholder]="plan.name + ' · plantilla'" /></label>
              <button type="submit" class="secondary" [disabled]="saving">Guardar como plantilla</button>
            </form>
            <section class="cycle-section">
              <h4>Mesociclos</h4>
              <form *ngIf="current.canManage" class="cycle-form" (ngSubmit)="createMesocycle(plan)">
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
                  <div class="section-heading"><strong>{{ meso.sequenceNumber }}. {{ meso.name }}</strong><div class="cycle-actions" *ngIf="current.canManage">
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
                <form *ngIf="current.canManage" class="cycle-form" (ngSubmit)="createMicrocycle(meso)">
                  <label>Nombre<input name="microName{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].name" required maxlength="160" /></label>
                  <label>N.º<input type="number" name="microSeq{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].sequence" required min="1" /></label>
                  <label>Inicio<input type="text" appDateInput name="microStart{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].startDate" required /></label>
                  <label>Fin<input type="text" appDateInput name="microEnd{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].endDate" /></label>
                  <label class="cycle-wide">Enfoque<input name="microFocus{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].focus" maxlength="500" /></label>
                  <button type="submit" [disabled]="saving">Añadir microciclo</button>
                </form>
                <ul *ngIf="microcycles[meso.id]?.length"><li *ngFor="let micro of microcycles[meso.id]; let microIndex = index">
                  <ng-container *ngIf="editingMicrocycleId !== micro.id; else editMicrocycleForm">
                    <div class="section-heading"><span><strong>{{ micro.sequenceNumber }}. {{ micro.name }}</strong> · {{ micro.startDate | date:'mediumDate' }}<span *ngIf="micro.endDate"> — {{ micro.endDate | date:'mediumDate' }}</span><span *ngIf="micro.focus"> · {{ micro.focus }}</span></span>
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
            <section class="session-workspace">
              <div class="session-workspace-heading">
                <div><p class="eyebrow">TRABAJO DIARIO</p><h4>Calendario de entrenamiento</h4><p>Planifica la semana de un vistazo y entra en cada sesión para ajustar sus detalles.</p></div>
                <div style="display:flex;align-items:center;gap:8px;flex-wrap:wrap"><span class="session-count">{{ sessions[plan.id]?.length || 0 }} sesiones</span><button type="button" class="secondary" (click)="exportSessions(plan)" [disabled]="!sessions[plan.id]?.length">Exportar CSV</button></div>
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
                    <label>Aplicar plantilla<select name="savedSessionTemplate{{plan.id}}" [(ngModel)]="selectedSessionTemplateIds[plan.id]"><option [ngValue]="null">Selecciona una plantilla…</option><option *ngFor="let template of sessionTemplates" [ngValue]="template.id">{{ template.name }} · {{ sessionTypeLabel(template.sessionType) }}</option></select></label>
                    <button type="button" class="secondary" (click)="applySavedSessionTemplate(plan)" [disabled]="saving || !selectedSessionTemplateIds[plan.id]">Aplicar</button>
                    <label>Guardar borrador como plantilla<input name="sessionTemplateName{{plan.id}}" [(ngModel)]="sessionTemplateNameDrafts[plan.id]" maxlength="160" placeholder="Ej. Torso A · hipertrofia" /></label>
                    <button type="button" (click)="saveSessionAsTemplate(plan)" [disabled]="saving || !sessionTemplateNameDrafts[plan.id]?.trim()">Guardar plantilla</button>
                  </div>
                  <div *ngIf="sessionTemplates.length" class="saved-session-list">
                    <span *ngFor="let template of sessionTemplates" class="saved-session-chip"><strong>{{ template.name }}</strong><small>{{ sessionTypeLabel(template.sessionType) }}<span *ngIf="template.durationMinutes"> · {{ template.durationMinutes }} min</span></small><button type="button" class="secondary" (click)="deleteSessionTemplate(template)" [disabled]="saving" [attr.aria-label]="'Eliminar plantilla ' + template.name">×</button></span>
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
                      <img *ngIf="exercise.imageUrl" class="exercise-thumb" [src]="exercise.imageUrl" [alt]="'Ilustración de ' + exercise.name" loading="lazy" />
                      <div><strong>{{ exercise.name }}</strong><p>{{ exercise.muscles }} · {{ exercise.equipmentLabel }}</p><small>{{ exercise.instructions }}</small><small *ngIf="exercise.source === 'repdb'" class="exercise-source">Biblioteca RepDB</small></div>
                      <button type="button" class="secondary" (click)="addExerciseToDraft(plan.id, exercise)" [disabled]="saving">＋ Añadir</button>
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
                  <label>Fecha<input type="text" appDateInput name="sessionDate{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].date" [min]="plan.startDate" [max]="plan.endDate || null" required /></label>
                  <label>Tipo de entrenamiento<select name="sessionType{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].type"><option value="endurance">Resistencia</option><option value="strength">Fuerza</option><option value="mobility">Movilidad</option><option value="recovery">Recuperación</option><option value="health">Salud</option><option value="general">General</option><option value="other">Otro</option></select></label>
                  <label>Duración estimada (min)<input type="number" name="sessionDuration{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].duration" min="1" max="1440" placeholder="60" /></label>
                  <label>Intensidad / zona objetivo<input name="sessionIntensity{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].intensity" maxlength="120" placeholder="Ej. RPE 7 · Z2 · 250 W · 5:00/km" /></label>
                  <label class="session-editor-wide">Instrucciones<textarea name="sessionInstructions{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].instructions" rows="2" maxlength="4000" placeholder="Calentamiento, bloques principales, pausas y vuelta a la calma…"></textarea></label>
                  <label class="session-editor-wide">Prescripción de ejercicios<textarea name="sessionExercises{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].exercisePrescription" rows="3" maxlength="6000" placeholder="Sentadilla · 3 × 8 · descanso 2 min · RPE 7&#10;Peso muerto rumano · 3 × 10 · descanso 90 s"></textarea><small>Indica ejercicio, series, repeticiones, descanso y observaciones. Puedes anotar ritmo o potencia en la zona objetivo.</small></label>
                  <div *ngIf="muscleSetSummary(sessionDrafts[plan.id].exercisePrescription).length" class="muscle-set-summary session-editor-wide"><strong>Resumen de series por grupo muscular</strong><span *ngFor="let item of muscleSetSummary(sessionDrafts[plan.id].exercisePrescription)">{{ item.muscle }}: {{ item.sets }} series</span><small>Cuenta las series prescritas de los ejercicios reconocidos en el catálogo; un ejercicio puede contribuir a varios grupos musculares.</small></div>
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
                        <label>Fecha<input type="text" appDateInput name="editSessionDate{{session.id}}" [(ngModel)]="edit.date" [min]="plan.startDate" [max]="plan.endDate || null" required /></label>
                        <label>Tipo<select name="editSessionType{{session.id}}" [(ngModel)]="edit.type"><option value="endurance">Resistencia</option><option value="strength">Fuerza</option><option value="mobility">Movilidad</option><option value="recovery">Recuperación</option><option value="health">Salud</option><option value="general">General</option><option value="other">Otro</option></select></label>
                        <label>Duración (min)<input type="number" name="editSessionDuration{{session.id}}" [(ngModel)]="edit.duration" min="1" max="1440" required /></label>
                        <label>Intensidad / zona<input name="editSessionIntensity{{session.id}}" [(ngModel)]="edit.intensity" maxlength="120" /></label>
                        <label class="session-editor-wide">Instrucciones<textarea name="editSessionInstructions{{session.id}}" [(ngModel)]="edit.instructions" rows="3" maxlength="4000"></textarea></label>
                        <label class="session-editor-wide">Prescripción de ejercicios<textarea name="editSessionExercises{{session.id}}" [(ngModel)]="edit.exercisePrescription" rows="4" maxlength="6000" placeholder="Ejercicio · series × repeticiones · descanso · notas"></textarea></label>
                      </div>
                      <div class="editor-footer"><span>Los registros de realización se conservan al editar.</span><div><button type="button" class="secondary" (click)="editingSessionId = null; sessionEditDraft = null">Cancelar</button> <button type="submit" [disabled]="saving">Guardar cambios</button></div></div>
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
            </section>
          </article>
        </section>
      </ng-container>
    </main>
  `,
  styles: [`
    .template-library{margin-bottom:18px}.template-row{display:flex;justify-content:space-between;align-items:center;gap:12px;padding:12px 0;border-top:1px solid var(--border-color,#e1e5eb)}.template-row p{margin:4px 0;color:var(--secondary-text-color,#687385);font-size:.88rem}.template-row small{color:var(--secondary-text-color,#687385)}.template-chip{white-space:nowrap;border-radius:999px;padding:5px 9px;background:#e8f5ed;color:#236442;font-size:.78rem;font-weight:600}.template-apply{margin-top:14px;padding-top:14px;border-top:1px solid var(--border-color,#e1e5eb)}.template-apply h3{margin:0}.template-apply select,.template-apply input,.template-save-row input{width:100%;box-sizing:border-box;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:var(--surface-color,#fff);color:inherit}.template-save-row{display:flex;align-items:end;gap:10px;margin:10px 0 16px;padding:12px;border:1px dashed var(--border-color,#cbd2dc);border-radius:10px}.template-save-row label{display:grid;gap:6px;flex:1;font-size:.86rem;font-weight:600}.template-save-row button{white-space:nowrap}
    .saved-session-templates{margin:12px 0 16px;padding:14px;border:1px solid var(--border-color,#d8dee8);border-radius:12px;background:var(--surface-color,#fff)}.standalone-session-template-panel{display:grid;gap:16px}.standalone-session-template-form{display:grid;gap:13px}.standalone-session-template-form label{display:grid;gap:6px;min-width:0;font-size:.9rem;font-weight:650}.standalone-session-template-form input,.standalone-session-template-form select,.standalone-session-template-form textarea{box-sizing:border-box;width:100%;min-width:0;border:1px solid #b9bec7;border-radius:9px;padding:10px;font:inherit;background:var(--surface-color,#fff);color:inherit}.standalone-session-template-fields{display:grid;grid-template-columns:2fr 1fr 2fr;gap:12px}.standalone-session-template-actions{display:flex;align-items:center;justify-content:space-between;gap:14px}.standalone-session-template-actions .muted{margin:0}.standalone-session-template-library{display:grid;gap:8px}.standalone-exercise-library{margin:0;padding:12px}.standalone-exercise-library .exercise-results{max-height:440px;overflow:auto}.standalone-exercise-library .exercise-result{grid-template-columns:auto minmax(0,1fr) auto}.standalone-exercise-library .exercise-thumb{width:58px;height:58px;object-fit:contain;border-radius:8px}.standalone-exercise-library .exercise-result small{display:block}.standalone-exercise-library .exercise-attribution{font-size:.78rem;color:var(--secondary-text-color,#666)}@media(max-width:560px){.standalone-exercise-library .exercise-result{grid-template-columns:1fr}.standalone-exercise-library .exercise-thumb{width:72px;height:72px}}.standalone-session-template-library h3{margin:0}.standalone-session-template-library .template-row{align-items:flex-start}.standalone-session-template-library .template-row small{display:block;white-space:pre-wrap;overflow-wrap:anywhere;margin-top:5px}@media(max-width:650px){.standalone-session-template-fields{grid-template-columns:1fr}.standalone-session-template-actions{align-items:stretch;flex-direction:column}.standalone-session-template-actions button{width:100%}}.saved-session-heading{display:flex;align-items:flex-start;justify-content:space-between;gap:12px}.saved-session-heading strong{font-size:.98rem}.saved-session-heading p{margin:4px 0;color:var(--secondary-text-color,#666);font-size:.84rem}.saved-session-controls{display:grid;grid-template-columns:minmax(0,2fr) auto minmax(0,2fr) auto;gap:9px;align-items:end;margin-top:12px}.saved-session-controls label{display:grid;gap:5px;min-width:0;font-size:.84rem;font-weight:650}.saved-session-controls input,.saved-session-controls select{width:100%;min-width:0;box-sizing:border-box;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:var(--surface-color,#fff);color:inherit}.saved-session-list{display:flex;gap:7px;flex-wrap:wrap;margin-top:12px}.saved-session-chip{display:inline-flex;align-items:center;gap:8px;padding:6px 8px;border:1px solid var(--border-color,#d8dee8);border-radius:9px;background:var(--surface-container,#f7f8fc);max-width:100%}.saved-session-chip strong{font-size:.78rem;overflow-wrap:anywhere}.saved-session-chip small{color:var(--secondary-text-color,#666);font-size:.72rem;white-space:nowrap}.saved-session-chip button{padding:2px 7px;line-height:1.2}@media(max-width:850px){.saved-session-controls{grid-template-columns:minmax(0,1fr) auto}.saved-session-controls label:nth-of-type(3){grid-column:1/-1}.saved-session-controls button:nth-of-type(2){grid-column:2}}@media(max-width:560px){.saved-session-controls{grid-template-columns:1fr}.saved-session-controls label:nth-of-type(3),.saved-session-controls button:nth-of-type(2){grid-column:auto}.saved-session-controls button{width:100%}.saved-session-chip{flex-wrap:wrap}}
    .prescription-quick-settings{display:grid;grid-template-columns:minmax(70px,.7fr) minmax(110px,1fr) minmax(90px,1fr);gap:10px;align-items:end;margin:10px 0;padding:12px;border:1px solid var(--border-color,#d8dee8);border-radius:10px;background:var(--surface-container,#f8f9fc)}.prescription-quick-settings label{display:grid;gap:5px;font-size:.82rem;font-weight:650}.prescription-quick-settings input{width:100%;min-width:0;box-sizing:border-box;border:1px solid #b9bec7;border-radius:8px;padding:8px;font:inherit;background:var(--surface-color,#fff);color:inherit}.prescription-quick-settings p{grid-column:1/-1;margin:0;color:var(--secondary-text-color,#687385);font-size:.78rem}.muscle-set-summary{display:flex;flex-wrap:wrap;align-items:center;gap:8px;margin:8px 0;padding:10px 12px;border:1px solid var(--border-color,#d8dee8);border-radius:9px}.muscle-set-summary strong{width:100%;font-size:.88rem}.muscle-set-summary span{padding:4px 8px;border-radius:999px;background:var(--surface-container,#f0f3f8);font-size:.8rem}.muscle-set-summary small{width:100%;color:var(--secondary-text-color,#687385);font-size:.75rem}@media(max-width:560px){.prescription-quick-settings{grid-template-columns:1fr 1fr}.prescription-quick-settings label:last-of-type{grid-column:1/-1}}    .exercise-library{margin:14px 0;padding:16px;border:1px solid var(--border-color,#d8dee8);border-radius:12px;background:var(--surface-color,#fff)}.exercise-library h5{font-size:1.05rem;margin:0}.exercise-filters{display:grid;grid-template-columns:2fr 1fr 1fr;gap:10px;margin:12px 0}.exercise-filters label,.session-tools label{display:grid;gap:5px;font-size:.85rem;font-weight:600}.exercise-filters input,.exercise-filters select,.session-tools select{min-width:0;width:100%;box-sizing:border-box;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:var(--surface-color,#fff);color:inherit}.exercise-thumb{width:76px;height:76px;object-fit:contain;border-radius:10px;background:#f4f6f8;flex:0 0 76px}.exercise-source{display:block;color:var(--secondary-text-color,#666);font-size:.75rem;margin-top:4px}.exercise-attribution{font-size:.78rem;color:var(--secondary-text-color,#666);margin:8px 0 0}.exercise-attribution a{color:var(--primary-color,#3657a6)}.exercise-results{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px;max-height:310px;overflow:auto}.exercise-result{display:flex;justify-content:space-between;align-items:center;gap:10px;padding:10px;border:1px solid var(--border-color,#e1e5eb);border-radius:9px}.exercise-result strong{font-size:.9rem}.exercise-result p,.exercise-result small{display:block;margin:3px 0;color:var(--secondary-text-color,#64748b);font-size:.78rem}.exercise-result button{white-space:nowrap}.session-tools{display:grid;grid-template-columns:minmax(0,2fr) auto minmax(0,1.5fr) minmax(0,1fr) minmax(0,1.5fr) auto;align-items:end;gap:8px;margin-top:14px;padding-top:14px;border-top:1px solid var(--border-color,#e1e5eb)}@media(max-width:850px){.exercise-filters{grid-template-columns:1fr 1fr}.exercise-filters label:first-child{grid-column:1/-1}.exercise-results{grid-template-columns:1fr}.session-tools{grid-template-columns:1fr 1fr}}@media(max-width:560px){.exercise-filters,.session-tools{grid-template-columns:1fr}.session-tools label:first-child{grid-column:auto}.exercise-result{align-items:flex-start}.exercise-library{padding:12px}}
    :host{display:block;color:var(--text-color,#202124)}.cycle-actions{display:flex;gap:6px;flex-wrap:wrap}.cycle-actions button{padding:5px 9px}.training-page{max-width:1100px;margin:0 auto;padding:24px;display:grid;gap:18px}.back-link{color:var(--primary-color,#3657a6);text-decoration:none}header h1{margin:.2rem 0 .4rem;font-size:clamp(1.6rem,3vw,2.2rem)}header p{margin:.3rem 0}.eyebrow{font-size:.75rem;font-weight:700;letter-spacing:.08em;color:var(--primary-color,#3657a6)}.panel,.plan-card{border:1px solid var(--border-color,#ddd);border-radius:14px;padding:20px;background:var(--surface-color,#fff)}.panel h2,.plan-card h3{margin-top:0}.section-heading{display:flex;justify-content:space-between;align-items:flex-start;gap:12px}.section-heading p,.plan-card p{color:var(--secondary-text-color,#666);margin:.4rem 0}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px;margin:16px 0}.form-grid label,.session-form label{display:grid;gap:6px;font-weight:600;font-size:.9rem}.form-grid .full{grid-column:1/-1}.form-grid input,.form-grid select,.form-grid textarea,.session-form input,.session-form select{width:100%;box-sizing:border-box;border:1px solid #b9bec7;border-radius:8px;padding:10px;font:inherit;background:transparent;color:inherit}.cycle-section{margin:16px 0;padding:14px;border:1px solid var(--border-color,#ddd);border-radius:10px}.cycle-section h4{margin:.2rem 0 .8rem}.cycle-form{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;align-items:end;margin:12px 0}.cycle-form label{display:grid;gap:6px;font-size:.86rem;font-weight:600}.cycle-form input{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:transparent;color:inherit}.cycle-wide{grid-column:span 2}.mesocycle-card{padding:12px 0;border-top:1px solid var(--border-color,#ddd)}.invite-form{display:flex;gap:10px;align-items:end;flex-wrap:wrap;margin:14px 0}.invite-form label{display:grid;gap:6px;flex:1 1 240px;font-size:.9rem;font-weight:600}.invite-form input{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:10px;font:inherit;background:transparent;color:inherit}.collaboration-row{display:flex;justify-content:space-between;align-items:center;gap:12px;padding:12px 0;border-top:1px solid var(--border-color,#ddd)}.collaboration-row p{margin:.35rem 0;color:var(--secondary-text-color,#666)}.calendar-toolbar{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;margin:14px 0}.view-switch{display:flex;gap:3px;padding:4px;border:1px solid var(--border-color,#ddd);border-radius:11px;background:var(--surface-container,#f3f4f8)}.view-switch button{border:0;background:transparent;color:inherit;border-radius:8px;padding:8px 12px;font-weight:700;cursor:pointer}.view-switch button.active{background:var(--surface-color,#fff);color:var(--primary-color,#3657a6);box-shadow:0 1px 4px #00000012}.week-navigation{display:flex;align-items:center;gap:8px;flex-wrap:wrap}.week-navigation strong{font-size:.9rem;min-width:150px;text-align:center}.week-summary{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;margin:14px 0}.week-summary>div{display:flex;align-items:center;gap:10px;padding:12px;border:1px solid var(--border-color,#e0e3e9);border-radius:11px;background:var(--surface-color,#fff)}.week-summary>div>span:last-child{display:grid;gap:3px}.week-summary strong{font-size:1.05rem}.week-summary small{font-size:.76rem;color:var(--secondary-text-color,#666)}.summary-dot{width:10px;height:10px;border-radius:50%;flex:0 0 10px}.planned-dot{background:#6b7ed1}.minutes-dot{background:#d29b3c}.completed-dot{background:#36876a}.week-grid{display:grid;grid-template-columns:repeat(7,minmax(0,1fr));gap:7px;align-items:stretch;margin:14px 0}.week-day{min-width:0;min-height:125px;padding:8px;border:1px solid var(--border-color,#e0e3e9);border-radius:11px;background:var(--surface-color,#fff)}.week-day.today{border:2px solid var(--primary-color,#3657a6);padding:7px;background:color-mix(in srgb,var(--primary-color,#3657a6) 4%,var(--surface-color,#fff))}.week-day-heading{display:flex;align-items:center;justify-content:space-between;gap:4px;padding-bottom:8px;border-bottom:1px solid var(--border-color,#e7e8ed)}.week-day-heading span{text-transform:uppercase;font-size:.68rem;letter-spacing:.04em;color:var(--secondary-text-color,#666);font-weight:800}.week-day-heading strong{display:grid;place-items:center;width:27px;height:27px;border-radius:50%;font-size:.9rem}.week-day.today .week-day-heading strong{background:var(--primary-color,#3657a6);color:white}.week-day-empty{display:grid;place-items:center;min-height:65px;color:var(--secondary-text-color,#a3a6ad)}.week-session{display:grid;gap:5px;margin-top:8px;padding:8px 7px;border-radius:8px;background:#f0f1ff;border-left:3px solid #6b7ed1;color:#262c45;font-size:.75rem;overflow-wrap:anywhere}.week-strength{background:#f0eaff;border-left-color:#8c67c2}.week-endurance{background:#e5f5ed;border-left-color:#3d9a70}.week-mobility{background:#e3f4f8;border-left-color:#3d91a6}.week-recovery{background:#eeedfa;border-left-color:#8075bb}.week-health{background:#ffedf1;border-left-color:#d26b85}.week-session-done{opacity:.78;border-left-color:#36876a}.week-session-type{font-size:1rem;font-weight:800}.week-session>strong{font-size:.78rem;line-height:1.25}.week-session-meta,.week-session-status{font-size:.68rem;line-height:1.3}.week-session-status{font-weight:750}.week-session-actions{display:grid;grid-template-columns:1fr;gap:4px;margin-top:3px}.week-session-actions button{border:1px solid #c7cbd7;border-radius:6px;background:#ffffffb8;padding:5px 3px;color:#333;font-size:.68rem;font-weight:750;cursor:pointer}.session-workspace{margin-top:24px;padding-top:20px;border-top:1px solid var(--border-color,#ddd)}.session-workspace-heading{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:16px}.session-workspace-heading h4{font-size:1.25rem;margin:.15rem 0}.session-workspace-heading p:not(.eyebrow){margin:.35rem 0;color:var(--secondary-text-color,#666)}.session-count{white-space:nowrap;border:1px solid var(--border-color,#ddd);border-radius:99px;padding:6px 11px;font-size:.8rem;font-weight:700}.session-editor{padding:18px;border:1px solid var(--border-color,#d6dce8);border-radius:14px;background:linear-gradient(135deg,var(--surface-container,#f7f8fc),var(--surface-color,#fff));margin:14px 0 18px}.editor-title{display:flex;align-items:center;gap:12px;margin-bottom:16px}.editor-title strong{font-size:1rem}.editor-title p{margin:3px 0 0;color:var(--secondary-text-color,#666);font-size:.88rem}.editor-icon{display:grid;place-items:center;width:40px;height:40px;border-radius:12px;background:var(--primary-color,#3657a6);color:#fff;font-size:1.5rem}.session-editor-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:13px}.session-editor-grid label{display:grid;gap:6px;font-size:.87rem;font-weight:650}.session-editor-grid input,.session-editor-grid select,.session-editor-grid textarea{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:9px;padding:10px 11px;font:inherit;background:var(--surface-color,#fff);color:inherit;min-width:0}.session-editor-grid input:focus,.session-editor-grid select:focus,.session-editor-grid textarea:focus{outline:2px solid color-mix(in srgb,var(--primary-color,#3657a6) 30%,transparent);outline-offset:1px}.session-editor-wide{grid-column:1/-1}.editor-footer{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-top:15px}.editor-footer>span{font-size:.78rem;color:var(--secondary-text-color,#666)}.session-empty{padding:20px;border:1px dashed var(--border-color,#c9ced8);border-radius:12px;color:var(--secondary-text-color,#666);text-align:center}.session-list{display:grid;gap:10px;margin-top:14px}.session-card{display:grid;grid-template-columns:58px minmax(0,1fr);gap:14px;padding:16px;border:1px solid var(--border-color,#dfe2e8);border-radius:14px;background:var(--surface-color,#fff);transition:border-color .15s,box-shadow .15s}.session-card:hover{border-color:color-mix(in srgb,var(--primary-color,#3657a6) 45%,var(--border-color,#ddd));box-shadow:0 4px 18px #0000000a}.session-completed{border-left:4px solid #36876a}.session-date-rail{display:flex;flex-direction:column;align-items:center;justify-content:center;align-self:start;min-height:58px;border-radius:11px;background:var(--surface-container,#f2f4f8);color:var(--primary-color,#3657a6)}.session-date-rail strong{font-size:1.35rem;line-height:1.2}.session-date-rail span{text-transform:uppercase;font-size:.7rem;font-weight:800;letter-spacing:.06em}.session-card-main{min-width:0}.session-card-top{display:flex;align-items:center;gap:11px}.session-type-icon{flex:0 0 38px;width:38px;height:38px;display:grid;place-items:center;border-radius:11px;background:#e9edff;color:#334b9b;font-size:1.25rem;font-weight:800}.type-endurance{background:#e2f3ec;color:#176b4b}.type-strength{background:#eee8ff;color:#6841a5}.type-mobility{background:#e1f3f8;color:#17677b}.type-recovery{background:#e9e8f8;color:#514b94}.type-health{background:#ffe8ee;color:#a23c5a}.session-title-block{flex:1;min-width:0}.session-title-line{display:flex;align-items:center;gap:8px;flex-wrap:wrap}.session-title-line h5{margin:0;font-size:1rem;line-height:1.35}.session-title-block p{margin:5px 0 0;font-size:.83rem;color:var(--secondary-text-color,#666)}.session-status{font-size:.72rem;font-weight:750;border-radius:99px;padding:4px 8px;background:#edf0f5;color:#555}.session-status.done{background:#dff3e9;color:#176b4b}.session-edit-button{flex:0 0 auto}.session-instructions{white-space:pre-line;margin:12px 0 0;padding:11px 13px;border-radius:9px;background:var(--surface-container,#f5f6f9);font-size:.88rem;line-height:1.5}.completion-summary{display:flex;align-items:center;gap:9px;margin-top:12px;padding:10px 12px;border-radius:10px;background:#edf8f2}.completion-check{display:grid;place-items:center;width:27px;height:27px;border-radius:50%;background:#36876a;color:white;font-weight:800}.completion-summary>span:nth-child(2){display:grid;gap:2px;flex:1}.completion-summary strong{font-size:.82rem;color:#205e46}.completion-summary small{font-size:.78rem;color:#39715b}.duration-delta{font-size:.78rem;font-weight:750;color:#39715b}.session-card-actions{display:flex;justify-content:flex-end;margin-top:10px}.session-edit-form{margin-top:14px;padding:14px;border-radius:11px;border:1px solid var(--border-color,#d8dce5);background:var(--surface-container,#f6f7fa)}.session-edit-form .editor-footer{align-items:center}.session-editor-grid textarea{resize:vertical}.record-form{grid-template-columns:repeat(2,minmax(0,1fr))}.record-form .record-notes{grid-column:1/-1}.record-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin-top:12px;padding:14px;border-radius:10px;background:var(--surface-container,#f6f7f9)}.record-form label{display:grid;gap:6px;font-size:.88rem;font-weight:600}.record-form label:nth-child(3){grid-column:1/-1}.record-form input,.record-form textarea{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:var(--surface-color,#fff);color:inherit}.record-form button{justify-self:start}.session-row{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:12px;align-items:start;padding:12px 0;border-top:1px solid var(--border-color,#ddd)}.session-form button{flex:0 0 auto}button{border:0;border-radius:8px;padding:10px 14px;background:var(--primary-color,#3657a6);color:white;font:inherit;font-weight:600;cursor:pointer}button:disabled{opacity:.55;cursor:wait}button.secondary{background:transparent;border:1px solid #9aa0a6;color:inherit}.plan-card{margin-top:14px}.status{font-size:.8rem;border-radius:20px;background:#eef2ff;padding:5px 10px}.session-list{border-top:1px solid #ddd;margin-top:14px}.session-row{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:12px;align-items:start;padding:12px 0;border-bottom:1px solid #eee}.record-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin-top:12px;padding:14px;border-radius:10px;background:var(--surface-container,#f6f7f9)}.record-form label{display:grid;gap:6px;font-size:.88rem;font-weight:600}.record-form label:nth-child(3){grid-column:1/-1}.record-form input,.record-form textarea{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:var(--surface-color,#fff);color:inherit}.record-form button{justify-self:start}.session-row p{margin:.3rem 0}.muted{color:#777}.error{color:#a21d24}@media(max-width:650px){.training-page{padding:14px}.collaboration-row{align-items:stretch;flex-direction:column}.cycle-form{grid-template-columns:repeat(2,minmax(0,1fr))}.cycle-wide{grid-column:1/-1}.cycle-form button{grid-column:1/-1}.invite-form button{width:100%}.form-grid{grid-template-columns:1fr}.form-grid .full{grid-column:auto}.section-heading{flex-direction:column;align-items:stretch}.week-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.week-summary{grid-template-columns:1fr}.week-summary>div{padding:10px}.week-day{min-height:100px}.week-navigation{width:100%;justify-content:space-between}.week-navigation strong{min-width:0;flex:1}.session-editor-grid{grid-template-columns:1fr}.session-editor-wide{grid-column:auto}.editor-footer{align-items:stretch;flex-direction:column}.editor-footer button{width:100%}.session-workspace-heading{align-items:flex-start}.session-card{grid-template-columns:42px minmax(0,1fr);gap:10px;padding:11px}.session-date-rail{min-height:48px}.session-date-rail strong{font-size:1.1rem}.session-type-icon{width:32px;height:32px;flex-basis:32px}.session-card-top{align-items:flex-start;flex-wrap:wrap}.session-edit-button{margin-left:auto}.session-title-block{flex-basis:calc(100% - 48px)}.session-card-actions{justify-content:stretch}.session-card-actions button{width:100%}.record-form{grid-template-columns:1fr}.record-form .record-notes{grid-column:auto}.record-form button{width:100%}.session-edit-form .editor-footer>div{display:grid;gap:8px}.session-edit-form .editor-footer button{width:100%}.cycle-actions{width:100%}}
  `]
})
export class TrainingPeriodizationComponent implements OnInit {
  clientId = 0;
  loading = true;
  loadFailed = false;
  saving = false;
  error = '';
  notice = '';
  recordingSessionId: number | null = null;
  editingSessionId: number | null = null;
  sessionEditDraft: { date: string; title: string; type: string; duration: number | null; intensity: string; instructions: string; exercisePrescription: string } | null = null;
  recordDrafts: Record<number, { duration: number | null; rpe: number | null; notes: string }> = {};
  collaborations: TrainingCollaboration[] = [];
  invitations: TrainingInvitation[] = [];
  trainingTemplates: TrainingTemplate[] = [];
  sessionTemplates: TrainingSessionTemplate[] = [];
  standaloneSessionTemplateDraft = { name: '', type: 'strength', duration: 60 as number | null, intensity: '', instructions: '', exercisePrescription: '' };
  sessionTemplateNameDrafts: Record<number, string> = {};
  selectedSessionTemplateIds: Record<number, number | null> = {};
  templateNameDrafts: Record<number, string> = {};
  selectedTemplateId: number | null = null;
  templatePlanName = '';
  templateStartDate = new Date().toISOString().slice(0, 10);
  mesocycles: Record<number, TrainingMesocycle[]> = {};
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
  plans: TrainingPlan[] = [];
  sessions: Record<number, TrainingSession[]> = {};
  loadTrends: Record<number, TrainingLoadWeek[]> = {};
  sessionViews: Record<number, 'week' | 'list'> = {};
  weekOffsets: Record<number, number> = {};
  sessionDrafts: Record<number, { date: string; title: string; type: string; duration: number | null; intensity: string; instructions: string; exercisePrescription: string }> = {};
  exerciseSearch = '';
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
    return muscles.split(/,|\s+y\s+|\s+and\s+|\s*\/\s*/i)
      .map(muscle => muscle.trim())
      .filter(muscle => muscle && !/^grupo muscular según ficha$/i.test(muscle));
  }

  get exerciseMuscleGroups(): string[] {
    const labels = new Map<string, string>();
    for (const exercise of this.exerciseCatalog) {
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
    return this.exerciseCatalog.filter(exercise =>
      (!query || (exercise.name + ' ' + exercise.muscles + ' ' + exercise.instructions).toLocaleLowerCase('es').includes(query)) &&
      (this.exerciseGoalFilter === 'all' || exercise.goal === this.exerciseGoalFilter) &&
      (this.exerciseEquipmentFilter === 'all' || exercise.equipment === this.exerciseEquipmentFilter) &&
      (selectedMuscle === 'all' || this.exerciseMuscleLabels(exercise.muscles).some(muscle => muscle.toLocaleLowerCase('es') === selectedMuscle)));
  }

  planName = '';
  periodizationModel = 'custom';
  startDate = new Date().toISOString().slice(0, 10);
  endDate = '';
  planNotes = '';

  constructor(private route: ActivatedRoute, private http: HttpClient, private changeDetector: ChangeDetectorRef) {}

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
      .subscribe(items => { this.trainingTemplates = items; this.changeDetector.markForCheck(); });
  }


  loadSessionTemplates(): void {
    this.http.get<TrainingSessionTemplate[]>('/api/training/session-templates')
      .pipe(catchError(() => of([] as TrainingSessionTemplate[])))
      .subscribe(items => { this.sessionTemplates = items; this.changeDetector.markForCheck(); });
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
      exercisePrescription: draft.exercisePrescription.trim() || null
    }).subscribe({
      next: () => {
        this.saving = false;
        this.standaloneSessionTemplateDraft = { name: '', type: 'strength', duration: 60, intensity: '', instructions: '', exercisePrescription: '' };
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

  private loadExpandedExerciseCatalog(): void {
    // RepDB permite el uso comercial dentro de la aplicación con atribución visible.
    // No se copia el catálogo completo al repositorio ni se expone como descarga masiva.
    this.http.get<{ exercises?: RepDbExercise[] }>('https://exercise-dataset.com/exercises.json')
      .pipe(timeout(12000), catchError(() => of({ exercises: [] })))
      .subscribe(data => {
        const imported = (data.exercises ?? []).filter((item): item is RepDbExercise & { id: string; name_es: string } =>
          typeof item.id === 'string' && typeof item.name_es === 'string' && !!item.name_es.trim())
          .map(item => {
            const equipmentRaw = String(item.equipment ?? '').toLocaleLowerCase('es');
            const bodyPart = String(item.body_part ?? '').toLocaleLowerCase('es');
            const category = String(item.category ?? '').toLocaleLowerCase('es');
            const goals = Array.isArray(item.goals) ? item.goals.join(' ').toLocaleLowerCase('es') : '';
            const muscles = Array.isArray(item.primary_muscles) ? item.primary_muscles.join(', ').replace(/[-_]/g, ' ') : bodyPart.replace(/[-_]/g, ' ');
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
            const instructions = Array.isArray(item.instructions_es) ? item.instructions_es.join(' ') :
              (typeof item.description_es === 'string' ? item.description_es : 'Realiza el movimiento con control y dentro de un rango cómodo.');
            const imagePath = item.images?.flat?.start ?? item.images?.flat?.main ?? item.images?.flat?.peak;
            const prescription = goal === 'endurance' ? item.name_es + ' · 10–20 min · intensidad ajustable'
              : goal === 'mobility' ? item.name_es + ' · 2 × 8–12 repeticiones controladas'
              : item.name_es + ' · 3 × 8–12 · ajustar carga y descanso';
            return {
              id: 'repdb-' + item.id, name: item.name_es, muscles: muscles || 'Grupo muscular según ficha',
              goal, equipment, equipmentLabel: equipmentLabel.charAt(0).toLocaleUpperCase('es') + equipmentLabel.slice(1),
              instructions, prescription,
              imageUrl: typeof imagePath === 'string' ? 'https://exercise-dataset.com/' + imagePath.replace(/^\//, '') : undefined,
              source: 'repdb'
            };
          });
        if (imported.length) {
          const curated = this.exerciseCatalog.filter(item => !item.id.startsWith('repdb-'));
          const seen = new Set(imported.map(item => item.name.toLocaleLowerCase('es')));
          this.exerciseCatalog = [...imported, ...curated.filter(item => !seen.has(item.name.toLocaleLowerCase('es')))];
          this.changeDetector.markForCheck();
        }
      });
  }

  loadInvitations(): void {
    this.http.get<TrainingInvitation[]>('/api/training/collaborations/invitations').pipe(catchError(() => of([] as TrainingInvitation[])))
      .subscribe(items => this.invitations = items);
  }

  loadCollaborations(): void {
    this.http.get<TrainingCollaboration[]>(`/api/training/clients/${this.clientId}/collaborations`)
      .pipe(catchError(() => of([] as TrainingCollaboration[])))
      .subscribe(items => this.collaborations = items);
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
      next: () => { d.name = ''; d.objective = ''; d.sequence += 1; this.saving = false; this.load(); },
      error: e => { this.error = e?.error?.message || 'No se pudo crear el mesociclo.'; this.saving = false; }
    });
  }

  createMicrocycle(meso: TrainingMesocycle): void {
    const d = this.microcycleDrafts[meso.id];
    if (!d?.name.trim() || !d.startDate || !Number.isInteger(d.sequence) || d.sequence < 1) return;
    this.saving = true; this.error = '';
    this.http.post(`/api/training/mesocycles/${meso.id}/microcycles`, { name: d.name.trim(), sequenceNumber: d.sequence, startDate: d.startDate, endDate: d.endDate || null, focus: d.focus.trim() || null, notes: null }).subscribe({
      next: () => { d.name = ''; d.focus = ''; d.sequence += 1; this.saving = false; this.http.get<TrainingMicrocycle[]>(`/api/training/mesocycles/${meso.id}/microcycles`).subscribe({ next: rows => this.microcycles[meso.id] = rows, error: () => this.error = 'El microciclo se guardó, pero no se pudo actualizar la lista.' }); },
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
        this.profile = profile;
        this.changeDetector.detectChanges();
        if (!profile) {
          this.loading = false;
          this.changeDetector.detectChanges();
          return;
        }

        this.trainingManagementMode = profile.managementMode ?? (profile.enabled ? 'self' : 'none');
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
          if (plans === null) {
            this.loading = false;
            this.changeDetector.detectChanges();
            return;
          }

          try {
            this.plans = plans;
            this.sessionDrafts = {};
            for (const plan of plans) {
              this.sessionDrafts[plan.id] = {
                date: this.defaultSessionDate(plan),
                title: '',
                type: 'strength',
                duration: 60,
                intensity: '',
                instructions: '',
                exercisePrescription: ''
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
            this.loadSessions(plans);
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

  private loadSessions(plans: TrainingPlan[]): void {
    if (!plans.length) return;
    for (const plan of plans) {
      this.mesocycleDrafts[plan.id] = this.mesocycleDrafts[plan.id] ?? { name: '', sequence: 1, startDate: plan.startDate, endDate: plan.endDate ?? '', objective: '' };
      this.http.get<TrainingMesocycle[]>(`/api/training/plans/${plan.id}/mesocycles`).pipe(catchError(() => of([] as TrainingMesocycle[]))).subscribe(items => {
        this.mesocycles[plan.id] = items;
        for (const meso of items) {
          this.microcycleDrafts[meso.id] = this.microcycleDrafts[meso.id] ?? { name: '', sequence: 1, startDate: meso.startDate, endDate: meso.endDate ?? '', focus: '' };
          this.http.get<TrainingMicrocycle[]>(`/api/training/mesocycles/${meso.id}/microcycles`).pipe(catchError(() => of([] as TrainingMicrocycle[]))).subscribe(rows => this.microcycles[meso.id] = rows);
        }
      });
    }
    forkJoin(plans.map(p => this.http.get<TrainingSession[]>(`/api/training/plans/${p.id}/sessions`).pipe(catchError(() => of([] as TrainingSession[]))))).subscribe(items => {
      plans.forEach((p, i) => this.sessions[p.id] = items[i]);
      for (const plan of plans) {
        this.http.get<TrainingLoadWeek[]>(`/api/training/plans/${plan.id}/load-trend`)
          .pipe(catchError(() => of([] as TrainingLoadWeek[])))
          .subscribe(weeks => this.loadTrends[plan.id] = weeks);
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

  createPlan(): void {
    if (!this.planName.trim() || !this.startDate) return;
    this.saving = true; this.error = '';
    this.http.post<TrainingPlan>(`/api/training/clients/${this.clientId}/plans`, { name: this.planName, periodizationModel: this.periodizationModel, startDate: this.startDate, endDate: this.endDate || null, notes: this.planNotes }).subscribe({
      next: () => { this.planName = ''; this.endDate = ''; this.planNotes = ''; this.saving = false; this.load(); },
      error: e => { this.error = e?.error?.message || 'No se pudo crear el plan.'; this.saving = false; }
    });
  }

  addExerciseToDraft(planId: number, exercise: typeof this.exerciseCatalog[number]): void {
    const draft = this.sessionDrafts[planId];
    if (!draft) return;
    const lines = draft.exercisePrescription.trim() ? draft.exercisePrescription.split(/\r?\n/).map(line => line.trim()).filter(Boolean) : [];
    if (lines.some(line => line.toLocaleLowerCase('es').startsWith(exercise.name.toLocaleLowerCase('es')))) {
      this.notice = 'Ese ejercicio ya está incluido en la sesión.';
      return;
    }
    draft.exercisePrescription = [...lines, this.buildExercisePrescription(exercise, this.sessionExerciseSets, this.sessionExerciseReps, this.sessionExerciseRest)].join('\n');
    this.notice = exercise.name + ' añadido a la sesión.';
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
    this.notice = 'Se ha copiado la estructura de «' + saved.title + '». Ajusta fecha y nombre antes de guardarla.';
  }

  generateSessionDraft(plan: TrainingPlan): void {
    const draft = this.sessionDrafts[plan.id];
    if (!draft) return;
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
    }).join('\\n');
    draft.instructions = 'Borrador para nivel ' + (level === 'beginner' ? 'principiante' : level === 'intermediate' ? 'intermedio' : 'avanzado') +
      '. Incluye calentamiento progresivo y vuelta a la calma. Ajusta ejercicios, volumen y esfuerzo al historial, disponibilidad y tolerancia del cliente.';
    if (!draft.duration) draft.duration = this.generatorGoal === 'endurance' ? 30 : 45;
    this.notice = 'Borrador de sesión preparado. Revísalo y personalízalo antes de guardarlo.';
  }

  createSession(plan: TrainingPlan): void {
    const draft = this.sessionDrafts[plan.id];
    if (!draft?.title.trim() || !draft.date) return;
    this.saving = true; this.error = '';
    this.http.post(`/api/training/plans/${plan.id}/sessions`, { scheduledDate: draft.date, title: draft.title.trim(), sessionType: draft.type, durationMinutes: draft.duration, intensityTarget: draft.intensity.trim() || null, instructions: draft.instructions.trim() || null, exercisePrescription: draft.exercisePrescription.trim() || null }).subscribe({
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

  beginEditSession(session: TrainingSession): void {
    if (this.editingSessionId === session.id) { this.editingSessionId = null; this.sessionEditDraft = null; return; }
    this.editingSessionId = session.id;
    this.recordingSessionId = null;
    this.sessionEditDraft = { date: session.scheduledDate, title: session.title, type: session.sessionType,
      duration: session.durationMinutes ?? 60, intensity: session.intensityTarget ?? '', instructions: session.instructions ?? '', exercisePrescription: session.exercisePrescription ?? '' };
  }

  saveSession(session: TrainingSession): void {
    const d = this.sessionEditDraft;
    if (!d || !d.title.trim() || !d.date || d.duration === null || !Number.isFinite(d.duration) || d.duration < 1 || d.duration > 1440) {
      this.error = 'Completa el nombre, la fecha y una duración entre 1 y 1440 minutos.';
      return;
    }
    this.saving = true; this.error = '';
    this.http.put(`/api/training/sessions/${session.id}`, { scheduledDate: d.date, title: d.title.trim(), sessionType: d.type,
      durationMinutes: d.duration, intensityTarget: d.intensity.trim() || null, instructions: d.instructions.trim() || null, exercisePrescription: d.exercisePrescription.trim() || null }).subscribe({
      next: () => { this.editingSessionId = null; this.sessionEditDraft = null; this.saving = false; this.refreshSessions(session.planId); },
      error: e => { this.error = e?.error?.message || 'No se pudo actualizar la sesión.'; this.saving = false; }
    });
  }

  private refreshSessions(planId: number): void {
    this.http.get<TrainingSession[]>(`/api/training/plans/${planId}/sessions`).subscribe({
      next: rows => this.sessions[planId] = rows,
      error: () => this.error = 'La operación se guardó, pero no se pudo actualizar la lista. Recarga la página.'
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

  modelLabel(model: string): string {
    return ({ linear: 'Lineal', undulating: 'Ondulante', block: 'Bloques', reverse: 'Inversa', continuous_competition: 'Competición continua', autoregulatory: 'Autoregulado', custom: 'Personalizado' } as Record<string,string>)[model] || model;
  }
}
