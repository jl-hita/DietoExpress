import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { AdminService } from '../../servicios/admin.service';

@Component({
  selector: 'app-admin-log',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, MatIconModule],
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
        <div class="log-toolbar"><span>{{ exists ? 'Log disponible' : 'No hay log para este día' }}</span><button mat-stroked-button (click)="loadLog(selectedDate)" [disabled]="loading"><mat-icon>refresh</mat-icon> Actualizar</button></div>
        <pre *ngIf="exists" class="log-content">{{ content }}</pre>
        <div *ngIf="!exists" class="empty-state"><mat-icon>event_busy</mat-icon><p>No existe ningún archivo de log para {{ selectedDate | date:'dd/MM/yyyy' }}.</p></div>
      </mat-card>
    </div>
  `,
  styles: [`
    .log-container { padding: 24px; max-width: 1400px; margin: 0 auto; font-family: 'Roboto', sans-serif; }
    .log-header { display: flex; justify-content: space-between; align-items: center; gap: 24px; margin-bottom: 20px; }
    h1 { margin: 0; color: #0f172a; font-size: 26px; }
    .log-header p { margin: 4px 0 0; color: #64748b; font-size: 14px; }
    .date-navigation { display: flex; align-items: center; gap: 12px; white-space: nowrap; }
    .log-card { padding: 16px; border-radius: 12px; }
    .log-toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 12px; color: #64748b; font-size: 13px; }
    .log-content { margin: 0; padding: 16px; max-height: calc(100vh - 230px); min-height: 300px; overflow: auto; background: #0f172a; color: #e2e8f0; border-radius: 8px; font: 12px/1.5 'Cascadia Mono', 'Consolas', monospace; white-space: pre; }
    .empty-state { min-height: 300px; display: flex; flex-direction: column; align-items: center; justify-content: center; color: #94a3b8; }
    .empty-state mat-icon { font-size: 48px; width: 48px; height: 48px; }
    @media (max-width: 700px) { .log-header { flex-direction: column; align-items: flex-start; } .date-navigation { align-self: center; } }
  `]
})
export class AdminLogComponent implements OnInit {
  selectedDate = this.toDateString(new Date());
  previousDate?: string;
  nextDate?: string;
  content = '';
  exists = false;
  loading = false;

  constructor(private adminService: AdminService) {}

  ngOnInit(): void { this.loadLog(this.selectedDate); }

  loadLog(date: string): void {
    this.loading = true;
    this.adminService.getLog(date).subscribe({
      next: (result) => {
        this.selectedDate = result.date;
        this.previousDate = result.previousDate;
        this.nextDate = result.nextDate;
        this.content = result.content;
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
