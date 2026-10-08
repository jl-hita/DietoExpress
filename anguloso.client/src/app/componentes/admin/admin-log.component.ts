import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { FormsModule } from '@angular/forms';
import { MatSelectModule } from '@angular/material/select';
import { AdminLog, AdminService } from '../../servicios/admin.service';

@Component({
  selector: 'app-admin-log',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatIconModule, MatInputModule, MatSelectModule],
  template: `
    <div class="log-container">
      <div class="log-header">
        <div>
          <h1>Logs del sistema</h1>
          <p>Consulta el log de Serilog por día.</p>
        </div>
        <div class="date-navigation">
          <button mat-icon-button [disabled]="!previousDate || loading" (click)="loadLog(previousDate!)" aria-label="Día anterior"><mat-icon>chevron_left</mat-icon></button>
          <strong>{{ selectedDate | date:'dd/MM/yyyy' }}</strong>
          <button mat-icon-button [disabled]="!nextDate || loading" (click)="loadLog(nextDate!)" aria-label="Día siguiente"><mat-icon>chevron_right</mat-icon></button>
        </div>
      </div>

      <mat-card class="log-card">
        <div class="log-toolbar">
          <span>
            {{ !exists ? 'No hay log para este día' : (searchActive ? (searchResultCount + ' entradas encontradas para «' + searchText + '»') : (hasMoreOlder ? 'Mostrando la parte más reciente del log' : 'Log completo cargado')) }}
          </span>
          <div class="log-actions">
            <mat-form-field appearance="outline" class="search-filter">
              <mat-label>Buscar en el log</mat-label>
              <input matInput [(ngModel)]="searchText" (keyup.enter)="searchLog()" placeholder="Texto incluido en la entrada">
              <button *ngIf="searchText" mat-icon-button matSuffix type="button" (click)="clearSearch()" aria-label="Limpiar búsqueda">
                <mat-icon>close</mat-icon>
              </button>
            </mat-form-field>
            <button mat-stroked-button (click)="searchLog()" [disabled]="loading || searching || !searchText.trim()">
              <mat-icon>search</mat-icon> Buscar
            </button>
            <mat-form-field appearance="outline" class="level-filter">
              <mat-label>Nivel</mat-label>
              <mat-select [(value)]="levelFilter">
                <mat-option value="all">Todos</mat-option>
                <mat-option value="info">Info</mat-option>
                <mat-option value="warning">Warning</mat-option>
                <mat-option value="error">Error</mat-option>
              </mat-select>
            </mat-form-field>
            <button mat-stroked-button (click)="loadLog(selectedDate)" [disabled]="loading || searching">
              <mat-icon>refresh</mat-icon> Actualizar
            </button>
          </div>
        </div>

        <div *ngIf="exists" class="log-content" (scroll)="onLogScroll($event)">

          <div *ngIf="filteredLogEntries.length; else noMatches">
            <div
              *ngFor="let entry of filteredLogEntries"
              class="log-entry"
              [class.warning-line]="getLevel(entry) === 'warning'"
              [class.error-line]="getLevel(entry) === 'error'">{{ entry }}</div>
          </div>

          <ng-template #noMatches>
            <div class="no-matches">No hay entradas que coincidan con los filtros seleccionados.</div>
          </ng-template>
        </div>

        <div *ngIf="!exists" class="empty-state">
          <mat-icon>event_busy</mat-icon>
          <p>No existe ningún archivo de log para {{ selectedDate | date:'dd/MM/yyyy' }}.</p>
        </div>
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
    .search-filter { width: 300px; }
    .level-filter { width: 150px; }
    .search-filter ::ng-deep .mat-mdc-form-field-subscript-wrapper,
    .level-filter ::ng-deep .mat-mdc-form-field-subscript-wrapper { display: none; }
    .log-content { margin: 0; padding: 16px; max-height: calc(100vh - 230px); min-height: 300px; overflow: auto; background: #0f172a; color: #e2e8f0; border-radius: 8px; font: 12px/1.5 'Cascadia Mono', 'Consolas', monospace; text-align: left; }
    .log-entry { white-space: pre; min-height: 1.5em; padding: 0 4px; }
    .warning-line { background: #78350f; color: #fff; }
    .error-line { background: #7f1d1d; color: #fff; }
    .no-matches { padding: 24px 4px; color: #94a3b8; }
    .empty-state { min-height: 300px; display: flex; flex-direction: column; align-items: center; justify-content: center; color: #94a3b8; }
    .empty-state mat-icon { font-size: 48px; width: 48px; height: 48px; }
    @media (max-width: 700px) {
      .log-header { flex-direction: column; align-items: flex-start; }
      .date-navigation { align-self: center; }
      .log-toolbar { flex-direction: column; align-items: flex-start; }
      .log-actions { width: 100%; }
      .search-filter, .level-filter { flex: 1; width: auto; }
    }
  `]
})
// Documentación: el visor trabaja con entradas completas y bloques acotados para no materializar
// archivos de log enormes en el navegador. El filtro se aplica a la entrada Serilog completa,
// incluyendo excepciones y stack traces multilínea.
export class AdminLogComponent implements OnInit {
  // La carga histórica se activa automáticamente al alcanzar el inicio del visor.
  selectedDate = this.toDateString(new Date());
  previousDate?: string;
  nextDate?: string;
  exists = false;
  loading = false;
  loadingOlder = false;
  levelFilter: 'all' | 'info' | 'warning' | 'error' = 'all';
  searchText = '';
  searching = false;
  private searchActive = false;
  searchResultCount = 0;

  private logEntries: string[] = [];
  private oldestLoadedByte = 0;
  hasMoreOlder = false;

  get filteredLogEntries(): string[] {
    if (this.levelFilter === 'all') return this.logEntries;
    return this.logEntries.filter(entry => this.getLevel(entry) === this.levelFilter);
  }

  searchLog(): void {
    const query = this.searchText.trim();
    if (!query || this.searching) return;

    this.searching = true;
    this.adminService.searchLog(this.selectedDate, query).subscribe({
      next: result => {
        this.logEntries = result.entries;
        this.searchResultCount = result.count;
        this.searchActive = true;
        this.oldestLoadedByte = 0;
        this.hasMoreOlder = false;
        this.searching = false;
      },
      error: err => {
        console.error('Error al buscar en el log', err);
        this.searching = false;
      }
    });
  }

  clearSearch(): void {
    this.searchText = '';
    this.searchActive = false;
    this.searchResultCount = 0;
    this.loadLog(this.selectedDate);
  }

  getLevel(entry: string): 'info' | 'warning' | 'error' | 'other' {
    const header = entry.split(/\r?\n/, 1)[0];
    if (/\[(ERR|FTL)\]/i.test(header)) return 'error';
    if (/\[WRN\]/i.test(header)) return 'warning';
    if (/\[INF\]/i.test(header)) return 'info';
    return 'other';
  }

  constructor(private adminService: AdminService) {}

  ngOnInit(): void {
    this.loadLog(this.selectedDate);
  }

  loadLog(date: string): void {
    this.loading = true;
    this.adminService.getLogMetadata(date).subscribe({
      next: (result: AdminLog) => {
        this.selectedDate = result.date;
        this.previousDate = result.previousDate;
        this.nextDate = result.nextDate;
        this.exists = result.exists;
        this.logEntries = [];
        this.oldestLoadedByte = 0;
        this.hasMoreOlder = false;
        this.searchActive = false;
        this.searchResultCount = 0;

        if (!result.exists) {
          this.loading = false;
          return;
        }

        this.adminService.getLogChunk(result.date).subscribe({
          next: chunk => {
            this.logEntries = this.parseEntries(chunk.content);
            this.oldestLoadedByte = chunk.startByte;
            this.hasMoreOlder = chunk.hasMore;
            this.loading = false;
          },
          error: err => {
            console.error('Error al cargar el contenido del log', err);
            this.exists = false;
            this.loading = false;
          }
        });
      },
      error: err => {
        console.error('Error al cargar los metadatos del log', err);
        this.logEntries = [];
        this.exists = false;
        this.previousDate = undefined;
        this.nextDate = undefined;
        this.loading = false;
      }
    });
  }

  onLogScroll(event: Event): void {
    const element = event.target as HTMLElement;
    // Al acercarnos al principio pedimos el bloque anterior automáticamente. El umbral
    // evita tener que llegar exactamente al píxel 0 y permite encadenar varias cargas.
    if (element.scrollTop <= 180) {
      this.loadOlder(element);
    }
  }

  loadOlder(element?: HTMLElement): void {
    if (!this.exists || !this.hasMoreOlder || this.loadingOlder) return;

    this.loadingOlder = true;
    const previousScrollHeight = element?.scrollHeight ?? 0;
    const previousScrollTop = element?.scrollTop ?? 0;
    this.adminService.getLogChunk(this.selectedDate, this.oldestLoadedByte).subscribe({
      next: chunk => {
        const olderEntries = this.parseEntries(chunk.content);
        this.logEntries = [...olderEntries, ...this.logEntries];
        this.oldestLoadedByte = chunk.startByte;
        this.hasMoreOlder = chunk.hasMore;
        this.loadingOlder = false;

        // Al insertar contenido arriba, conservamos la posición visual para que el usuario
        // pueda seguir desplazándose hacia atrás sin que el scroll salte al inicio.
        if (element) {
          setTimeout(() => {
            element.scrollTop = previousScrollTop + (element.scrollHeight - previousScrollHeight);
          });
        }
      },
      error: err => {
        console.error('Error al cargar bloques anteriores del log', err);
        this.loadingOlder = false;
      }
    });
  }

  private parseEntries(content: string): string[] {
    if (!content) return [];

    // Cada entrada empieza por la cabecera de Serilog. El lookahead conserva todos los
    // saltos de línea siguientes dentro de la misma entrada, incluidos stack traces.
    return content
      .split(/(?=^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2} \[[A-Z]{3}\])/m)
      .map(entry => entry.replace(/^\r?\n+|\r?\n+$/g, ''))
      .filter(entry => entry.length > 0);
  }

  private toDateString(date: Date): string {
    return date.toISOString().slice(0, 10);
  }
}
