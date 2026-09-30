import { Component, OnInit } from '@angular/core';
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
import { AdminService, AdminConfig, AdminStats, AdminUser, AdminPlan, CreateAdminAccountDto } from '../../servicios/admin.service';
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
    MatSnackBarModule
  ],
  template: `
    <div class="admin-container">
      <div class="admin-header">
        <div>
          <h1>Panel de Control de SuperAdministrador</h1>
          <p class="subtitle">Supervisa nutricionistas registrados, licencias activas y métricas de uso de la plataforma.</p>
        </div>
        <button mat-flat-button color="primary" (click)="openCreateAccount()">
          <mat-icon>person_add</mat-icon> Crear cuenta
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

                <button *ngIf="!u.archivedAt && (u.role === 'nutritionist' || u.role === 'user')" mat-icon-button color="warn" matTooltip="Archivar Cuenta" (click)="deleteUser(u)">
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

      <!-- Application Configuration -->
      <mat-card class="table-card config-card">
        <div class="section-header">
          <div>
            <h2>Configuración de la aplicación</h2>
            <p class="section-subtitle">Todas las líneas de la tabla config.</p>
          </div>
        </div>

        <div class="table-container config-table-container">
          <table mat-table [dataSource]="configs" class="config-table">
            <ng-container matColumnDef="id">
              <th mat-header-cell *matHeaderCellDef>ID</th>
              <td mat-cell *matCellDef="let config">{{ config.id }}</td>
            </ng-container>

            <ng-container matColumnDef="nombre">
              <th mat-header-cell *matHeaderCellDef>Nombre</th>
              <td mat-cell *matCellDef="let config"><strong>{{ config.nombre }}</strong></td>
            </ng-container>

            <ng-container matColumnDef="valor">
              <th mat-header-cell *matHeaderCellDef>Valor</th>
              <td mat-cell *matCellDef="let config" class="config-value">
                <mat-form-field appearance="outline" class="config-value-field">
                  <input matInput [type]="config.esSecreta ? 'password' : 'text'" [(ngModel)]="config.valor" (ngModelChange)="onConfigValueChange(config)" [placeholder]="config.esSecreta && config.tieneValor ? '•••••••• (configurado)' : ''" autocomplete="new-password" />
                  <mat-hint *ngIf="config.saveState === 'pending' || config.saveState === 'saving'">Guardando...</mat-hint>
                  <mat-hint *ngIf="config.saveState === 'saved'">✓ Guardado</mat-hint>
                  <mat-hint *ngIf="config.saveState === 'error'" class="config-error">⚠ No se ha podido guardar. Revisa el valor.</mat-hint>
                </mat-form-field>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="configDisplayedColumns"></tr>
            <tr mat-row *matRowDef="let row; columns: configDisplayedColumns;"></tr>
          </table>

          <div *ngIf="configs.length === 0" class="empty-state">
            <mat-icon>settings_off</mat-icon>
            <p>No hay líneas en la tabla de configuración.</p>
          </div>
        </div>
      </mat-card>
    </div>
  `,
  styles: [`
    .admin-container {
      padding: 24px;
      max-width: 1300px;
      margin: 0 auto;
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
    .config-card {
      margin-top: 24px;
    }
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
export class AdminDashboardComponent implements OnInit {
  stats?: AdminStats;
  plans: AdminPlan[] = [];
  users: AdminUser[] = [];
  totalUsers = 0;
  userPage = 1;
  userPageSize = 25;
  configs: AdminConfig[] = [];
  displayedColumns = ['user', 'plan', 'status', 'expires', 'usage', 'lastLogin', 'actions'];
  configDisplayedColumns = ['id', 'nombre', 'valor'];
  private configTimers = new Map<number, ReturnType<typeof setTimeout>>();
  private searchTimer?: ReturnType<typeof setTimeout>;

  searchTerm = '';
  statusFilter = '';
  planFilter = '';

  constructor(
    private adminService: AdminService,
    private dialog: MatDialog,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.loadData();
  }

  loadData(): void {
    this.adminService.getStats().subscribe({
      next: (stats) => this.stats = stats,
      error: (err) => console.error('Error fetching admin stats', err)
    });
    this.loadUsers();
    this.loadConfig();
    this.adminService.getPlans().subscribe({
      next: plans => this.plans = plans.filter(p => p.active),
      error: () => this.snackBar.open('Error al cargar los planes.', 'Cerrar', { duration: 4000 })
    });
  }

  loadConfig(): void {
    this.adminService.getConfig().subscribe({
      next: (configs) => this.configs = configs.map(config => ({ ...config, originalValor: config.esSecreta ? '' : config.valor, saving: false })),
      error: () => this.snackBar.open('Error al cargar la configuración.', 'Cerrar', { duration: 4000 })
    });
  }

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
