import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, forkJoin, of } from 'rxjs';

interface TrainingProfile {
  id: number; clientId: number; enabled: boolean; primaryGoal: string; experienceLevel: string; notes?: string | null;
}
interface TrainingPlan {
  id: number; name: string; periodizationModel: string; startDate: string; endDate?: string | null; status: string; version: number; notes?: string | null;
}
interface TrainingSession {
  id: number; planId: number; scheduledDate: string; title: string; sessionType: string; durationMinutes?: number | null;
  intensityTarget?: string | null; instructions?: string | null; status: string; completedAt?: string | null;
  actualDurationMinutes?: number | null; sessionRpe?: number | null; recordNotes?: string | null;
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
      <section *ngIf="!loading && !profile" class="panel">
        <h2>Este cliente no utiliza el módulo deportivo</h2>
        <p>La planificación deportiva es opcional. Activarla no modifica el plan nutricional ni obliga al cliente a entrenar.</p>
        <button type="button" (click)="activate()" [disabled]="saving">Activar para este cliente</button>
      </section>
      <ng-container *ngIf="profile as current">
        <section class="panel">
          <div class="section-heading"><div><h2>Perfil deportivo</h2><p>El expediente nutricional permanece disponible aunque desactives este módulo.</p></div><button type="button" class="secondary" (click)="toggleEnabled()" [disabled]="saving">{{ current.enabled ? 'Desactivar módulo' : 'Reactivar módulo' }}</button></div>
          <div class="form-grid">
            <label>Objetivo principal<select [(ngModel)]="current.primaryGoal" name="primaryGoal"><option value="endurance">Resistencia</option><option value="strength">Fuerza</option><option value="hypertrophy">Hipertrofia</option><option value="weight_loss">Pérdida de peso</option><option value="health">Salud</option><option value="general_fitness">Condición física general</option><option value="other">Otro</option></select></label>
            <label>Experiencia<select [(ngModel)]="current.experienceLevel" name="experience"><option value="beginner">Principiante</option><option value="intermediate">Intermedio</option><option value="advanced">Avanzado</option><option value="unknown">Sin evaluar</option></select></label>
            <label>Gestión del entrenamiento<select [(ngModel)]="trainingManagementMode" name="trainingManagementMode"><option value="self">Lo llevo yo</option><option value="collaboration">En colaboración con otro profesional</option><option value="none">Solo seguimiento nutricional</option></select></label>
            <label class="full">Notas<textarea [(ngModel)]="current.notes" name="notes" rows="2"></textarea></label>
          </div>
          <button type="button" (click)="saveProfile()" [disabled]="saving">Guardar perfil</button>
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
            <form (ngSubmit)="createSession(plan)" class="session-form">
              <label>Fecha<input type="date" name="sessionDate{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].date" required /></label>
              <label>Sesión<input name="sessionTitle{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].title" maxlength="160" placeholder="Ej. Fuerza tren inferior" required /></label>
              <label>Tipo<select name="sessionType{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].type"><option value="endurance">Resistencia</option><option value="strength">Fuerza</option><option value="mobility">Movilidad</option><option value="recovery">Recuperación</option><option value="health">Salud</option><option value="general">General</option><option value="other">Otro</option></select></label>
              <label>Duración (min)<input type="number" name="sessionDuration{{plan.id}}" [(ngModel)]="sessionDrafts[plan.id].duration" min="1" max="1440" /></label>
              <button type="submit" [disabled]="saving">Añadir sesión</button>
            </form>
            <div *ngIf="sessions[plan.id]?.length" class="session-list">
              <div *ngFor="let session of sessions[plan.id]" class="session-row">
                <div><strong>{{ session.scheduledDate | date:'mediumDate' }} · {{ session.title }}</strong><p>{{ session.durationMinutes || '—' }} min <span *ngIf="session.intensityTarget">· {{ session.intensityTarget }}</span></p><p *ngIf="session.completedAt" class="muted">Registrada: {{ session.actualDurationMinutes || '—' }} min · RPE {{ session.sessionRpe ?? '—' }}</p></div>
                <button type="button" class="secondary" (click)="record(session)" [disabled]="saving">Registrar realización</button>
              </div>
            </div>
          </article>
        </section>
      </ng-container>
    </main>
  `,
  styles: [`
    :host{display:block;color:var(--text-color,#202124)}.training-page{max-width:1100px;margin:0 auto;padding:24px;display:grid;gap:18px}.back-link{color:var(--primary-color,#3657a6);text-decoration:none}header h1{margin:.2rem 0 .4rem;font-size:clamp(1.6rem,3vw,2.2rem)}header p{margin:.3rem 0}.eyebrow{font-size:.75rem;font-weight:700;letter-spacing:.08em;color:var(--primary-color,#3657a6)}.panel,.plan-card{border:1px solid var(--border-color,#ddd);border-radius:14px;padding:20px;background:var(--surface-color,#fff)}.panel h2,.plan-card h3{margin-top:0}.section-heading{display:flex;justify-content:space-between;align-items:flex-start;gap:12px}.section-heading p,.plan-card p{color:var(--secondary-text-color,#666);margin:.4rem 0}.form-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:14px;margin:16px 0}.form-grid label,.session-form label{display:grid;gap:6px;font-weight:600;font-size:.9rem}.form-grid .full{grid-column:1/-1}.form-grid input,.form-grid select,.form-grid textarea,.session-form input,.session-form select{width:100%;box-sizing:border-box;border:1px solid #b9bec7;border-radius:8px;padding:10px;font:inherit;background:transparent;color:inherit}.session-form{display:flex;flex-wrap:wrap;gap:10px;align-items:end;margin:16px 0}.session-form label{flex:1 1 135px}.session-form button{flex:0 0 auto}button{border:0;border-radius:8px;padding:10px 14px;background:var(--primary-color,#3657a6);color:white;font:inherit;font-weight:600;cursor:pointer}button:disabled{opacity:.55;cursor:wait}button.secondary{background:transparent;border:1px solid #9aa0a6;color:inherit}.plan-card{margin-top:14px}.status{font-size:.8rem;border-radius:20px;background:#eef2ff;padding:5px 10px}.session-list{border-top:1px solid #ddd;margin-top:14px}.session-row{display:flex;align-items:center;justify-content:space-between;gap:12px;padding:12px 0;border-bottom:1px solid #eee}.session-row p{margin:.3rem 0}.muted{color:#777}.error{color:#a21d24}@media(max-width:650px){.training-page{padding:14px}.form-grid{grid-template-columns:1fr}.form-grid .full{grid-column:auto}.section-heading,.session-row{flex-direction:column;align-items:stretch}.session-form button{width:100%}}
  `]
})
export class TrainingPeriodizationComponent implements OnInit {
  clientId = 0;
  loading = true;
  saving = false;
  error = '';
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
    this.load();
  }

  load(): void {
    this.loading = true; this.error = '';
    this.http.get<TrainingProfile>(`/api/training/clients/${this.clientId}/profile`).pipe(catchError(() => of(null))).subscribe(profile => {
      this.profile = profile;
      if (!profile) { this.loading = false; return; }
      this.trainingManagementMode = this.readTrainingMode(profile.notes);
      this.profile.notes = this.cleanTrainingNotes(profile.notes);
      if (!profile.enabled) { this.loading = false; return; }
      this.http.get<TrainingPlan[]>(`/api/training/clients/${this.clientId}/plans`).subscribe({
        next: plans => { this.plans = plans; this.sessionDrafts = {}; for (const p of plans) this.sessionDrafts[p.id] = { date: new Date().toISOString().slice(0,10), title: '', type: 'general', duration: null }; this.loadSessions(plans); this.loading = false; },
        error: () => { this.error = 'No se pudieron cargar los planes.'; this.loading = false; }
      });
    });
  }

  private loadSessions(plans: TrainingPlan[]): void {
    if (!plans.length) return;
    forkJoin(plans.map(p => this.http.get<TrainingSession[]>(`/api/training/plans/${p.id}/sessions`).pipe(catchError(() => of([] as TrainingSession[]))))).subscribe(items => {
      plans.forEach((p, i) => this.sessions[p.id] = items[i]);
    });
  }

  activate(): void {
    this.profile = { id: 0, clientId: this.clientId, enabled: true, primaryGoal: 'general_fitness', experienceLevel: 'beginner', notes: '' };
    this.saveProfile();
  }

  toggleEnabled(): void { if (!this.profile) return; this.profile.enabled = !this.profile.enabled; this.saveProfile(); }

  saveProfile(): void {
    if (!this.profile) return;
    this.saving = true; this.error = '';
    const visibleNotes = this.cleanTrainingNotes(this.profile.notes);
    const payload = { ...this.profile, notes: [visibleNotes, `[[training-management-mode:${this.trainingManagementMode}]]`].filter(Boolean).join(' ') };
    this.http.put<TrainingProfile>(`/api/training/clients/${this.clientId}/profile`, payload).subscribe({
      next: p => { this.trainingManagementMode = this.readTrainingMode(p.notes); this.profile = { ...p, notes: this.cleanTrainingNotes(p.notes) }; this.saving = false; this.load(); },
      error: e => { this.error = e?.error?.message || 'No se pudo guardar el perfil deportivo.'; this.saving = false; }
    });
  }

  private readTrainingMode(notes?: string | null): 'none' | 'self' | 'collaboration' {
    const value = String(notes ?? '');
    if (value.includes('[[training-management-mode:collaboration]]')) return 'collaboration';
    if (value.includes('[[training-management-mode:none]]')) return 'none';
    return 'self';
  }

  private cleanTrainingNotes(notes?: string | null): string {
    return String(notes ?? '').split(' ')
      .filter(token => !token.startsWith('[[training-management-mode:'))
      .join(' ').trim();
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

  record(session: TrainingSession): void {
    const duration = window.prompt('Duración realizada en minutos', String(session.durationMinutes || ''));
    if (duration === null) return;
    const rpe = window.prompt('Esfuerzo percibido (RPE de 0 a 10)', '5');
    if (rpe === null) return;
    const actualDuration = Number(duration); const sessionRpe = Number(rpe);
    if (!Number.isFinite(actualDuration) || actualDuration < 1 || actualDuration > 1440 || !Number.isFinite(sessionRpe) || sessionRpe < 0 || sessionRpe > 10) { this.error = 'Introduce una duración válida y un RPE entre 0 y 10.'; return; }
    this.saving = true; this.error = '';
    this.http.post(`/api/training/sessions/${session.id}/record`, { completedAt: new Date().toISOString(), actualDurationMinutes: actualDuration, sessionRpe, notes: null }).subscribe({
      next: () => { this.saving = false; this.http.get<TrainingSession[]>(`/api/training/plans/${session.planId}/sessions`).subscribe(rows => this.sessions[session.planId] = rows); },
      error: e => { this.error = e?.error?.message || 'No se pudo registrar la sesión.'; this.saving = false; }
    });
  }

  modelLabel(model: string): string {
    return ({ linear: 'Lineal', undulating: 'Ondulante', block: 'Bloques', reverse: 'Inversa', continuous_competition: 'Competición continua', autoregulatory: 'Autoregulado', custom: 'Personalizado' } as Record<string,string>)[model] || model;
  }
}
