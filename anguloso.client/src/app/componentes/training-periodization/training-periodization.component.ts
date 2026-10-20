import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';

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

@Component({
  selector: 'app-training-periodization',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  template: `
    <main class="training-page">
      <a *ngIf="clientId > 0" class="back-link" [routerLink]="['/clients', clientId]">← Volver al expediente</a>
      <header><div><p class="eyebrow">MÓDULO OPCIONAL</p><h1>{{ clientId > 0 ? 'Planificación deportiva' : 'Invitaciones de entrenamiento' }}</h1><p *ngIf="clientId > 0">Actívala solo para clientes que quieran trabajar objetivos de actividad física o entrenamiento.</p></div></header>
      <p *ngIf="loading" role="status">Cargando planificación…</p>
      <p *ngIf="error" class="error" role="alert">{{ error }}</p>
      <p *ngIf="acceptedClientId" role="status">Colaboración aceptada. <a [routerLink]="['/clients', acceptedClientId, 'training']">Abrir el entrenamiento</a></p>
      <section *ngIf="clientId === 0 && !invitations.length && !loading" class="panel">
        <h2>No tienes invitaciones pendientes</h2>
        <p>Cuando otro profesional te invite a colaborar en un plan deportivo, aparecerá aquí.</p>
      </section>
      <section *ngIf="invitations.length" class="panel">
        <h2>Invitaciones de colaboración</h2>
        <p>Revisa las solicitudes de acceso al entrenamiento de otros profesionales. Solo tendrás acceso de lectura.</p>
        <article *ngFor="let invitation of invitations" class="collaboration-row">
          <div><strong>{{ invitation.clientName }}</strong><p>Invita: {{ invitation.invitedBy }} · {{ invitation.createdAt | date:'mediumDate' }}</p></div>
          <button type="button" (click)="acceptInvitation(invitation)" [disabled]="saving">Aceptar invitación</button>
        </article>
      </section>
      <section *ngIf="clientId > 0 && !loading && !profile" class="panel">
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
            <label>Fecha inicial<input type="date" name="startDate" [(ngModel)]="startDate" required /></label>
            <label>Fecha final (opcional)<input type="date" name="endDate" [(ngModel)]="endDate" /></label>
            <label class="full">Notas<textarea name="planNotes" [(ngModel)]="planNotes" rows="2"></textarea></label>
            <div class="full"><button type="submit" [disabled]="saving">Crear plan</button></div>
          </form>
          <p *ngIf="plans.length === 0" class="muted">Todavía no hay planes de entrenamiento.</p>
          <article *ngFor="let plan of plans" class="plan-card">
            <div class="section-heading"><div><h3>{{ plan.name }}</h3><p>{{ plan.startDate | date:'mediumDate' }} <span *ngIf="plan.endDate">— {{ plan.endDate | date:'mediumDate' }}</span> · {{ modelLabel(plan.periodizationModel) }}</p></div><span class="status">{{ plan.status }}</span></div>
            <p *ngIf="plan.notes">{{ plan.notes }}</p>
            <section class="cycle-section">
              <h4>Mesociclos</h4>
              <form *ngIf="current.canManage" class="cycle-form" (ngSubmit)="createMesocycle(plan)">
                <label>Nombre<input name="mesoName{{plan.id}}" [(ngModel)]="mesocycleDrafts[plan.id].name" required maxlength="160" /></label>
                <label>N.º<input type="number" name="mesoSeq{{plan.id}}" [(ngModel)]="mesocycleDrafts[plan.id].sequence" required min="1" /></label>
                <label>Inicio<input type="date" name="mesoStart{{plan.id}}" [(ngModel)]="mesocycleDrafts[plan.id].startDate" required /></label>
                <label>Fin<input type="date" name="mesoEnd{{plan.id}}" [(ngModel)]="mesocycleDrafts[plan.id].endDate" /></label>
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
                    <label>Inicio<input type="date" name="editMesoStart{{meso.id}}" [(ngModel)]="edit.startDate" required /></label>
                    <label>Fin<input type="date" name="editMesoEnd{{meso.id}}" [(ngModel)]="edit.endDate" /></label>
                    <label class="cycle-wide">Objetivo<input name="editMesoObjective{{meso.id}}" [(ngModel)]="edit.objective" maxlength="500" /></label>
                    <label class="cycle-wide">Notas<textarea name="editMesoNotes{{meso.id}}" [(ngModel)]="edit.notes" rows="2"></textarea></label>
                    <button type="submit" [disabled]="saving">Guardar cambios</button><button type="button" class="secondary" (click)="editingMesocycleId = null; mesocycleEditDraft = null">Cancelar</button>
                  </form>
                </ng-template>
                <form *ngIf="current.canManage" class="cycle-form" (ngSubmit)="createMicrocycle(meso)">
                  <label>Nombre<input name="microName{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].name" required maxlength="160" /></label>
                  <label>N.º<input type="number" name="microSeq{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].sequence" required min="1" /></label>
                  <label>Inicio<input type="date" name="microStart{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].startDate" required /></label>
                  <label>Fin<input type="date" name="microEnd{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].endDate" /></label>
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
                      <label>Inicio<input type="date" name="editMicroStart{{micro.id}}" [(ngModel)]="edit.startDate" required /></label>
                      <label>Fin<input type="date" name="editMicroEnd{{micro.id}}" [(ngModel)]="edit.endDate" /></label>
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
                <span class="session-count">{{ sessions[plan.id]?.length || 0 }} sesiones</span>
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
                <div class="session-editor-grid">
                  <label>Nombre de la sesión<input name="sessionTitle{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].title" maxlength="160" placeholder="Ej. Fuerza de tren inferior" required /></label>
                  <label>Fecha<input type="date" name="sessionDate{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].date" [min]="plan.startDate" [max]="plan.endDate || null" required /></label>
                  <label>Tipo de entrenamiento<select name="sessionType{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].type"><option value="endurance">Resistencia</option><option value="strength">Fuerza</option><option value="mobility">Movilidad</option><option value="recovery">Recuperación</option><option value="health">Salud</option><option value="general">General</option><option value="other">Otro</option></select></label>
                  <label>Duración estimada (min)<input type="number" name="sessionDuration{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].duration" min="1" max="1440" placeholder="60" /></label>
                  <label>Intensidad / zona objetivo<input name="sessionIntensity{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].intensity" maxlength="120" placeholder="Ej. RPE 7 · Z2 · ritmo cómodo" /></label>
                  <label class="session-editor-wide">Instrucciones<textarea name="sessionInstructions{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].instructions" rows="2" maxlength="4000" placeholder="Calentamiento, bloques principales, pausas y vuelta a la calma…"></textarea></label>
                  <label class="session-editor-wide">Prescripción de ejercicios<textarea name="sessionExercises{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].exercisePrescription" rows="3" maxlength="6000" placeholder="Sentadilla · 3 × 8 · descanso 2 min · RPE 7&#10;Peso muerto rumano · 3 × 10 · descanso 90 s"></textarea><small>Indica ejercicio, series, repeticiones, descanso y observaciones. Puedes anotar ritmo o potencia en la zona objetivo.</small></label>
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
                        <label>Fecha<input type="date" name="editSessionDate{{session.id}}" [(ngModel)]="edit.date" [min]="plan.startDate" [max]="plan.endDate || null" required /></label>
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
    :host{display:block;color:var(--text-color,#202124)}.cycle-actions{display:flex;gap:6px;flex-wrap:wrap}.cycle-actions button{padding:5px 9px}.training-page{max-width:1100px;margin:0 auto;padding:24px;display:grid;gap:18px}.back-link{color:var(--primary-color,#3657a6);text-decoration:none}header h1{margin:.2rem 0 .4rem;font-size:clamp(1.6rem,3vw,2.2rem)}header p{margin:.3rem 0}.eyebrow{font-size:.75rem;font-weight:700;letter-spacing:.08em;color:var(--primary-color,#3657a6)}.panel,.plan-card{border:1px solid var(--border-color,#ddd);border-radius:14px;padding:20px;background:var(--surface-color,#fff)}.panel h2,.plan-card h3{margin-top:0}.section-heading{display:flex;justify-content:space-between;align-items:flex-start;gap:12px}.section-heading p,.plan-card p{color:var(--secondary-text-color,#666);margin:.4rem 0}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px;margin:16px 0}.form-grid label,.session-form label{display:grid;gap:6px;font-weight:600;font-size:.9rem}.form-grid .full{grid-column:1/-1}.form-grid input,.form-grid select,.form-grid textarea,.session-form input,.session-form select{width:100%;box-sizing:border-box;border:1px solid #b9bec7;border-radius:8px;padding:10px;font:inherit;background:transparent;color:inherit}.cycle-section{margin:16px 0;padding:14px;border:1px solid var(--border-color,#ddd);border-radius:10px}.cycle-section h4{margin:.2rem 0 .8rem}.cycle-form{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;align-items:end;margin:12px 0}.cycle-form label{display:grid;gap:6px;font-size:.86rem;font-weight:600}.cycle-form input{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:transparent;color:inherit}.cycle-wide{grid-column:span 2}.mesocycle-card{padding:12px 0;border-top:1px solid var(--border-color,#ddd)}.invite-form{display:flex;gap:10px;align-items:end;flex-wrap:wrap;margin:14px 0}.invite-form label{display:grid;gap:6px;flex:1 1 240px;font-size:.9rem;font-weight:600}.invite-form input{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:10px;font:inherit;background:transparent;color:inherit}.collaboration-row{display:flex;justify-content:space-between;align-items:center;gap:12px;padding:12px 0;border-top:1px solid var(--border-color,#ddd)}.collaboration-row p{margin:.35rem 0;color:var(--secondary-text-color,#666)}.calendar-toolbar{display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;margin:14px 0}.view-switch{display:flex;gap:3px;padding:4px;border:1px solid var(--border-color,#ddd);border-radius:11px;background:var(--surface-container,#f3f4f8)}.view-switch button{border:0;background:transparent;color:inherit;border-radius:8px;padding:8px 12px;font-weight:700;cursor:pointer}.view-switch button.active{background:var(--surface-color,#fff);color:var(--primary-color,#3657a6);box-shadow:0 1px 4px #00000012}.week-navigation{display:flex;align-items:center;gap:8px;flex-wrap:wrap}.week-navigation strong{font-size:.9rem;min-width:150px;text-align:center}.week-summary{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px;margin:14px 0}.week-summary>div{display:flex;align-items:center;gap:10px;padding:12px;border:1px solid var(--border-color,#e0e3e9);border-radius:11px;background:var(--surface-color,#fff)}.week-summary>div>span:last-child{display:grid;gap:3px}.week-summary strong{font-size:1.05rem}.week-summary small{font-size:.76rem;color:var(--secondary-text-color,#666)}.summary-dot{width:10px;height:10px;border-radius:50%;flex:0 0 10px}.planned-dot{background:#6b7ed1}.minutes-dot{background:#d29b3c}.completed-dot{background:#36876a}.week-grid{display:grid;grid-template-columns:repeat(7,minmax(0,1fr));gap:7px;align-items:stretch;margin:14px 0}.week-day{min-width:0;min-height:125px;padding:8px;border:1px solid var(--border-color,#e0e3e9);border-radius:11px;background:var(--surface-color,#fff)}.week-day.today{border:2px solid var(--primary-color,#3657a6);padding:7px;background:color-mix(in srgb,var(--primary-color,#3657a6) 4%,var(--surface-color,#fff))}.week-day-heading{display:flex;align-items:center;justify-content:space-between;gap:4px;padding-bottom:8px;border-bottom:1px solid var(--border-color,#e7e8ed)}.week-day-heading span{text-transform:uppercase;font-size:.68rem;letter-spacing:.04em;color:var(--secondary-text-color,#666);font-weight:800}.week-day-heading strong{display:grid;place-items:center;width:27px;height:27px;border-radius:50%;font-size:.9rem}.week-day.today .week-day-heading strong{background:var(--primary-color,#3657a6);color:white}.week-day-empty{display:grid;place-items:center;min-height:65px;color:var(--secondary-text-color,#a3a6ad)}.week-session{display:grid;gap:5px;margin-top:8px;padding:8px 7px;border-radius:8px;background:#f0f1ff;border-left:3px solid #6b7ed1;color:#262c45;font-size:.75rem;overflow-wrap:anywhere}.week-strength{background:#f0eaff;border-left-color:#8c67c2}.week-endurance{background:#e5f5ed;border-left-color:#3d9a70}.week-mobility{background:#e3f4f8;border-left-color:#3d91a6}.week-recovery{background:#eeedfa;border-left-color:#8075bb}.week-health{background:#ffedf1;border-left-color:#d26b85}.week-session-done{opacity:.78;border-left-color:#36876a}.week-session-type{font-size:1rem;font-weight:800}.week-session>strong{font-size:.78rem;line-height:1.25}.week-session-meta,.week-session-status{font-size:.68rem;line-height:1.3}.week-session-status{font-weight:750}.week-session-actions{display:grid;grid-template-columns:1fr;gap:4px;margin-top:3px}.week-session-actions button{border:1px solid #c7cbd7;border-radius:6px;background:#ffffffb8;padding:5px 3px;color:#333;font-size:.68rem;font-weight:750;cursor:pointer}.session-workspace{margin-top:24px;padding-top:20px;border-top:1px solid var(--border-color,#ddd)}.session-workspace-heading{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-bottom:16px}.session-workspace-heading h4{font-size:1.25rem;margin:.15rem 0}.session-workspace-heading p:not(.eyebrow){margin:.35rem 0;color:var(--secondary-text-color,#666)}.session-count{white-space:nowrap;border:1px solid var(--border-color,#ddd);border-radius:99px;padding:6px 11px;font-size:.8rem;font-weight:700}.session-editor{padding:18px;border:1px solid var(--border-color,#d6dce8);border-radius:14px;background:linear-gradient(135deg,var(--surface-container,#f7f8fc),var(--surface-color,#fff));margin:14px 0 18px}.editor-title{display:flex;align-items:center;gap:12px;margin-bottom:16px}.editor-title strong{font-size:1rem}.editor-title p{margin:3px 0 0;color:var(--secondary-text-color,#666);font-size:.88rem}.editor-icon{display:grid;place-items:center;width:40px;height:40px;border-radius:12px;background:var(--primary-color,#3657a6);color:#fff;font-size:1.5rem}.session-editor-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:13px}.session-editor-grid label{display:grid;gap:6px;font-size:.87rem;font-weight:650}.session-editor-grid input,.session-editor-grid select,.session-editor-grid textarea{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:9px;padding:10px 11px;font:inherit;background:var(--surface-color,#fff);color:inherit;min-width:0}.session-editor-grid input:focus,.session-editor-grid select:focus,.session-editor-grid textarea:focus{outline:2px solid color-mix(in srgb,var(--primary-color,#3657a6) 30%,transparent);outline-offset:1px}.session-editor-wide{grid-column:1/-1}.editor-footer{display:flex;align-items:center;justify-content:space-between;gap:12px;margin-top:15px}.editor-footer>span{font-size:.78rem;color:var(--secondary-text-color,#666)}.session-empty{padding:20px;border:1px dashed var(--border-color,#c9ced8);border-radius:12px;color:var(--secondary-text-color,#666);text-align:center}.session-list{display:grid;gap:10px;margin-top:14px}.session-card{display:grid;grid-template-columns:58px minmax(0,1fr);gap:14px;padding:16px;border:1px solid var(--border-color,#dfe2e8);border-radius:14px;background:var(--surface-color,#fff);transition:border-color .15s,box-shadow .15s}.session-card:hover{border-color:color-mix(in srgb,var(--primary-color,#3657a6) 45%,var(--border-color,#ddd));box-shadow:0 4px 18px #0000000a}.session-completed{border-left:4px solid #36876a}.session-date-rail{display:flex;flex-direction:column;align-items:center;justify-content:center;align-self:start;min-height:58px;border-radius:11px;background:var(--surface-container,#f2f4f8);color:var(--primary-color,#3657a6)}.session-date-rail strong{font-size:1.35rem;line-height:1.2}.session-date-rail span{text-transform:uppercase;font-size:.7rem;font-weight:800;letter-spacing:.06em}.session-card-main{min-width:0}.session-card-top{display:flex;align-items:center;gap:11px}.session-type-icon{flex:0 0 38px;width:38px;height:38px;display:grid;place-items:center;border-radius:11px;background:#e9edff;color:#334b9b;font-size:1.25rem;font-weight:800}.type-endurance{background:#e2f3ec;color:#176b4b}.type-strength{background:#eee8ff;color:#6841a5}.type-mobility{background:#e1f3f8;color:#17677b}.type-recovery{background:#e9e8f8;color:#514b94}.type-health{background:#ffe8ee;color:#a23c5a}.session-title-block{flex:1;min-width:0}.session-title-line{display:flex;align-items:center;gap:8px;flex-wrap:wrap}.session-title-line h5{margin:0;font-size:1rem;line-height:1.35}.session-title-block p{margin:5px 0 0;font-size:.83rem;color:var(--secondary-text-color,#666)}.session-status{font-size:.72rem;font-weight:750;border-radius:99px;padding:4px 8px;background:#edf0f5;color:#555}.session-status.done{background:#dff3e9;color:#176b4b}.session-edit-button{flex:0 0 auto}.session-instructions{white-space:pre-line;margin:12px 0 0;padding:11px 13px;border-radius:9px;background:var(--surface-container,#f5f6f9);font-size:.88rem;line-height:1.5}.completion-summary{display:flex;align-items:center;gap:9px;margin-top:12px;padding:10px 12px;border-radius:10px;background:#edf8f2}.completion-check{display:grid;place-items:center;width:27px;height:27px;border-radius:50%;background:#36876a;color:white;font-weight:800}.completion-summary>span:nth-child(2){display:grid;gap:2px;flex:1}.completion-summary strong{font-size:.82rem;color:#205e46}.completion-summary small{font-size:.78rem;color:#39715b}.duration-delta{font-size:.78rem;font-weight:750;color:#39715b}.session-card-actions{display:flex;justify-content:flex-end;margin-top:10px}.session-edit-form{margin-top:14px;padding:14px;border-radius:11px;border:1px solid var(--border-color,#d8dce5);background:var(--surface-container,#f6f7fa)}.session-edit-form .editor-footer{align-items:center}.session-editor-grid textarea{resize:vertical}.record-form{grid-template-columns:repeat(2,minmax(0,1fr))}.record-form .record-notes{grid-column:1/-1}.record-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin-top:12px;padding:14px;border-radius:10px;background:var(--surface-container,#f6f7f9)}.record-form label{display:grid;gap:6px;font-size:.88rem;font-weight:600}.record-form label:nth-child(3){grid-column:1/-1}.record-form input,.record-form textarea{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:var(--surface-color,#fff);color:inherit}.record-form button{justify-self:start}.session-row{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:12px;align-items:start;padding:12px 0;border-top:1px solid var(--border-color,#ddd)}.session-form button{flex:0 0 auto}button{border:0;border-radius:8px;padding:10px 14px;background:var(--primary-color,#3657a6);color:white;font:inherit;font-weight:600;cursor:pointer}button:disabled{opacity:.55;cursor:wait}button.secondary{background:transparent;border:1px solid #9aa0a6;color:inherit}.plan-card{margin-top:14px}.status{font-size:.8rem;border-radius:20px;background:#eef2ff;padding:5px 10px}.session-list{border-top:1px solid #ddd;margin-top:14px}.session-row{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:12px;align-items:start;padding:12px 0;border-bottom:1px solid #eee}.record-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin-top:12px;padding:14px;border-radius:10px;background:var(--surface-container,#f6f7f9)}.record-form label{display:grid;gap:6px;font-size:.88rem;font-weight:600}.record-form label:nth-child(3){grid-column:1/-1}.record-form input,.record-form textarea{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:var(--surface-color,#fff);color:inherit}.record-form button{justify-self:start}.session-row p{margin:.3rem 0}.muted{color:#777}.error{color:#a21d24}@media(max-width:650px){.training-page{padding:14px}.collaboration-row{align-items:stretch;flex-direction:column}.cycle-form{grid-template-columns:repeat(2,minmax(0,1fr))}.cycle-wide{grid-column:1/-1}.cycle-form button{grid-column:1/-1}.invite-form button{width:100%}.form-grid{grid-template-columns:1fr}.form-grid .full{grid-column:auto}.section-heading{flex-direction:column;align-items:stretch}.week-grid{grid-template-columns:repeat(2,minmax(0,1fr))}.week-summary{grid-template-columns:1fr}.week-summary>div{padding:10px}.week-day{min-height:100px}.week-navigation{width:100%;justify-content:space-between}.week-navigation strong{min-width:0;flex:1}.session-editor-grid{grid-template-columns:1fr}.session-editor-wide{grid-column:auto}.editor-footer{align-items:stretch;flex-direction:column}.editor-footer button{width:100%}.session-workspace-heading{align-items:flex-start}.session-card{grid-template-columns:42px minmax(0,1fr);gap:10px;padding:11px}.session-date-rail{min-height:48px}.session-date-rail strong{font-size:1.1rem}.session-type-icon{width:32px;height:32px;flex-basis:32px}.session-card-top{align-items:flex-start;flex-wrap:wrap}.session-edit-button{margin-left:auto}.session-title-block{flex-basis:calc(100% - 48px)}.session-card-actions{justify-content:stretch}.session-card-actions button{width:100%}.record-form{grid-template-columns:1fr}.record-form .record-notes{grid-column:auto}.record-form button{width:100%}.session-edit-form .editor-footer>div{display:grid;gap:8px}.session-edit-form .editor-footer button{width:100%}.cycle-actions{width:100%}}
  `]
})
export class TrainingPeriodizationComponent implements OnInit {
  clientId = 0;
  loading = true;
  saving = false;
  error = '';
  recordingSessionId: number | null = null;
  editingSessionId: number | null = null;
  sessionEditDraft: { date: string; title: string; type: string; duration: number | null; intensity: string; instructions: string; exercisePrescription: string } | null = null;
  recordDrafts: Record<number, { duration: number | null; rpe: number | null; notes: string }> = {};
  collaborations: TrainingCollaboration[] = [];
  invitations: TrainingInvitation[] = [];
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
  planName = '';
  periodizationModel = 'custom';
  startDate = new Date().toISOString().slice(0, 10);
  endDate = '';
  planNotes = '';

  constructor(private route: ActivatedRoute, private http: HttpClient) {}

  ngOnInit(): void {
    const routeId = this.route.snapshot.paramMap.get('id');
    if (routeId === null) {
      this.clientId = 0;
      this.http.get<TrainingInvitation[]>('/api/training/collaborations/invitations')
        .pipe(catchError(() => of([] as TrainingInvitation[])))
        .subscribe(items => { this.invitations = items; this.loading = false; });
      return;
    }
    this.clientId = Number(routeId);
    if (!Number.isInteger(this.clientId) || this.clientId < 1) { this.error = 'Identificador de cliente no válido.'; this.loading = false; return; }
    this.loadInvitations();
    this.load();
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
    this.http.post('/api/training/clients/' + this.clientId + '/collaborations', { email, accessScope: 'training' }).subscribe({
      next: () => { this.collaboratorEmail = ''; this.saving = false; this.loadCollaborations(); },
      error: e => { this.error = e?.error?.message || 'No se pudo enviar la invitación.'; this.saving = false; }
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
    this.loading = true; this.error = '';
    this.http.get<TrainingProfile>(`/api/training/clients/${this.clientId}/profile`).pipe(catchError(() => of(null))).subscribe(profile => {
      this.profile = profile;
      if (!profile) { this.loading = false; return; }
      this.trainingManagementMode = profile.managementMode ?? (profile.enabled ? 'self' : 'none');
      if (profile.enabled && this.trainingManagementMode === 'collaboration') this.loadCollaborations();
      else this.collaborations = [];
      if (!profile.enabled) { this.loading = false; return; }
      this.http.get<TrainingPlan[]>(`/api/training/clients/${this.clientId}/plans`).subscribe({
        next: plans => { this.plans = plans; this.sessionDrafts = {}; for (const p of plans) this.sessionDrafts[p.id] = { date: this.defaultSessionDate(p), title: '', type: 'strength', duration: 60, intensity: '', instructions: '', exercisePrescription: '' }; this.loadSessions(plans); this.loading = false; },
        error: () => { this.error = 'No se pudieron cargar los planes.'; this.loading = false; }
      });
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
        this.http.get<TrainingLoadWeek[]>(\`/api/training/plans/\${plan.id}/load-trend\`)
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

  createSession(plan: TrainingPlan): void {
    const draft = this.sessionDrafts[plan.id];
    if (!draft?.title.trim() || !draft.date) return;
    this.saving = true; this.error = '';
    this.http.post(`/api/training/plans/${plan.id}/sessions`, { scheduledDate: draft.date, title: draft.title.trim(), sessionType: draft.type, durationMinutes: draft.duration, intensityTarget: draft.intensity.trim() || null, instructions: draft.instructions.trim() || null, exercisePrescription: draft.exercisePrescription.trim() || null }).subscribe({
      next: () => { draft.title = ''; draft.intensity = ''; draft.instructions = ''; draft.exercisePrescription = ''; this.saving = false; this.refreshSessions(plan.id); },
      error: e => { this.error = e?.error?.message || 'No se pudo crear la sesión.'; this.saving = false; }
    });
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
