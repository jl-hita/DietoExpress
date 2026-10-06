import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { AdminService } from '../../servicios/admin.service';

@Component({
  selector: 'app-admin-log',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, MatIconModule, MatSelectModule],
  template: `
    <div class="log-container">
      <div class="log-header">
        <div><h1>Logs del sistema</h1><p>Consulta el log de Serilog por día.</p></div>
        <div class="date-navigation">
          <button mat-icon-button [disabled]="!previousDate || loading" (click)="loadLog(previousDate!)" aria-label="Día anterior"><mat-icon>chevron_left</mat-icon></button>
          <strong>{{ selectedDate | date:'dd/MM/yyyy' }}</strong>
          <button mat-icon-button [disabled]="!nextDate || loading" (click)="loadLog(nextDate!)" aria-label="Día siguiente"><mat-icon>chevron_right</mat-icon></button>
        </div>
      </div>
      <mat-card class="log-card">
        <div class="log-toolbar"><span>{{ exists ? 'Log disponible' : 'No hay log para este día' }}</span><div class="log-actions"><mat-form-field appearance="outline" class="level-filter"><mat-label>Nivel</mat-label><mat-select [(value)]="levelFilter"><mat-option value="all">Todos</mat-option><mat-option value="info">Info</mat-option><mat-option value="warning">Warning</mat-option><mat-option value="error">Error</mat-option></mat-select></mat-form-field><button mat-stroked-button (click)="loadLog(selectedDate)" [disabled]="loading"><mat-icon>refresh</mat-icon> Actualizar</button></div></div>
        <div *ngIf="exists" class="log-content">
          <div *ngFor="let line of filteredLogLines" class="log-line" [class.warning-line]="getLevel(line) === 'warning'" [class.error-line]="getLevel(line) === 'error'">{{ line }}</div>
        </div>
        <div *ngIf="!exists" class="empty-state"><mat-icon>event_busy</mat-icon><p>No existe ningún archivo de log para {{ selectedDate | date:'dd/MM/yyyy' }}.</p></div>
      </mat-card>
    </div>
  `,
  styles: [`
    .log-container { padding: 24px; width: 100%; margin: 0; font-family: 'Roboto', sans-serif; }
    .log-header { display: flex; justify-content: space-between; align-items: center; gap: 24px; margin-bottom: 20px; }
    h1 { margin: 0; color: #0f172a; font-size: 26px; }
    .log-header p { margin: 4px 0 0; color: #64748b; font-size: 14px; }
    .date-navigation { display: flex; align-items: center; gap: 12px; white-space: nowrap; }
    .log-card { padding: 16px; border-radius: 12px; }
    .log-toolbar { display: flex; justify-content: space-between; align-items: center; gap: 16px; margin-bottom: 12px; color: #64748b; font-size: 13px; }
    .log-actions { display: flex; align-items: center; gap: 10px; }
    .level-filter { width: 150px; }
    .level-filter ::ng-deep .mat-mdc-form-field-subscript-wrapper { display: none; }
    .log-content { margin: 0; padding: 16px; max-height: calc(100vh - 230px); min-height: 300px; overflow: auto; background: #0f172a; color: #e2e8f0; border-radius: 8px; font: 12px/1.5 'Cascadia Mono', 'Consolas', monospace; text-align: left; }
    .log-line { white-space: pre; min-height: 1.5em; }
    .warning-line { background: #78350f; color: #fff; }
    .error-line { background: #7f1d1d; color: #fff; }
    .empty-state { min-height: 300px; display: flex; flex-direction: column; align-items: center; justify-content: center; color: #94a3b8; }
    .empty-state mat-icon { font-size: 48px; width: 48px; height: 48px; }
    @media (max-width: 700px) { .log-header { flex-direction: column; align-items: flex-start; } .date-navigation { align-self: center; } .log-toolbar { flex-direction: column; align-items: flex-start; } .log-actions { width: 100%; } .level-filter { flex: 1; } }
  `]
})
// Documentación: este componente coordina estado de interfaz y operaciones asíncronas que deben mantenerse alineadas con la API.
export class AdminLogComponent implements OnInit {
  selectedDate = this.toDateString(new Date());
  previousDate?: string;
  nextDate?: string;
  content = '';
  exists = false;
  loading = false;
  levelFilter: 'all' | 'info' | 'warning' | 'error' = 'all';

  get logLines(): string[] {
    return this.content ? this.content.split(/\r?\n/) : [];
  }

  get filteredLogLines(): string[] {
    if (this.levelFilter === 'all') return this.logLines;
    return this.logLines.filter(line => this.getLevel(line) === this.levelFilter);
  }

  getLevel(line: string): 'info' | 'warning' | 'error' | 'other' {
    if (/\[(ERR|FTL)\]/i.test(line)) return 'error';
    if (/\[(WRN)\]/i.test(line)) return 'warning';
    if (/\[(INF)\]/i.test(line)) return 'info';
    return 'other';
  }

  constructor(private adminService: AdminService) {}

  ngOnInit(): void { this.loadLog(this.selectedDate); }

  loadLog(date: string): void {
    this.loading = true;
    this.adminService.getLog(date).subscribe({
      next: (result) => {
        this.selectedDate = result.date;
        this.previousDate = result.previousDate;
        this.nextDate = result.nextDate;
        this.content = result.content ?? '';
        this.exists = result.exists;
        this.loading = false;
      },
      error: (err) => {
        console.error('Error al cargar el log', err);
        this.content = '';
        this.exists = false;
        this.previousDate = undefined;
        this.nextDate = undefined;
        this.loading = false;
      }
    });
  }

  private toDateString(date: Date): string { return date.toISOString().slice(0, 10); }
}
