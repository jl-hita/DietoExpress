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
  actualDurationMinutes?: number | null; sessionRpe?: number | null; recordNotes?: string | null;
}
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
      <a class="back-link" [routerLink]="['/clients', clientId]">← Volver al expediente</a>
      <header><div><p class="eyebrow">MÓDULO OPCIONAL</p><h1>Planificación deportiva</h1><p>Actívala solo para clientes que quieran trabajar objetivos de actividad física o entrenamiento.</p></div></header>
      <p *ngIf="loading" role="status">Cargando planificación…</p>
      <p *ngIf="error" class="error" role="alert">{{ error }}</p>
      <p *ngIf="acceptedClientId" role="status">Colaboración aceptada. <a [routerLink]="['/clients', acceptedClientId, 'training']">Abrir el entrenamiento</a></p>
      <section *ngIf="invitations.length" class="panel">
        <h2>Invitaciones de colaboración</h2>
        <p>Revisa las solicitudes de acceso al entrenamiento de otros profesionales. Solo tendrás acceso de lectura.</p>
        <article *ngFor="let invitation of invitations" class="collaboration-row">
          <div><strong>{{ invitation.clientName }}</strong><p>Invita: {{ invitation.invitedBy }} · {{ invitation.createdAt | date:'mediumDate' }}</p></div>
          <button type="button" (click)="acceptInvitation(invitation)" [disabled]="saving">Aceptar invitación</button>
        </article>
      </section>
      <section *ngIf="!loading && !profile" class="panel">
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
                <strong>{{ meso.sequenceNumber }}. {{ meso.name }}</strong><p>{{ meso.startDate | date:'mediumDate' }} <span *ngIf="meso.endDate">— {{ meso.endDate | date:'mediumDate' }}</span></p>
                <p *ngIf="meso.objective">{{ meso.objective }}</p>
                <form *ngIf="current.canManage" class="cycle-form" (ngSubmit)="createMicrocycle(meso)">
                  <label>Nombre<input name="microName{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].name" required maxlength="160" /></label>
                  <label>N.º<input type="number" name="microSeq{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].sequence" required min="1" /></label>
                  <label>Inicio<input type="date" name="microStart{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].startDate" required /></label>
                  <label>Fin<input type="date" name="microEnd{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].endDate" /></label>
                  <label class="cycle-wide">Enfoque<input name="microFocus{{meso.id}}" [(ngModel)]="microcycleDrafts[meso.id].focus" maxlength="500" /></label>
                  <button type="submit" [disabled]="saving">Añadir microciclo</button>
                </form>
                <ul *ngIf="microcycles[meso.id]?.length"><li *ngFor="let micro of microcycles[meso.id]"><strong>{{ micro.sequenceNumber }}. {{ micro.name }}</strong> · {{ micro.startDate | date:'mediumDate' }}<span *ngIf="micro.endDate"> — {{ micro.endDate | date:'mediumDate' }}</span><span *ngIf="micro.focus"> · {{ micro.focus }}</span></li></ul>
              </article>
            </section>
            <form *ngIf="current.canManage" (ngSubmit)="createSession(plan)" class="session-form">
              <label>Fecha<input type="date" name="sessionDate{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].date" required /></label>
              <label>Sesión<input name="sessionTitle{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].title" maxlength="160" placeholder="Ej. Fuerza tren inferior" required /></label>
              <label>Tipo<select name="sessionType{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].type"><option value="endurance">Resistencia</option><option value="strength">Fuerza</option><option value="mobility">Movilidad</option><option value="recovery">Recuperación</option><option value="health">Salud</option><option value="general">General</option><option value="other">Otro</option></select></label>
              <label>Duración (min)<input type="number" name="sessionDuration{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].duration" min="1" max="1440" /></label>
              <button type="submit" [disabled]="saving">Añadir sesión</button>
            </form>
            <div *ngIf="sessions[plan.id]?.length" class="session-list">
              <div *ngFor="let session of sessions[plan.id]" class="session-row">
                <div><strong>{{ session.scheduledDate | date:'mediumDate' }} · {{ session.title }}</strong><p>{{ session.durationMinutes || '—' }} min <span *ngIf="session.intensityTarget">· {{ session.intensityTarget }}</span></p><p *ngIf="session.completedAt" class="muted">Registrada: {{ session.actualDurationMinutes || '—' }} min · RPE {{ session.sessionRpe ?? '—' }}</p></div>
                <button *ngIf="current.canManage" type="button" class="secondary" (click)="openRecord(session)" [disabled]="saving">{{ recordingSessionId === session.id ? 'Cerrar registro' : (session.completedAt ? 'Editar registro' : 'Registrar realización') }}</button>
                <form *ngIf="current.canManage && recordingSessionId === session.id" class="record-form" (ngSubmit)="record(session)">
                  <label>Duración real (min)<input type="number" [(ngModel)]="recordDrafts[session.id].duration" name="actualDuration{{session.id}}" min="1" max="1440" required /></label>
                  <label>Esfuerzo percibido (RPE)<input type="number" [(ngModel)]="recordDrafts[session.id].rpe" name="actualRpe{{session.id}}" min="0" max="10" step="0.5" required /></label>
                  <label>Notas<textarea [(ngModel)]="recordDrafts[session.id].notes" name="recordNotes{{session.id}}" rows="2" maxlength="2000" placeholder="Cómo te has encontrado, ajustes realizados…"></textarea></label>
                  <button type="submit" [disabled]="saving">Guardar realización</button>
                </form>
              </div>
            </div>
          </article>
        </section>
      </ng-container>
    </main>
  `,
  styles: [`
    :host{display:block;color:var(--text-color,#202124)}.training-page{max-width:1100px;margin:0 auto;padding:24px;display:grid;gap:18px}.back-link{color:var(--primary-color,#3657a6);text-decoration:none}header h1{margin:.2rem 0 .4rem;font-size:clamp(1.6rem,3vw,2.2rem)}header p{margin:.3rem 0}.eyebrow{font-size:.75rem;font-weight:700;letter-spacing:.08em;color:var(--primary-color,#3657a6)}.panel,.plan-card{border:1px solid var(--border-color,#ddd);border-radius:14px;padding:20px;background:var(--surface-color,#fff)}.panel h2,.plan-card h3{margin-top:0}.section-heading{display:flex;justify-content:space-between;align-items:flex-start;gap:12px}.section-heading p,.plan-card p{color:var(--secondary-text-color,#666);margin:.4rem 0}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px;margin:16px 0}.form-grid label,.session-form label{display:grid;gap:6px;font-weight:600;font-size:.9rem}.form-grid .full{grid-column:1/-1}.form-grid input,.form-grid select,.form-grid textarea,.session-form input,.session-form select{width:100%;box-sizing:border-box;border:1px solid #b9bec7;border-radius:8px;padding:10px;font:inherit;background:transparent;color:inherit}.cycle-section{margin:16px 0;padding:14px;border:1px solid var(--border-color,#ddd);border-radius:10px}.cycle-section h4{margin:.2rem 0 .8rem}.cycle-form{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:10px;align-items:end;margin:12px 0}.cycle-form label{display:grid;gap:6px;font-size:.86rem;font-weight:600}.cycle-form input{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:transparent;color:inherit}.cycle-wide{grid-column:span 2}.mesocycle-card{padding:12px 0;border-top:1px solid var(--border-color,#ddd)}.invite-form{display:flex;gap:10px;align-items:end;flex-wrap:wrap;margin:14px 0}.invite-form label{display:grid;gap:6px;flex:1 1 240px;font-size:.9rem;font-weight:600}.invite-form input{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:10px;font:inherit;background:transparent;color:inherit}.collaboration-row{display:flex;justify-content:space-between;align-items:center;gap:12px;padding:12px 0;border-top:1px solid var(--border-color,#ddd)}.collaboration-row p{margin:.35rem 0;color:var(--secondary-text-color,#666)}.session-form{display:flex;flex-wrap:wrap;gap:10px;align-items:end;margin:16px 0}.session-form label{flex:1 1 135px}.record-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin-top:12px;padding:14px;border-radius:10px;background:var(--surface-container,#f6f7f9)}.record-form label{display:grid;gap:6px;font-size:.88rem;font-weight:600}.record-form label:nth-child(3){grid-column:1/-1}.record-form input,.record-form textarea{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:var(--surface-color,#fff);color:inherit}.record-form button{justify-self:start}.session-row{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:12px;align-items:start;padding:12px 0;border-top:1px solid var(--border-color,#ddd)}.session-form button{flex:0 0 auto}button{border:0;border-radius:8px;padding:10px 14px;background:var(--primary-color,#3657a6);color:white;font:inherit;font-weight:600;cursor:pointer}button:disabled{opacity:.55;cursor:wait}button.secondary{background:transparent;border:1px solid #9aa0a6;color:inherit}.plan-card{margin-top:14px}.status{font-size:.8rem;border-radius:20px;background:#eef2ff;padding:5px 10px}.session-list{border-top:1px solid #ddd;margin-top:14px}.session-row{display:grid;grid-template-columns:minmax(0,1fr) auto;gap:12px;align-items:start;padding:12px 0;border-bottom:1px solid #eee}.record-form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin-top:12px;padding:14px;border-radius:10px;background:var(--surface-container,#f6f7f9)}.record-form label{display:grid;gap:6px;font-size:.88rem;font-weight:600}.record-form label:nth-child(3){grid-column:1/-1}.record-form input,.record-form textarea{box-sizing:border-box;width:100%;border:1px solid #b9bec7;border-radius:8px;padding:9px;font:inherit;background:var(--surface-color,#fff);color:inherit}.record-form button{justify-self:start}.session-row p{margin:.3rem 0}.muted{color:#777}.error{color:#a21d24}@media(max-width:650px){.training-page{padding:14px}.collaboration-row{align-items:stretch;flex-direction:column}.cycle-form{grid-template-columns:repeat(2,minmax(0,1fr))}.cycle-wide{grid-column:1/-1}.cycle-form button{grid-column:1/-1}.invite-form button{width:100%}.form-grid{grid-template-columns:1fr}.form-grid .full{grid-column:auto}.section-heading{flex-direction:column;align-items:stretch}.session-row{grid-template-columns:1fr}.record-form{grid-template-columns:1fr}.record-form label:nth-child(3){grid-column:auto}.session-form button{width:100%}}
  `]
})
export class TrainingPeriodizationComponent implements OnInit {
  clientId = 0;
  loading = true;
  saving = false;
  error = '';
  recordingSessionId: number | null = null;
  recordDrafts: Record<number, { duration: number | null; rpe: number | null; notes: string }> = {};
  collaborations: TrainingCollaboration[] = [];
  invitations: TrainingInvitation[] = [];
  mesocycles: Record<number, TrainingMesocycle[]> = {};
  microcycles: Record<number, TrainingMicrocycle[]> = {};
  mesocycleDrafts: Record<number, { name: string; sequence: number; startDate: string; endDate: string; objective: string }> = {};
  microcycleDrafts: Record<number, { name: string; sequence: number; startDate: string; endDate: string; focus: string }> = {};
  collaboratorEmail = '';
  acceptedClientId: number | null = null;
  profile: TrainingProfile | null = null;
  trainingManagementMode: 'none' | 'self' | 'collaboration' = 'self';
  plans: TrainingPlan[] = [];
  sessions: Record<number, TrainingSession[]> = {};
  sessionDrafts: Record<number, { date: string; title: string; type: string; duration: number | null }> = {};
  planName = '';
  periodizationModel = 'custom';
  startDate = new Date().toISOString().slice(0, 10);
  endDate = '';
  planNotes = '';

  constructor(private route: ActivatedRoute, private http: HttpClient) {}

  ngOnInit(): void {
    this.clientId = Number(this.route.snapshot.paramMap.get('id'));
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
        next: plans => { this.plans = plans; this.sessionDrafts = {}; for (const p of plans) this.sessionDrafts[p.id] = { date: new Date().toISOString().slice(0,10), title: '', type: 'general', duration: null }; this.loadSessions(plans); this.loading = false; },
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
    this.http.post(`/api/training/plans/${plan.id}/sessions`, { scheduledDate: draft.date, title: draft.title, sessionType: draft.type, durationMinutes: draft.duration, intensityTarget: null, instructions: null }).subscribe({
      next: () => { draft.title = ''; this.saving = false; this.http.get<TrainingSession[]>(`/api/training/plans/${plan.id}/sessions`).subscribe(rows => this.sessions[plan.id] = rows); },
      error: e => { this.error = e?.error?.message || 'No se pudo crear la sesión.'; this.saving = false; }
    });
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
