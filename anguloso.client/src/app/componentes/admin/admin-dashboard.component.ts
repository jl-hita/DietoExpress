import { Component, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatTableModule } from '@angular/material/table';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { AdminService, AdminAlert, AdminConfig, AdminStats, AdminUser, AdminPlan, CreateAdminAccountDto, AdminVideoUsage, AdminDatabaseBackup } from '../../servicios/admin.service';
import { EditLicenseDialogComponent } from './edit-license-dialog.component';
import { ResetPasswordDialogComponent } from './reset-password-dialog.component';
import { CreateAdminAccountDialogComponent } from './create-admin-account-dialog.component';
import { DeleteAccountDialogComponent } from './delete-account-dialog.component';
import { DeactivateAccountDialogComponent } from './deactivate-account-dialog.component';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatTableModule,
    MatButtonModule,
    MatIconModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatChipsModule,
    MatTooltipModule,
    MatPaginatorModule,
    MatDialogModule,
    MatSnackBarModule,
    MatTabsModule,
    RouterLink
  ],
  template: `
    <div class="admin-container">
      <mat-card class="alerts-card" *ngIf="alerts.length">
        <div class="alerts-header">
          <div>
            <h2><mat-icon>error_outline</mat-icon> Incidencias de la aplicación</h2>
            <p>Hay problemas operativos que requieren revisión. El detalle técnico permanece en el servidor.</p>
          </div>
          <span class="alert-count">{{ alerts.length }}</span>
        </div>
        <div class="alert-item" *ngFor="let alert of alerts" [ngClass]="'severity-' + alert.severity">
          <mat-icon>{{ alert.severity === 'critical' ? 'report' : (alert.severity === 'error' ? 'error' : 'warning_amber') }}</mat-icon>
          <div class="alert-body">
            <strong>{{ alert.title }}</strong>
            <span>{{ alert.message }}</span>
            <small>{{ alert.component }} · {{ alert.lastSeenAt | date:'dd/MM/yyyy HH:mm' }}<ng-container *ngIf="alert.occurrences > 1"> · {{ alert.occurrences }} ocurrencias</ng-container></small>
          </div>
          <button mat-stroked-button (click)="resolveAlert(alert)" [disabled]="resolvingAlertIds.has(alert.id)">
            {{ resolvingAlertIds.has(alert.id) ? 'Guardando…' : 'Marcar revisada' }}
          </button>
        </div>
      </mat-card>
      <div class="admin-header">
        <div>
          <h1>Panel de Control de SuperAdministrador</h1>
          <p class="subtitle">Supervisa nutricionistas registrados, licencias activas y métricas de uso de la plataforma.</p>
        </div>
        <button mat-flat-button color="primary" (click)="openCreateAccount()">
          <mat-icon>person_add</mat-icon> Crear cuenta
        </button>
        <button mat-stroked-button color="primary" routerLink="/admin/legal">
          <mat-icon>gavel</mat-icon> Legal
        </button>
        <button mat-stroked-button color="primary" (click)="loadData()">
          <mat-icon>refresh</mat-icon> Actualizar
        </button>
      </div>

      <!-- KPI Metrics Cards -->
      <div class="kpi-grid">
        <mat-card class="kpi-card card-blue">
          <div class="kpi-icon"><mat-icon>group</mat-icon></div>
          <div class="kpi-content">
            <span class="kpi-title">Nutricionistas</span>
            <span class="kpi-value">{{ stats?.totalUsers ?? 0 }}</span>
            <span class="kpi-sub">{{ stats?.activeSubscriptions ?? 0 }} con suscripción activa</span>
          </div>
        </mat-card>

        <mat-card class="kpi-card card-teal">
          <div class="kpi-icon"><mat-icon>person_pin</mat-icon></div>
          <div class="kpi-content">
            <span class="kpi-title">Pacientes Totales</span>
            <span class="kpi-value">{{ stats?.totalClients ?? 0 }}</span>
            <span class="kpi-sub">Registrados en la plataforma</span>
          </div>
        </mat-card>

        <mat-card class="kpi-card card-purple">
          <div class="kpi-icon"><mat-icon>restaurant_menu</mat-icon></div>
          <div class="kpi-content">
            <span class="kpi-title">Dietas Elaboradas</span>
            <span class="kpi-value">{{ stats?.totalDiets ?? 0 }}</span>
            <span class="kpi-sub">En base de datos</span>
          </div>
        </mat-card>

        <mat-card class="kpi-card" [ngClass]="(stats?.licensesExpiringSoon ?? 0) > 0 ? 'card-amber' : 'card-slate'">
          <div class="kpi-icon"><mat-icon>warning_amber</mat-icon></div>
          <div class="kpi-content">
            <span class="kpi-title">Expiran en 7 Días</span>
            <span class="kpi-value">{{ stats?.licensesExpiringSoon ?? 0 }}</span>
            <span class="kpi-sub">Requieren renovación o contacto</span>
          </div>
        </mat-card>
      </div>

      <!-- Filters & Search Toolbar -->
      <mat-card class="table-card">
        <div class="filter-toolbar">
          <mat-form-field appearance="outline" class="search-field">
            <mat-label>Buscar nutricionista o clínica</mat-label>
            <input matInput [(ngModel)]="searchTerm" (ngModelChange)="onSearchChange()" placeholder="Nombre, email, usuario..." />
            <mat-icon matSuffix>search</mat-icon>
          </mat-form-field>

          <mat-form-field appearance="outline" class="select-field">
            <mat-label>Filtrar por Estado</mat-label>
            <mat-select [(ngModel)]="statusFilter" (selectionChange)="loadUsers()">
              <mat-option value="">Todos los estados</mat-option>
              <mat-option value="active">Activa</mat-option>
              <mat-option value="suspended">Suspendida</mat-option>
              <mat-option value="past_due">Pago Pendiente</mat-option>
            </mat-select>
          </mat-form-field>

          <mat-form-field appearance="outline" class="select-field">
            <mat-label>Filtrar por Plan</mat-label>
            <mat-select [(ngModel)]="planFilter" (selectionChange)="loadUsers()">
              <mat-option value="">Todos los planes</mat-option>
              <mat-option value="free">Cuenta gratuita</mat-option>
              <mat-option value="demo_nutri">Demo nutricionista</mat-option>
              <mat-option value="nutri_full">Nutri Full</mat-option>
              <mat-option value="clinic_full">Clínica Full</mat-option>
            </mat-select>
          </mat-form-field>
        </div>

        <!-- Table of Users -->
        <div class="table-container">
          <table mat-table [dataSource]="users" class="users-table">
            <!-- User Info Column -->
            <ng-container matColumnDef="user">
              <th mat-header-cell *matHeaderCellDef>Nutricionista / Clínica</th>
              <td mat-cell *matCellDef="let u">
                <div class="user-cell">
                  <strong>{{ u.fullName || u.username }}</strong>
                  <span class="user-sub">{{ u.email }}</span>
                  <span class="clinic-sub" *ngIf="u.clinicName">Clínica: {{ u.clinicName }}</span>
                </div>
              </td>
            </ng-container>

            <!-- Plan Column -->
            <ng-container matColumnDef="plan">
              <th mat-header-cell *matHeaderCellDef>Plan</th>
              <td mat-cell *matCellDef="let u">
                <span class="plan-badge plan-{{ u.subscriptionPlan }}">{{ u.subscriptionPlan | uppercase }}</span>
              </td>
            </ng-container>

            <!-- Status Column -->
            <ng-container matColumnDef="status">
              <th mat-header-cell *matHeaderCellDef>Estado</th>
              <td mat-cell *matCellDef="let u">
                <span class="status-badge status-{{ u.subscriptionStatus }}">
                  {{ u.archivedAt ? 'Archivado' : (u.subscriptionStatus === 'active' ? 'Activo' : (u.subscriptionStatus === 'suspended' ? 'Suspendido' : u.subscriptionStatus)) }}
                </span>
              </td>
            </ng-container>

            <!-- Expiration Column -->
            <ng-container matColumnDef="expires">
              <th mat-header-cell *matHeaderCellDef>Vencimiento Licencia</th>
              <td mat-cell *matCellDef="let u">
                <div *ngIf="u.licenseExpiresAt">
                  <span [ngClass]="{'text-danger': isExpiredOrNear(u.licenseExpiresAt)}">
                    {{ u.licenseExpiresAt | date:'dd/MM/yyyy' }}
                  </span>
                </div>
                <span *ngIf="!u.licenseExpiresAt" class="text-muted">Sin límite</span>
              </td>
            </ng-container>

            <!-- Usage Limits Column -->
            <ng-container matColumnDef="usage">
              <th mat-header-cell *matHeaderCellDef>Pacientes</th>
              <td mat-cell *matCellDef="let u">
                <span>{{ u.clientCount }} / {{ u.maxClientsAllowed }}</span>
              </td>
            </ng-container>

            <!-- Last Login Column -->
            <ng-container matColumnDef="lastLogin">
              <th mat-header-cell *matHeaderCellDef>Último Acceso</th>
              <td mat-cell *matCellDef="let u">
                <span *ngIf="u.lastLogin">{{ u.lastLogin | date:'dd/MM/yy HH:mm' }}</span>
                <span *ngIf="!u.lastLogin" class="text-muted">Nunca</span>
              </td>
            </ng-container>

            <!-- Actions Column -->
            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef class="text-right">Acciones</th>
              <td mat-cell *matCellDef="let u" class="text-right">
                <button mat-icon-button color="primary" matTooltip="Gestionar Licencia" (click)="openEditLicense(u)">
                  <mat-icon>card_membership</mat-icon>
                </button>

                <button mat-icon-button color="accent" matTooltip="Restablecer Contraseña" (click)="openResetPassword(u)">
                  <mat-icon>lock_reset</mat-icon>
                </button>

                <button *ngIf="!u.archivedAt && (u.role === 'nutritionist')" mat-icon-button color="warn" matTooltip="Archivar Cuenta" (click)="deleteUser(u)">
                  <mat-icon>delete_forever</mat-icon>
                </button>

                <button *ngIf="u.subscriptionStatus === 'active'" mat-icon-button color="warn" matTooltip="Suspender Cuenta" (click)="suspendUser(u)">
                  <mat-icon>block</mat-icon>
                </button>

                <button *ngIf="u.archivedAt || u.subscriptionStatus !== 'active'" mat-icon-button style="color: #10b981;" matTooltip="Reactivar Cuenta" (click)="activateUser(u)">
                  <mat-icon>check_circle</mat-icon>
                </button>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
          </table>

          <mat-paginator
            [length]="totalUsers"
            [pageIndex]="userPage - 1"
            [pageSize]="userPageSize"
            [pageSizeOptions]="[10, 25, 50, 100]"
            (page)="onUsersPageChange($event)"
            showFirstLastButtons>
          </mat-paginator>

          <div *ngIf="users.length === 0" class="empty-state">
            <mat-icon>search_off</mat-icon>
            <p>No se encontraron nutricionistas con los filtros seleccionados.</p>
          </div>
        </div>
      </mat-card>

      <mat-card class="table-card backup-card">
        <div class="section-header">
          <div>
            <h2><mat-icon>backup</mat-icon> Copias de seguridad de PostgreSQL</h2>
            <p class="section-subtitle">Backups manuales y semanales. Puedes verificar una copia o iniciar una restauración controlada.</p>
          </div>
          <button mat-flat-button color="primary" (click)="createDatabaseBackup()" [disabled]="databaseBackupRunning || !databaseBackupConfigured">
            <mat-icon>save</mat-icon>
            {{ databaseBackupRunning ? 'Creando copia…' : 'Crear backup ahora' }}
          </button>
        </div>

        <div *ngIf="!databaseBackupConfigured" class="backup-warning">
          <mat-icon>warning_amber</mat-icon>
          <span>Las copias no están configuradas en el servidor. Debe definirse <code>DIETOEXPRESS_BACKUP_DIR</code> y la conexión de PostgreSQL.</span>
        </div>

        <div *ngIf="databaseBackupConfigured && databaseBackups.length === 0" class="empty-state backup-empty">
          <mat-icon>cloud_off</mat-icon>
          <p>Todavía no hay copias disponibles.</p>
        </div>

        <div *ngIf="databaseRestoreCountdownActive" class="restore-countdown">
  <div class="restore-countdown-icon"><mat-icon>schedule</mat-icon></div>
  <div class="restore-countdown-content">
    <strong>Restauración programada</strong>
    <span>La restauración comenzará en <b>{{ databaseRestoreCountdownSeconds }}</b> segundos.</span>
    <small>Prevista a las {{ databaseRestoreScheduledAt | date:'HH:mm:ss' }}</small>
  </div>
  <button mat-stroked-button color="warn" type="button" (click)="cancelDatabaseRestoreCountdown()">Cancelar</button>
</div>

<div class="backup-list" *ngIf="databaseBackups.length">
          <div class="backup-row" *ngFor="let backup of databaseBackups">
            <div>
              <strong>{{ backup.fileName }}</strong>
              <span>{{ formatBackupSize(backup.sizeBytes) }} · {{ backup.createdAtUtc | date:'dd/MM/yyyy HH:mm':'UTC' }} UTC</span>
            </div>
            <div class="backup-actions">
              <button mat-stroked-button (click)="verifyDatabaseBackup(backup)" [disabled]="databaseBackupOperation === backup.fileName">
                <mat-icon>verified</mat-icon> Verificar
              </button>
              <button mat-stroked-button color="warn" (click)="restoreDatabaseBackup(backup)" [disabled]="databaseBackupOperation === backup.fileName">
                <mat-icon>restore</mat-icon> Restaurar
              </button>
              <a mat-stroked-button color="primary" [href]="adminService.getDatabaseBackupDownloadUrl(backup.fileName)">
                <mat-icon>download</mat-icon> Descargar
              </a>
            </div>
          </div>
        </div>
      </mat-card>

      <!-- Application Configuration -->
      <mat-card class="table-card config-card">
        <div class="section-header">
          <div>
            <h2>Configuración de la aplicación</h2>
            <p class="section-subtitle">Todas las líneas de la tabla config.</p>
          </div>
        </div>

        <div class="video-usage-card" *ngIf="videoUsage">
          <div>
            <strong>Consumo real de LiveKit</strong>
            <span *ngIf="videoUsage.analytics.available">
              {{ videoUsage.analytics.connectionMinutes }} minutos-participante en los últimos 7 días
              ({{ videoUsage.analytics.sessions }} sesiones).
            </span>
            <span *ngIf="!videoUsage.analytics.available" class="text-muted">
              {{ videoUsage.analytics.message }}
            </span>
          </div>
          <div class="video-usage-metrics">
            <span>Cuota/nutricionista: <b>{{ videoUsage.nutritionistLimitParticipantMinutes }}</b></span>
            <span>Cuota global: <b>{{ videoUsage.globalLimitParticipantMinutes }}</b></span>
          </div>
          <small>La cuota interna se reserva como estimación; connectionMinutes es el consumo real reportado por LiveKit cuando Analytics está disponible.</small>
        </div>

        <mat-tab-group animationDuration="0ms" class="config-tabs">
          <mat-tab *ngFor="let tab of configTabs"><ng-template mat-tab-label>{{ tab.label }}</ng-template>
            <div class="config-tab-content">
              <div class="config-grid" *ngIf="configsFor(tab.key).length > 0">
                <mat-card class="config-item" *ngFor="let config of configsFor(tab.key)">
                  <div class="config-item-header"><div><strong>{{ config.nombre }}</strong><span class="config-description">{{ config.descripcion }}</span></div><span class="secret-badge" *ngIf="config.esSecreta">Secreto</span></div>
                  <mat-form-field appearance="outline" class="config-value-field">
                    <mat-label>Valor</mat-label>
                    <input matInput [type]="config.esSecreta ? 'password' : 'text'" [(ngModel)]="config.valor" (ngModelChange)="onConfigValueChange(config)" [placeholder]="config.esSecreta && config.tieneValor ? '•••••••• (configurado)' : ''" autocomplete="new-password" />
                    <mat-hint *ngIf="config.saveState === 'pending' || config.saveState === 'saving'">Guardando...</mat-hint><mat-hint *ngIf="config.saveState === 'saved'">✓ Guardado</mat-hint><mat-hint *ngIf="config.saveState === 'error'" class="config-error">⚠ No se ha podido guardar.</mat-hint>
                  </mat-form-field>
                </mat-card>
              </div>
              <div *ngIf="configsFor(tab.key).length === 0" class="empty-state"><mat-icon>settings_off</mat-icon><p>No hay configuración en esta categoría.</p></div>
            </div>
          </mat-tab>
        </mat-tab-group>
      </mat-card>
    </div>
  `,
  styles: [`
    .alerts-card { margin-bottom: 24px; padding: 20px; border-radius: 12px; border-left: 5px solid #dc2626; }
    .alerts-header { display:flex; justify-content:space-between; gap:16px; align-items:flex-start; margin-bottom:14px; }
    .alerts-header h2 { margin:0; display:flex; align-items:center; gap:8px; font-size:20px; color:#991b1b; }
    .alerts-header h2 mat-icon { color:#dc2626; }
    .alerts-header p { margin:5px 0 0; color:#64748b; font-size:13px; }
    .alert-count { min-width:28px; height:28px; border-radius:50%; display:grid; place-items:center; background:#fee2e2; color:#991b1b; font-weight:700; }
    .alert-item { display:flex; align-items:center; gap:12px; padding:13px; border:1px solid #fecaca; border-radius:10px; background:#fff7f7; margin-top:10px; }
    .alert-item > mat-icon { color:#dc2626; flex:none; }
    .alert-body { flex:1; min-width:0; display:grid; gap:3px; }
    .alert-body strong { color:#7f1d1d; }
    .alert-body span { color:#334155; font-size:13px; }
    .alert-body small { color:#64748b; font-size:11px; }
    .severity-warning { border-color:#fde68a; background:#fffbeb; }
    .severity-warning > mat-icon { color:#d97706; }
    .severity-warning .alert-body strong { color:#92400e; }
    .severity-info { border-color:#bfdbfe; background:#eff6ff; }
    .severity-info > mat-icon { color:#2563eb; }
    .severity-info .alert-body strong { color:#1e40af; }
    @media (max-width: 768px) { .alert-item { align-items:flex-start; flex-wrap:wrap; } .alert-item button { margin-left:32px; } }

    .admin-container {
      padding: 24px;
      width: 100%;
      margin: 0;
      font-family: 'Roboto', sans-serif;
    }
    .admin-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 24px;
    }
    .admin-header h1 {
      margin: 0;
      font-size: 26px;
      font-weight: 700;
      color: #0f172a;
    }
    .subtitle {
      margin-top: 4px;
      color: #64748b;
      font-size: 14px;
    }
    .kpi-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
      gap: 16px;
      margin-bottom: 24px;
    }
    .kpi-card {
      display: flex;
      flex-direction: row;
      align-items: center;
      padding: 20px;
      border-radius: 12px;
      color: #0f172a;
      border: 1px solid #e2e8f0;
      box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.08);
    }
    .kpi-icon {
      margin-right: 16px;
      flex: 0 0 auto;
    }
    .kpi-icon mat-icon {
      font-size: 40px;
      width: 40px;
      height: 40px;
    }
    .kpi-content {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }
    .kpi-title {
      font-size: 13px;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      font-weight: 700;
      color: #334155;
    }
    .kpi-value {
      font-size: 28px;
      font-weight: 700;
      line-height: 1.2;
      color: #0f172a;
    }
    .kpi-sub {
      font-size: 12px;
      color: #475569;
      margin-top: 2px;
    }
    .card-blue { background: linear-gradient(135deg, #eff6ff, #dbeafe); }
    .card-blue .kpi-icon mat-icon { color: #2563eb; }
    .card-teal { background: linear-gradient(135deg, #f0fdfa, #ccfbf1); }
    .card-teal .kpi-icon mat-icon { color: #0d9488; }
    .card-purple { background: linear-gradient(135deg, #f5f3ff, #ede9fe); }
    .card-purple .kpi-icon mat-icon { color: #7c3aed; }
    .card-amber { background: linear-gradient(135deg, #fffbeb, #fef3c7); }
    .card-amber .kpi-icon mat-icon { color: #d97706; }
    .card-slate { background: linear-gradient(135deg, #f8fafc, #e2e8f0); }
    .card-slate .kpi-icon mat-icon { color: #475569; }

    .table-card {
      padding: 20px;
      border-radius: 12px;
    }
    .backup-card { margin-bottom: 24px; }
    .backup-card .section-header { display:flex; justify-content:space-between; align-items:flex-start; gap:16px; }
    .backup-card .section-header h2 { display:flex; align-items:center; gap:8px; }
    .backup-list { display:grid; gap:8px; }
    .backup-row { display:flex; justify-content:space-between; align-items:center; gap:16px; padding:12px 14px; border:1px solid #e2e8f0; border-radius:10px; background:#f8fafc; }
    .backup-row > div { min-width:0; display:grid; gap:3px; }
    .backup-row strong { overflow-wrap:anywhere; }
    .backup-row span { color:#64748b; font-size:12px; }
    .backup-warning { display:flex; align-items:flex-start; gap:10px; padding:12px 14px; border-radius:10px; background:#fffbeb; border:1px solid #fde68a; color:#92400e; }
    .backup-warning mat-icon { flex:none; }
    .restore-countdown { display:flex; align-items:center; gap:14px; margin:0 0 16px; padding:16px; border:1px solid #fcd34d; border-radius:10px; background:#fffbeb; }
    .restore-countdown-icon { display:grid; place-items:center; width:44px; height:44px; border-radius:50%; background:#fef3c7; color:#b45309; flex:none; }
    .restore-countdown-content { flex:1; min-width:0; display:grid; gap:3px; }
    .restore-countdown-content strong { color:#92400e; }
    .restore-countdown-content span { color:#334155; }
    .restore-countdown-content small { color:#64748b; }
    @media (max-width: 768px) { .restore-countdown { align-items:flex-start; flex-wrap:wrap; } .restore-countdown button { margin-left:58px; } }
    .config-card { margin-top: 24px; }
    .video-usage-card {
      margin: 0 0 18px;
      padding: 14px 16px;
      border: 1px solid #cbd5e1;
      border-radius: 10px;
      background: #f8fafc;
      display: grid;
      gap: 8px;
    }
    .video-usage-card > div:first-child { display: grid; gap: 3px; }
    .video-usage-metrics { display: flex; gap: 18px; flex-wrap: wrap; color: #334155; }
    .video-usage-card small { color: #64748b; }
    .section-header {
      margin-bottom: 16px;
    }
    .section-header h2 {
      margin: 0;
      font-size: 20px;
      font-weight: 600;
      color: #0f172a;
    }
    .section-subtitle {
      margin: 4px 0 0;
      color: #64748b;
      font-size: 13px;
    }
    .config-table {
      width: 100%;
    }
    .config-value {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
      font-family: monospace;
    }
    .config-value-field {
      width: 100%;
      min-width: 300px;
      margin-top: 8px;
    }
    .filter-toolbar {
      display: flex;
      gap: 16px;
      flex-wrap: wrap;
      margin-bottom: 16px;
    }
    .search-field {
      flex: 1;
      min-width: 250px;
    }
    .select-field {
      width: 200px;
    }
    .users-table {
      width: 100%;
    }
    .user-cell {
      display: flex;
      flex-direction: column;
      padding: 6px 0;
    }
    .user-sub {
      font-size: 12px;
      color: #64748b;
    }
    .clinic-sub {
      font-size: 11px;
      color: #0d9488;
      font-weight: 500;
    }
    .plan-badge {
      font-size: 11px;
      font-weight: 700;
      padding: 3px 8px;
      border-radius: 6px;
      display: inline-block;
    }
    .plan-free, .plan-demo_nutri { background: #e2e8f0; color: #475569; }
    .plan-starter, .plan-nutri_full { background: #dbeafe; color: #1e40af; }
    .plan-professional { background: #ede9fe; color: #5b21b6; }
    .plan-enterprise, .plan-clinic_full { background: #ccfbf1; color: #0f766e; }

    .status-badge {
      font-size: 11px;
      font-weight: 600;
      padding: 3px 8px;
      border-radius: 12px;
    }
    .status-active { background: #dcfce7; color: #166534; }
    .status-suspended { background: #fee2e2; color: #991b1b; }
    .status-past_due { background: #fef3c7; color: #92400e; }

    .text-danger { color: #dc2626; font-weight: 600; }
    .text-muted { color: #94a3b8; font-size: 12px; }
    .text-right { text-align: right; }

    .backup-policy { display:flex; align-items:center; gap:8px; padding:10px 12px; margin-bottom:12px; border-radius:8px; background:#f1f5f9; color:#475569; font-size:13px; }
    .backup-policy mat-icon { font-size:20px; width:20px; height:20px; }
    .backup-actions { display:flex; gap:8px; flex-wrap:wrap; justify-content:flex-end; }

    .empty-state {
      text-align: center;
      padding: 40px;
      color: #94a3b8;
    }
    .empty-state mat-icon {
      font-size: 48px;
      width: 48px;
      height: 48px;
    }
  `]
})
// Documentación: este componente coordina estado de interfaz y operaciones asíncronas que deben mantenerse alineadas con la API.
export class AdminDashboardComponent implements OnInit {
  alerts: AdminAlert[] = [];
  resolvingAlertIds = new Set<number>();
  stats?: AdminStats;
  plans: AdminPlan[] = [];
  users: AdminUser[] = [];
  totalUsers = 0;
  userPage = 1;
  userPageSize = 25;
  configs: AdminConfig[] = [];
  videoUsage?: AdminVideoUsage;
  databaseBackups: AdminDatabaseBackup[] = [];
  databaseBackupConfigured = false;
  databaseBackupRunning = false;
  databaseBackupOperation = '';
  databaseRestoreCountdownActive = false;
  databaseRestoreCountdownSeconds = 0;
  databaseRestoreScheduledAt?: Date;
  private databaseRestoreCountdownTimer?: ReturnType<typeof setInterval>;
  displayedColumns = ['user', 'plan', 'status', 'expires', 'usage', 'lastLogin', 'actions'];
  configDisplayedColumns = ['id', 'nombre', 'valor'];
  configTabs = [{key:'PLATFORM',label:'Plataforma'},{key:'EMAIL',label:'Email'},{key:'GOOGLE',label:'Google / Calendario'},{key:'VIDEO',label:'Videollamadas'},{key:'FOOD',label:'Alimentos'},{key:'NOTIFICATIONS',label:'Notificaciones'},{key:'ADDRESS',label:'Direcciones'},{key:'OTHER',label:'Otros'}];
  configsFor(category: string): AdminConfig[] { return this.configs.filter(c => c.categoria === category); }
  private configTimers = new Map<number, ReturnType<typeof setTimeout>>();
  private searchTimer?: ReturnType<typeof setTimeout>;

  searchTerm = '';
  statusFilter = '';
  planFilter = '';

  constructor(
    public adminService: AdminService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.loadData();
  }

  // El panel combina métricas, usuarios, configuración y planes; cada bloque se carga independientemente para que un fallo parcial no inutilice toda la pantalla.
  loadData(): void {
    this.adminService.getAlerts().subscribe({
      next: alerts => this.alerts = alerts,
      error: err => console.error('Error fetching application alerts', err)
    });
    this.adminService.getStats().subscribe({
      next: (stats) => this.stats = stats,
      error: (err) => console.error('Error fetching admin stats', err)
    });
    this.loadUsers();
    this.loadConfig();
    this.loadVideoUsage();
    this.loadDatabaseBackups();
    this.adminService.getPlans().subscribe({
      next: plans => this.plans = plans.filter(p => p.active),
      error: () => this.snackBar.open('Error al cargar los planes.', 'Cerrar', { duration: 4000 })
    });
  }

  loadDatabaseBackups(): void {
    this.adminService.getDatabaseBackups().subscribe({
      next: result => {
        this.databaseBackupConfigured = result.configured;
        this.databaseBackups = result.items;
      },
      error: () => this.snackBar.open('No se pudo consultar el estado de las copias.', 'Cerrar', { duration: 4000 })
    });
  }

  createDatabaseBackup(): void {
    if (this.databaseBackupRunning || !this.databaseBackupConfigured) return;
    this.databaseBackupRunning = true;
    this.adminService.createDatabaseBackup().subscribe({
      next: backup => {
        this.databaseBackups = [backup, ...this.databaseBackups.filter(x => x.fileName !== backup.fileName)];
        this.databaseBackupRunning = false;
        this.snackBar.open('Backup creado correctamente.', 'OK', { duration: 4000 });
      },
      error: err => {
        this.databaseBackupRunning = false;
        const message = err?.error?.detail || err?.error?.title || 'No se pudo crear el backup.';
        this.snackBar.open(message, 'Cerrar', { duration: 6000 });
      }
    });
  }

  verifyDatabaseBackup(backup: AdminDatabaseBackup): void {
    if (this.databaseBackupOperation) return;
    this.databaseBackupOperation = backup.fileName;
    this.adminService.verifyDatabaseBackup(backup.fileName).subscribe({
      next: () => {
        this.databaseBackupOperation = '';
        this.snackBar.open('Backup verificado correctamente.', 'OK', { duration: 4000 });
      },
      error: err => {
        this.databaseBackupOperation = '';
        const message = err?.error?.detail || err?.error?.title || 'No se pudo verificar el backup.';
        this.snackBar.open(message, 'Cerrar', { duration: 6000 });
      }
    });
  }

  restoreDatabaseBackup(backup: AdminDatabaseBackup): void {
    if (this.databaseBackupOperation || this.databaseRestoreCountdownActive) return;
    const confirmed = confirm(
      'ATENCIÓN: esta operación sustituirá la base de datos actual por la del backup seleccionado. ' +
      'Se creará automáticamente un backup del estado actual antes de restaurar y, si falla, se intentará hacer rollback. ¿Continuar?'
    );
    if (!confirmed) return;

    this.databaseBackupOperation = backup.fileName;
    this.databaseRestoreCountdownActive = true;
    this.databaseRestoreCountdownSeconds = 10;
    this.databaseRestoreScheduledAt = new Date(Date.now() + 10000);

    this.databaseRestoreCountdownTimer = setInterval(() => {
      this.databaseRestoreCountdownSeconds = Math.max(
        0,
        Math.ceil((this.databaseRestoreScheduledAt!.getTime() - Date.now()) / 1000)
      );
      if (this.databaseRestoreCountdownSeconds === 0) {
        this.clearDatabaseRestoreCountdownTimer();
        this.databaseRestoreCountdownActive = false;
        this.startDatabaseRestore(backup);
      }
    }, 250);
  }

  cancelDatabaseRestoreCountdown(): void {
    this.clearDatabaseRestoreCountdownTimer();
    this.databaseRestoreCountdownActive = false;
    this.databaseRestoreScheduledAt = undefined;
    this.databaseBackupOperation = '';
    this.snackBar.open('Restauración cancelada.', 'OK', { duration: 3000 });
  }

  private clearDatabaseRestoreCountdownTimer(): void {
    if (this.databaseRestoreCountdownTimer) {
      clearInterval(this.databaseRestoreCountdownTimer);
      this.databaseRestoreCountdownTimer = undefined;
    }
  }

  private startDatabaseRestore(backup: AdminDatabaseBackup): void {
    this.adminService.restoreDatabaseBackup(backup.fileName).subscribe({
      next: () => {
        this.databaseBackupOperation = '';
        this.databaseRestoreScheduledAt = undefined;
        this.snackBar.open('Restauración iniciada. DietoExpress se reiniciará cuando termine el proceso.', 'OK', { duration: 8000 });
        this.loadDatabaseBackups();
      },
      error: err => {
        this.databaseBackupOperation = '';
        this.databaseRestoreScheduledAt = undefined;
        const message = err?.error?.detail || err?.error?.title || 'No se pudo restaurar el backup.';
        this.snackBar.open(message, 'Cerrar', { duration: 8000 });
        this.loadDatabaseBackups();
      }
    });
  }

  formatBackupSize(bytes: number): string {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    if (bytes < 1024 * 1024 * 1024) return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    return (bytes / (1024 * 1024 * 1024)).toFixed(1) + ' GB';
  }

  loadVideoUsage(): void {
    this.adminService.getVideoUsage().subscribe({
      next: usage => this.videoUsage = usage,
      error: err => console.error('Error fetching LiveKit usage', err)
    });
  }

  loadConfig(): void {
    this.adminService.getConfig().subscribe({
      next: (configs) => this.configs = configs.map(config => ({ ...config, originalValor: config.esSecreta ? '' : config.valor, saving: false })),
      error: () => this.snackBar.open('Error al cargar la configuración.', 'Cerrar', { duration: 4000 })
    });
  }

  // Cada configuración tiene su propio temporizador de debounce para agrupar cambios de escritura y evitar guardar cada pulsación.
  onConfigValueChange(config: AdminConfig & { originalValor?: string; saving?: boolean; saveState?: 'idle' | 'pending' | 'saving' | 'saved' | 'error' }): void {
    config.saveState = 'pending';
    const previous = this.configTimers.get(config.id);
    if (previous) clearTimeout(previous);

    const timer = setTimeout(() => {
      this.configTimers.delete(config.id);
      this.saveConfig(config);
    }, 700);

    this.configTimers.set(config.id, timer);
  }

  // Si llega un cambio mientras otro guardado está en curso se marca como pendiente y se reintenta al terminar, evitando perder la última edición.
  saveConfig(config: AdminConfig & { originalValor?: string; saving?: boolean; saveState?: 'idle' | 'pending' | 'saving' | 'saved' | 'error'; pendingSave?: boolean }): void {
    if (config.saving) {
      config.pendingSave = true;
      return;
    }

    if (config.esSecreta ? !config.valor : config.valor === config.originalValor) {
      config.saveState = 'saved';
      return;
    }

    if (config.nombre === 'frontendUrl') {
      try {
        const url = new URL(config.valor.trim());
        if (url.protocol !== 'https:') {
          config.saveState = 'error';
          return;
        }
      } catch {
        config.saveState = 'error';
        return;
      }
    }

    config.saving = true;
    config.saveState = 'saving';
    this.adminService.updateConfig(config.id, config.valor).subscribe({
      next: (updated) => {
        config.valor = updated.valor;
        config.originalValor = updated.esSecreta ? '' : updated.valor;
        config.saving = false;
        config.saveState = 'saved';
        if (config.pendingSave) {
          config.pendingSave = false;
          this.onConfigValueChange(config);
        }
      },
      error: () => {
        config.saving = false;
        config.saveState = 'error';
        if (config.pendingSave) {
          config.pendingSave = false;
          this.onConfigValueChange(config);
        }
      }
    });
  }

  onSearchChange(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.loadUsers(), 350);
  }

  loadUsers(resetPage = true): void {
    if (resetPage) this.userPage = 1;
    this.adminService.getUsers(this.searchTerm, this.statusFilter, this.planFilter, this.userPage, this.userPageSize).subscribe({
      next: (result) => {
        this.users = result.items;
        this.totalUsers = result.totalCount;
      },
      error: (err) => {
        this.snackBar.open('Error al cargar la lista de usuarios.', 'Cerrar', { duration: 4000 });
      }
    });
  }

  onUsersPageChange(event: PageEvent): void {
    this.userPage = event.pageIndex + 1;
    this.userPageSize = event.pageSize;
    this.loadUsers(false);
  }

  resolveAlert(alert: AdminAlert): void {
    if (this.resolvingAlertIds.has(alert.id)) return;

    this.resolvingAlertIds.add(alert.id);
    this.adminService.resolveAlert(alert.id).subscribe({
      next: () => {
        this.alerts = this.alerts.filter(current => current.id !== alert.id);
        this.resolvingAlertIds.delete(alert.id);
        this.snackBar.open('Incidencia marcada como revisada.', 'OK', { duration: 3000 });
      },
      error: () => {
        this.resolvingAlertIds.delete(alert.id);
        this.snackBar.open('No se pudo marcar la incidencia como revisada.', 'Cerrar', { duration: 4000 });
      }
    });
  }

  isExpiredOrNear(dateStr: string): boolean {
    const d = new Date(dateStr).getTime();
    const now = new Date().getTime();
    const in7Days = now + (7 * 24 * 60 * 60 * 1000);
    return d <= in7Days;
  }


  openCreateAccount(): void {
    if (!this.plans.length) {
      this.snackBar.open('No hay planes activos disponibles.', 'Cerrar', { duration: 4000 });
      return;
    }

    const ref = this.dialog.open(CreateAdminAccountDialogComponent, {
      width: '720px',
      maxWidth: '95vw',
      data: { plans: this.plans }
    });

    ref.afterClosed().subscribe((account: CreateAdminAccountDto | undefined) => {
      if (!account) return;
      this.adminService.createAccount(account).subscribe({
        next: result => {
          this.snackBar.open('Cuenta ' + result.username + ' creada correctamente.', 'OK', { duration: 5000 });
          this.loadData();
        },
        error: err => {
          const message = err?.error?.message || err?.error || 'Error al crear la cuenta.';
          this.snackBar.open(message, 'Cerrar', { duration: 5000 });
        }
      });
    });
  }

  openEditLicense(user: AdminUser): void {
    const ref = this.dialog.open(EditLicenseDialogComponent, {
      width: '450px',
      data: user
    });

    ref.afterClosed().subscribe(res => {
      if (res) {
        this.adminService.updateLicense(user.id, res).subscribe({
          next: () => {
            this.snackBar.open('Licencia actualizada con éxito.', 'OK', { duration: 3000 });
            this.loadData();
          },
          error: () => this.snackBar.open('Error al actualizar licencia.', 'Cerrar', { duration: 4000 })
        });
      }
    });
  }

  openResetPassword(user: AdminUser): void {
    const ref = this.dialog.open(ResetPasswordDialogComponent, {
      width: '400px',
      data: user
    });

    ref.afterClosed().subscribe(newPassword => {
      if (newPassword) {
        this.adminService.resetUserPassword(user.id, newPassword).subscribe({
          next: () => this.snackBar.open('Contraseña restablecida exitosamente.', 'OK', { duration: 3000 }),
          error: () => this.snackBar.open('Error al restablecer contraseña.', 'Cerrar', { duration: 4000 })
        });
      }
    });
  }

  suspendUser(user: AdminUser): void {
    if (!confirm(`¿Estás seguro de suspender la cuenta de ${user.username}?`)) return;

    this.adminService.suspendUser(user.id).subscribe({
      next: () => {
        this.snackBar.open('Usuario suspendido.', 'OK', { duration: 3000 });
        this.loadData();
      },
      error: () => this.snackBar.open('Error al suspender usuario.', 'Cerrar', { duration: 4000 })
    });
  }

  // El archivado tiene dos recorridos: sin pacientes se confirma directamente; con pacientes se abre primero un flujo para resolver sus asignaciones.
  deleteUser(user: AdminUser): void {
    this.adminService.getDeactivationPreview(user.id).subscribe({
      next: preview => {
        if (!preview.clients.length) {
          const confirmRef = this.dialog.open(DeleteAccountDialogComponent, { width:'560px', maxWidth:'95vw', data:user });
          confirmRef.afterClosed().subscribe(confirmed => {
            if (!confirmed) return;
            this.adminService.deleteUser(user.id,{assignments:[]}).subscribe({
              next:()=>{this.snackBar.open('Cuenta archivada correctamente.','OK',{duration:4000});this.loadData();},
              error:err=>this.snackBar.open(err?.error?.message||err?.error||'Error al archivar la cuenta.','Cerrar',{duration:5000})
            });
          });
          return;
        }

        const ref=this.dialog.open(DeactivateAccountDialogComponent,{
          width:'760px',
          maxWidth:'95vw',
          data:preview
        });
        ref.afterClosed().subscribe(result=>{
          if(!result)return;
          this.adminService.deleteUser(user.id,result).subscribe({
            next:()=>{this.snackBar.open('Cuenta archivada y pacientes gestionados correctamente.','OK',{duration:4000});this.loadData();},
            error:err=>this.snackBar.open(err?.error?.message||err?.error||'Error al archivar la cuenta.','Cerrar',{duration:5000})
          });
        });
      },
      error:err=>this.snackBar.open(err?.error?.message||err?.error||'No se pudo preparar el archivado.','Cerrar',{duration:5000})
    });
  }

  activateUser(user: AdminUser): void {
    this.adminService.activateUser(user.id).subscribe({
      next: () => {
        this.snackBar.open('Usuario activado.', 'OK', { duration: 3000 });
        this.loadData();
      },
      error: () => this.snackBar.open('Error al activar usuario.', 'Cerrar', { duration: 4000 })
    });
  }
}

