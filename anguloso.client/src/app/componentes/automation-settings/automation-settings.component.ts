import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatTabsModule } from '@angular/material/tabs';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { environment } from '../../../environments/environments';
import { PatientPortalService, FollowupSettings } from '../../servicios/patient-portal.service';

interface AutomationRule {
  ruleKey: string;
  enabled: boolean;
  delayMinutes: number | null;
  recipientScope: 'assigned_professional' | 'clinic_admin' | 'patient' | 'both';
  channels: string[];
  updatedAt: string | null;
}

interface AutomationTemplate {
  ruleKey: string;
  patientTitle: string | null;
  patientMessage: string | null;
  professionalTitle: string | null;
  professionalMessage: string | null;
  emailSubject: string | null;
  emailHtml: string | null;
}

interface AutomationJob {
  id: number;
  eventId: number | null;
  actionType: string;
  scheduledAt: string;
  status: string;
  attempts: number;
  maxAttempts: number;
  lastError: string | null;
  createdAt: string;
  completedAt: string | null;
}

@Component({
  selector: 'app-automation-settings',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatCardModule, MatCheckboxModule,
    MatIconModule, MatInputModule, MatFormFieldModule, MatProgressSpinnerModule, MatSelectModule,
    MatSlideToggleModule, MatTabsModule, MatSnackBarModule
  ],
  template: `
    <div class="automation-page">
      <header class="hero">
        <div>
          <span class="eyebrow">DietoExpress</span>
          <h1>Automatizaciones</h1>
          <p>Configura cuándo actuar, quién recibe cada aviso y el contenido de las comunicaciones.</p>
        </div>
        <button mat-stroked-button (click)="reload()" [disabled]="loading">
          <mat-icon>refresh</mat-icon> Actualizar
        </button>
      </header>

      <div *ngIf="loading" class="loading"><mat-spinner diameter="42"></mat-spinner></div>

      <mat-tab-group *ngIf="!loading">
        <mat-tab label="Reglas">
          <section class="section">
            <div class="section-intro">
              <div><h2>Reglas automáticas</h2><p>Los cambios se guardan por organización y se aplican a los nuevos trabajos programados.</p></div>
            </div>

            <div class="rule-grid">
              <mat-card *ngFor="let rule of rules" class="rule-card">
                <div class="rule-head">
                  <div>
                    <h3>{{ ruleLabel(rule.ruleKey) }}</h3>
                    <code>{{ rule.ruleKey }}</code>
                  </div>
                  <mat-slide-toggle [(ngModel)]="rule.enabled" (change)="saveRule(rule)">
                    {{ rule.enabled ? 'Activa' : 'Desactivada' }}
                  </mat-slide-toggle>
                </div>

                <div class="rule-fields">
                  <mat-form-field appearance="outline">
                    <mat-label>Destinatario</mat-label>
                    <mat-select [(ngModel)]="rule.recipientScope" (selectionChange)="saveRule(rule)">
                      <mat-option value="assigned_professional">Profesional asignado</mat-option>
                      <mat-option value="clinic_admin">Administrador de clínica</mat-option>
                      <mat-option value="patient">Paciente</mat-option>
                      <mat-option value="both">Paciente y profesional</mat-option>
                    </mat-select>
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>Retardo / antelación (minutos)</mat-label>
                    <input matInput type="number" min="0" max="525600"
                           [(ngModel)]="rule.delayMinutes" (change)="saveRule(rule)">
                    <mat-hint>Vacío = comportamiento predeterminado</mat-hint>
                  </mat-form-field>
                </div>

                <div class="channels">
                  <span>Canales</span>
                  <mat-checkbox [checked]="hasChannel(rule, 'in_app')" (change)="toggleChannel(rule, 'in_app')">App</mat-checkbox>
                  <mat-checkbox [checked]="hasChannel(rule, 'email')" (change)="toggleChannel(rule, 'email')">Email</mat-checkbox>
                  <mat-checkbox [checked]="hasChannel(rule, 'push')" (change)="toggleChannel(rule, 'push')">Push</mat-checkbox>
                </div>
              </mat-card>
            </div>
          </section>
        </mat-tab>

        <mat-tab label="Plantillas">
          <section class="section">
            <div class="section-intro">
              <div><h2>Plantillas de comunicación</h2><p>Deja un campo vacío para conservar el texto generado por DietoExpress.</p></div>
            </div>

            <mat-card class="template-card">
              <mat-form-field appearance="outline" class="full">
                <mat-label>Regla</mat-label>
                <mat-select [(ngModel)]="selectedTemplateKey" (selectionChange)="selectTemplate()">
                  <mat-option *ngFor="let rule of rules" [value]="rule.ruleKey">{{ ruleLabel(rule.ruleKey) }}</mat-option>
                </mat-select>
              </mat-form-field>

              <div class="template-grid">
                <mat-form-field appearance="outline"><mat-label>Título para paciente</mat-label><input matInput [(ngModel)]="template.patientTitle"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Título profesional</mat-label><input matInput [(ngModel)]="template.professionalTitle"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Mensaje para paciente</mat-label><textarea matInput rows="5" [(ngModel)]="template.patientMessage"></textarea></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Mensaje profesional</mat-label><textarea matInput rows="5" [(ngModel)]="template.professionalMessage"></textarea></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>Asunto de email</mat-label><input matInput [(ngModel)]="template.emailSubject"></mat-form-field>
                <mat-form-field appearance="outline"><mat-label>HTML del email</mat-label><textarea matInput rows="7" [(ngModel)]="template.emailHtml"></textarea></mat-form-field>
              </div>

              <div class="tokens">
                Variables disponibles: <code>{{ '{title}' }}</code> <code>{{ '{message}' }}</code> <code>{{ '{action_url}' }}</code>
              </div>

              <div class="actions">
                <button mat-flat-button color="primary" (click)="saveTemplate()" [disabled]="saving">
                  <mat-spinner *ngIf="saving" diameter="18"></mat-spinner>
                  <span *ngIf="!saving">Guardar plantilla</span>
                </button>
              </div>
            </mat-card>
          </section>
        </mat-tab>

        <mat-tab label="Seguimiento">
          <section class="section">
            <div class="section-intro">
              <div><h2>Panel de seguimiento</h2><p>Configura qué métricas aparecen y cuándo una variación genera una señal para revisar.</p></div>
            </div>
            <mat-card class="template-card" *ngIf="followupSettings">
              <div class="followup-period">
                <mat-form-field appearance="outline">
                  <mat-label>Histórico</mat-label>
                  <mat-select [(ngModel)]="followupSettings.periodWeeks">
                    <mat-option [value]="2">Últimos 2 check-ins</mat-option>
                    <mat-option [value]="4">Últimos 4 check-ins</mat-option>
                    <mat-option [value]="6">Últimos 6 check-ins</mat-option>
                    <mat-option [value]="8">Últimos 8 check-ins</mat-option>
                    <mat-option [value]="12">Últimos 12 check-ins</mat-option>
                  </mat-select>
                </mat-form-field>
              </div>
              <h3>Métricas visibles y señales</h3>
              <div class="followup-settings-grid">
                <div class="followup-setting" *ngFor="let metric of followupMetricKeys">
                  <div class="followup-setting-head">
                    <mat-checkbox [checked]="followupSettings.selectedMetrics.includes(metric.key)" (change)="toggleFollowupMetric(metric.key)">
                      {{ metric.label }}
                    </mat-checkbox>
                  </div>
                  <div class="threshold-grid">
                    <mat-form-field appearance="outline"><mat-label>Mínimo</mat-label><input matInput type="number" [(ngModel)]="followupSettings.thresholds[metric.key].low"></mat-form-field>
                    <mat-form-field appearance="outline"><mat-label>Máximo</mat-label><input matInput type="number" [(ngModel)]="followupSettings.thresholds[metric.key].high"></mat-form-field>
                    <mat-form-field appearance="outline"><mat-label>Bajada</mat-label><input matInput type="number" min="0" [(ngModel)]="followupSettings.thresholds[metric.key].drop"></mat-form-field>
                    <mat-form-field appearance="outline"><mat-label>Subida</mat-label><input matInput type="number" min="0" [(ngModel)]="followupSettings.thresholds[metric.key].rise"></mat-form-field>
                  </div>
                </div>
              </div>
              <div class="actions">
                <button mat-flat-button color="primary" (click)="saveFollowupSettings()" [disabled]="followupSaving">
                  <mat-spinner *ngIf="followupSaving" diameter="18"></mat-spinner>
                  <span *ngIf="!followupSaving">Guardar configuración</span>
                </button>
              </div>
            </mat-card>
          </section>
        </mat-tab>

        <mat-tab label="Actividad">
          <section class="section">
            <div class="section-intro">
              <div><h2>Trabajos de automatización</h2><p>Consulta trabajos programados, completados y fallidos de tu organización.</p></div>
              <button mat-stroked-button (click)="loadJobs()" [disabled]="jobsLoading"><mat-icon>refresh</mat-icon> Actualizar</button>
            </div>

            <mat-card class="jobs-card">
              <div *ngIf="jobsLoading" class="loading small"><mat-spinner diameter="32"></mat-spinner></div>
              <div *ngIf="!jobsLoading && !jobs.length" class="empty"><mat-icon>schedule</mat-icon><span>No hay trabajos de automatización.</span></div>
              <div *ngIf="!jobsLoading && jobs.length" class="jobs-table">
                <div class="job-row job-head"><span>Acción</span><span>Programado</span><span>Estado</span><span>Intentos</span><span></span></div>
                <div class="job-row" *ngFor="let job of jobs">
                  <span>{{ job.actionType }}</span>
                  <span>{{ job.scheduledAt | date:'dd/MM/yyyy HH:mm' }}</span>
                  <span class="status" [class.failed]="job.status === 'failed'" [class.completed]="job.status === 'completed'">{{ job.status }}</span>
                  <span>{{ job.attempts }}/{{ job.maxAttempts }}</span>
                  <button *ngIf="job.status === 'pending'" mat-icon-button (click)="cancelJob(job)" aria-label="Cancelar"><mat-icon>cancel</mat-icon></button>
                  <span *ngIf="job.status !== 'pending'"></span>
                </div>
              </div>
            </mat-card>
          </section>
        </mat-tab>
      </mat-tab-group>
    </div>
  `,
  styles: [`
    .automation-page { width:100%; padding:32px 24px 48px; box-sizing:border-box; }
    .hero { display:flex; justify-content:space-between; align-items:flex-end; gap:20px; margin-bottom:24px; }
    .eyebrow { color:#0f766e; font-size:12px; font-weight:700; letter-spacing:1.2px; text-transform:uppercase; }
    h1 { margin:5px 0 8px; color:#0f172a; font-size:32px; } .hero p,.section-intro p { margin:0; color:#64748b; }
    .section { padding:24px 0; } .section-intro { display:flex; justify-content:space-between; gap:20px; margin-bottom:18px; }
    h2 { margin:0 0 5px; color:#0f172a; font-size:22px; } h3 { margin:0 0 5px; color:#0f172a; font-size:16px; }
    .rule-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(330px,1fr)); gap:18px; }
    .rule-card,.template-card,.jobs-card { padding:20px; border-radius:14px; }
    .rule-head { display:flex; justify-content:space-between; align-items:flex-start; gap:15px; margin-bottom:18px; }
    code { color:#64748b; font-size:11px; word-break:break-word; }
    .rule-fields { display:grid; grid-template-columns:1fr 1fr; gap:12px; } mat-form-field { width:100%; }
    .channels { display:flex; flex-wrap:wrap; align-items:center; gap:10px; padding-top:6px; border-top:1px solid #e2e8f0; }
    .channels > span { width:100%; color:#64748b; font-size:12px; font-weight:600; }
    .template-grid { display:grid; grid-template-columns:1fr 1fr; gap:14px; }
    .followup-period { max-width:280px; margin-bottom:20px; }
    .followup-settings-grid { display:grid; grid-template-columns:1fr 1fr; gap:14px; margin-top:10px; }
    .followup-setting { padding:14px; border:1px solid #e2e8f0; border-radius:12px; }
    .followup-setting-head { margin-bottom:8px; }
    .threshold-grid { display:grid; grid-template-columns:repeat(4,1fr); gap:8px; }
 .full { width:100%; }
    .tokens { margin-top:5px; padding:12px; background:#f8fafc; border-radius:8px; color:#64748b; font-size:12px; }
    .tokens code { margin-left:5px; } .actions { display:flex; justify-content:flex-end; margin-top:18px; }
    .jobs-table { width:100%; overflow:auto; } .job-row { display:grid; grid-template-columns:1.4fr 1.2fr .8fr .6fr 45px; align-items:center; gap:12px; padding:11px 6px; border-bottom:1px solid #e2e8f0; min-width:650px; font-size:13px; color:#334155; }
    .job-head { color:#64748b; font-size:11px; font-weight:700; text-transform:uppercase; } .status { font-weight:600; }
    .status.failed { color:#b91c1c; } .status.completed { color:#15803d; } .empty,.loading { min-height:180px; display:flex; flex-direction:column; align-items:center; justify-content:center; gap:10px; color:#94a3b8; }
    .loading.small { min-height:130px; } .empty mat-icon { font-size:42px; width:42px; height:42px; }
    @media (max-width:760px) { .automation-page{padding:24px 16px 40px}.hero,.section-intro{align-items:stretch; flex-direction:column}.rule-fields,.template-grid,.followup-settings-grid{grid-template-columns:1fr}.threshold-grid{grid-template-columns:1fr 1fr}.rule-grid{grid-template-columns:1fr}.rule-head{flex-direction:column} }
  `]
})
export class AutomationSettingsComponent implements OnInit {
  private readonly base = environment.apiUrl;
  rules: AutomationRule[] = [];
  templates: AutomationTemplate[] = [];
  jobs: AutomationJob[] = [];
  selectedTemplateKey = '';
  template: AutomationTemplate = this.emptyTemplate('');
  loading = true;
  jobsLoading = false;
  saving = false;
  followupSaving = false;
  followupSettings: FollowupSettings = {
    selectedMetrics: ['adherence','hunger','energy','sleep_quality','sleep_hours','training','weight'],
    periodWeeks: 4,
    thresholds: {}
  };
  followupMetricKeys = [
    { key:'adherence', label:'Adherencia' }, { key:'hunger', label:'Hambre' },
    { key:'energy', label:'Energía' }, { key:'sleep_quality', label:'Calidad del sueño' },
    { key:'sleep_hours', label:'Horas de sueño' }, { key:'training', label:'Entrenamiento' },
    { key:'weight', label:'Peso' }
  ];

  constructor(private http: HttpClient, private snack: MatSnackBar, private portalService: PatientPortalService) {}

  ngOnInit(): void { this.reload(); this.loadFollowupSettings(); }

  loadFollowupSettings(): void {
    this.portalService.getFollowupSettings().subscribe({
      next: settings => {
        this.followupSettings = settings;
        for (const metric of this.followupMetricKeys) this.followupSettings.thresholds[metric.key] ??= {};
      },
      error: () => this.snack.open('No se ha podido cargar la configuración de seguimiento.', 'Cerrar', { duration: 3000 })
    });
  }

  toggleFollowupMetric(key: string): void {
    const selected = new Set(this.followupSettings.selectedMetrics);
    if (selected.has(key)) selected.delete(key); else selected.add(key);
    if (!selected.size) { this.snack.open('Debe quedar al menos una métrica seleccionada.', 'Cerrar', { duration: 2200 }); return; }
    this.followupSettings.selectedMetrics = [...selected];
  }

  saveFollowupSettings(): void {
    this.followupSaving = true;
    this.portalService.updateFollowupSettings(this.followupSettings).subscribe({
      next: () => { this.followupSaving = false; this.snack.open('Configuración de seguimiento guardada.', 'Cerrar', { duration: 1800 }); },
      error: () => { this.followupSaving = false; this.snack.open('No se ha podido guardar la configuración de seguimiento.', 'Cerrar', { duration: 3000 }); }
    });
  }

  reload(): void {
    this.loading = true;
    this.http.get<AutomationRule[]>(`${this.base}/professional/automation/rules`).subscribe({
      next: rules => {
        this.rules = rules;
        this.selectedTemplateKey = rules[0]?.ruleKey ?? '';
        this.loadTemplates();
        this.loadJobs();
      },
      error: () => { this.loading = false; this.snack.open('No se han podido cargar las automatizaciones.', 'Cerrar', { duration: 3000 }); }
    });
  }

  loadTemplates(): void {
    this.http.get<AutomationTemplate[]>(`${this.base}/professional/automation/templates`).subscribe({
      next: templates => {
        this.templates = templates;
        this.selectTemplate();
        this.loading = false;
      },
      error: () => { this.loading = false; this.snack.open('No se han podido cargar las plantillas.', 'Cerrar', { duration: 3000 }); }
    });
  }

  loadJobs(): void {
    this.jobsLoading = true;
    this.http.get<AutomationJob[]>(`${this.base}/professional/automation/jobs?limit=100`).subscribe({
      next: jobs => { this.jobs = jobs; this.jobsLoading = false; },
      error: () => { this.jobsLoading = false; }
    });
  }

  selectTemplate(): void {
    const saved = this.templates.find(x => x.ruleKey === this.selectedTemplateKey);
    this.template = saved ? { ...saved } : this.emptyTemplate(this.selectedTemplateKey);
  }

  saveRule(rule: AutomationRule): void {
    this.http.put(`${this.base}/professional/automation/rules/${encodeURIComponent(rule.ruleKey)}`, {
      enabled: rule.enabled,
      delayMinutes: rule.delayMinutes,
      recipientScope: rule.recipientScope,
      channels: rule.channels
    }).subscribe({
      next: () => this.snack.open('Regla guardada.', 'Cerrar', { duration: 1800 }),
      error: () => this.snack.open('No se ha podido guardar la regla.', 'Cerrar', { duration: 3000 })
    });
  }

  toggleChannel(rule: AutomationRule, channel: string): void {
    const channels = new Set(rule.channels ?? []);
    if (channels.has(channel)) channels.delete(channel); else channels.add(channel);
    rule.channels = [...channels];
    if (!rule.channels.length) rule.channels = ['in_app'];
    this.saveRule(rule);
  }

  hasChannel(rule: AutomationRule, channel: string): boolean { return (rule.channels ?? []).includes(channel); }

  saveTemplate(): void {
    if (!this.selectedTemplateKey) return;
    this.saving = true;
    this.http.put(`${this.base}/professional/automation/templates/${encodeURIComponent(this.selectedTemplateKey)}`, this.template).subscribe({
      next: () => {
        const index = this.templates.findIndex(x => x.ruleKey === this.selectedTemplateKey);
        if (index >= 0) this.templates[index] = { ...this.template };
        else this.templates.push({ ...this.template });
        this.saving = false;
        this.snack.open('Plantilla guardada.', 'Cerrar', { duration: 1800 });
      },
      error: () => { this.saving = false; this.snack.open('No se ha podido guardar la plantilla.', 'Cerrar', { duration: 3000 }); }
    });
  }

  cancelJob(job: AutomationJob): void {
    this.http.post(`${this.base}/professional/automation/jobs/${job.id}/cancel`, { reason: 'Cancelado desde la configuración de automatizaciones' }).subscribe({
      next: () => { job.status = 'cancelled'; this.snack.open('Trabajo cancelado.', 'Cerrar', { duration: 1800 }); },
      error: () => this.snack.open('No se ha podido cancelar el trabajo.', 'Cerrar', { duration: 3000 })
    });
  }

  ruleLabel(key: string): string {
    const labels: Record<string, string> = {
      'client.created':'Nuevo paciente',
      'patient.checkin.submitted':'Check-in enviado',
      'patient.checkin.reviewed':'Check-in revisado',
      'appointment.completed':'Cita completada',
      'appointment.reminder.24h':'Recordatorio 24 h',
      'appointment.reminder.2h':'Recordatorio 2 h',
      'appointment.no_show':'Cita no presentada',
      'onboarding.info.reminder':'Onboarding: información pendiente',
      'onboarding.info.escalation':'Onboarding: escalar al profesional',
      'onboarding.first_appointment.reminder':'Primera cita: recordatorio',
      'onboarding.first_appointment.escalation':'Primera cita: escalar al profesional',
      'followup.checkin.reminder':'Seguimiento: check-in pendiente',
      'followup.checkin.escalation':'Seguimiento: escalar al profesional',
      'diet.expiry.reminder':'Dieta: próxima caducidad',
      'diet.expired':'Dieta caducada',
      'diet.renewal':'Dieta: renovación',
      'biometrics.review_due':'Mediciones: revisión pendiente',
      'biometrics.evolution':'Mediciones: cambio relevante'
    };
    return labels[key] ?? key;
  }

  private emptyTemplate(ruleKey: string): AutomationTemplate {
    return { ruleKey, patientTitle:null, patientMessage:null, professionalTitle:null, professionalMessage:null, emailSubject:null, emailHtml:null };
  }
}
