import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatTableModule } from '@angular/material/table';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { ClientService } from '../../servicios/client.service';

export interface BioimpedanceDialogData {
  clientId: number;
  clientName: string;
}

@Component({
  selector: 'app-bioimpedance-import-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatSelectModule,
    MatFormFieldModule,
    MatCheckboxModule,
    MatTableModule,
    MatProgressBarModule,
    MatSnackBarModule
  ],
  template: `
    <div class="dialog-container">
      <div class="header">
        <div class="title-row">
          <mat-icon class="brand-icon">speed</mat-icon>
          <div>
            <h2 mat-dialog-title class="dialog-title">Importar Báscula Bioimpedancia</h2>
            <p class="subtitle">Tanita, InBody u otros analizadores para <strong>{{ data.clientName }}</strong></p>
          </div>
        </div>
        <button mat-icon-button (click)="dialogRef.close()"><mat-icon>close</mat-icon></button>
      </div>

      <mat-dialog-content class="content">
        <!-- Paso 1: Selección de Archivo y Báscula -->
        <div class="upload-section" *ngIf="!previewData">
          <div class="device-select">
            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Báscula o Fabricante</mat-label>
              <mat-select [(ngModel)]="selectedDevice">
                <mat-option value="auto">Autodetectar dispositivo (Recomendado)</mat-option>
                <mat-option value="Tanita">Tanita (GMON / Healthware / MC Series)</mat-option>
                <mat-option value="InBody">InBody (Lookin'Body / 270 / 770)</mat-option>
                <mat-option value="Genérico">Archivo CSV / Delimitado Genérico</mat-option>
              </mat-select>
            </mat-form-field>
          </div>

          <div
            class="dropzone"
            [class.dragover]="isDragOver"
            (dragover)="onDragOver($event)"
            (dragleave)="onDragLeave($event)"
            (drop)="onDrop($event)"
            (click)="fileInput.click()">
            <input #fileInput type="file" (change)="onFileSelected($event)" accept=".csv,.txt,.tsv" style="display:none" />
            <mat-icon class="upload-icon">cloud_upload</mat-icon>
            <p class="drop-text">Arrastra aquí el archivo exportado o <span>haz clic para buscar</span></p>
            <p class="format-hint">Formatos compatibles: .CSV, .TXT, .TSV generados por el software de Tanita o InBody</p>
            <div *ngIf="selectedFile" class="file-chip">
              <mat-icon>description</mat-icon> {{ selectedFile.name }} ({{ (selectedFile.size / 1024) | number:'1.1-1' }} KB)
            </div>
          </div>

          <div class="info-alert">
            <mat-icon>lightbulb</mat-icon>
            <span>El analizador detectará automáticamente columnas como Peso, % Grasa Corporal, Masa Muscular Esquelética y Grasa Visceral.</span>
          </div>
        </div>

        <mat-progress-bar mode="indeterminate" *ngIf="loading"></mat-progress-bar>

        <!-- Paso 2: Previsualización de Mediciones -->
        <div class="preview-section" *ngIf="previewData && !loading">
          <div class="preview-header">
            <div class="detected-badge">
              <mat-icon>check_circle</mat-icon>
              <span>Dispositivo detectado: <strong>{{ previewData.detectedBrand }}</strong></span>
            </div>
            <span class="count-tag">{{ selectedRowsCount }} de {{ previewData.rows.length }} mediciones seleccionadas</span>
          </div>

          <div *ngIf="previewData.warnings?.length" class="warnings-box">
            <mat-icon>warning</mat-icon>
            <div>
              <div *ngFor="let w of previewData.warnings">{{ w }}</div>
            </div>
          </div>

          <div class="table-container">
            <table mat-table [dataSource]="previewData.rows" class="preview-table">
              <!-- Checkbox Column -->
              <ng-container matColumnDef="select">
                <th mat-header-cell *matHeaderCellDef>
                  <mat-checkbox (change)="toggleAll($event)" [checked]="isAllSelected()"></mat-checkbox>
                </th>
                <td mat-cell *matCellDef="let row">
                  <mat-checkbox [(ngModel)]="row.selected"></mat-checkbox>
                </td>
              </ng-container>

              <!-- Date Column -->
              <ng-container matColumnDef="date">
                <th mat-header-cell *matHeaderCellDef>Fecha Medición</th>
                <td mat-cell *matCellDef="let row">
                  <strong>{{ row.measurementDate | date:'dd/MM/yyyy' }}</strong>
                  <span *ngIf="row.alreadyExists" class="already-badge" title="Ya existe una medición en esta fecha. Si se importa, se actualizarán los valores.">
                    Existe
                  </span>
                </td>
              </ng-container>

              <!-- Weight Column -->
              <ng-container matColumnDef="weight">
                <th mat-header-cell *matHeaderCellDef>Peso (kg)</th>
                <td mat-cell *matCellDef="let row">
                  <span *ngIf="row.weight != null" class="val-weight">{{ row.weight }} kg</span>
                  <span *ngIf="row.weight == null" class="text-muted">-</span>
                </td>
              </ng-container>

              <!-- Fat Column -->
              <ng-container matColumnDef="bodyFat">
                <th mat-header-cell *matHeaderCellDef>% Grasa</th>
                <td mat-cell *matCellDef="let row">
                  <span *ngIf="row.bodyFat != null" class="val-fat">{{ row.bodyFat }}%</span>
                  <span *ngIf="row.bodyFat == null" class="text-muted">-</span>
                </td>
              </ng-container>

              <!-- Muscle Column -->
              <ng-container matColumnDef="muscleMass">
                <th mat-header-cell *matHeaderCellDef>Masa Muscular</th>
                <td mat-cell *matCellDef="let row">
                  <span *ngIf="row.muscleMass != null" class="val-muscle">{{ row.muscleMass }} kg</span>
                  <span *ngIf="row.muscleMass == null" class="text-muted">-</span>
                </td>
              </ng-container>

              <!-- Visceral Fat Column -->
              <ng-container matColumnDef="visceralFat">
                <th mat-header-cell *matHeaderCellDef>Grasa Visceral</th>
                <td mat-cell *matCellDef="let row">
                  <span *ngIf="row.visceralFat != null">{{ row.visceralFat }}</span>
                  <span *ngIf="row.visceralFat == null" class="text-muted">-</span>
                </td>
              </ng-container>

              <tr mat-header-row *matHeaderRowDef="displayedColumns"></tr>
              <tr mat-row *matRowDef="let row; columns: displayedColumns;"></tr>
            </table>
          </div>
        </div>
      </mat-dialog-content>

      <mat-dialog-actions align="end" class="actions-bar">
        <button mat-button (click)="dialogRef.close()" [disabled]="loading">Cancelar</button>

        <button
          *ngIf="!previewData"
          mat-flat-button
          color="primary"
          [disabled]="!selectedFile || loading"
          (click)="uploadAndPreview()">
          <mat-icon>visibility</mat-icon> Previsualizar Datos
        </button>

        <button
          *ngIf="previewData"
          mat-stroked-button
          (click)="resetFile()"
          [disabled]="loading">
          <mat-icon>arrow_back</mat-icon> Cambiar Archivo
        </button>

        <button
          *ngIf="previewData"
          mat-flat-button
          color="primary"
          [disabled]="selectedRowsCount === 0 || loading"
          (click)="confirmImport()">
          <mat-icon>file_download_done</mat-icon> Importar {{ selectedRowsCount }} Registros
        </button>
      </mat-dialog-actions>
    </div>
  `,
  styles: [`
    .dialog-container {
      min-width: 620px;
      max-width: 800px;
      font-family: 'Roboto', sans-serif;
    }
    .header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 16px 24px 8px;
      border-bottom: 1px solid #e2e8f0;
    }
    .title-row {
      display: flex;
      align-items: center;
      gap: 12px;
    }
    .brand-icon {
      font-size: 32px;
      width: 32px;
      height: 32px;
      color: #0d9488;
    }
    .dialog-title {
      margin: 0;
      font-size: 20px;
      font-weight: 700;
      color: #0f172a;
    }
    .subtitle {
      margin: 2px 0 0;
      color: #64748b;
      font-size: 13px;
    }
    .content {
      padding: 20px 24px;
      max-height: 70vh;
      overflow-y: auto;
    }
    .full-width {
      width: 100%;
    }
    .dropzone {
      border: 2px dashed #cbd5e1;
      border-radius: 12px;
      padding: 32px 16px;
      text-align: center;
      cursor: pointer;
      background: #f8fafc;
      transition: all 0.2s ease;
      margin-bottom: 16px;
    }
    .dropzone:hover, .dropzone.dragover {
      border-color: #0d9488;
      background: #f0fdfa;
    }
    .upload-icon {
      font-size: 48px;
      width: 48px;
      height: 48px;
      color: #0d9488;
      margin-bottom: 8px;
    }
    .drop-text {
      margin: 0 0 6px;
      font-size: 15px;
      color: #334155;
    }
    .drop-text span {
      color: #0d9488;
      font-weight: 600;
      text-decoration: underline;
    }
    .format-hint {
      margin: 0;
      font-size: 12px;
      color: #94a3b8;
    }
    .file-chip {
      margin-top: 12px;
      display: inline-flex;
      align-items: center;
      gap: 6px;
      background: #e2e8f0;
      padding: 6px 12px;
      border-radius: 16px;
      font-size: 13px;
      color: #0f172a;
      font-weight: 500;
    }
    .info-alert {
      display: flex;
      align-items: center;
      gap: 8px;
      background: #eff6ff;
      border: 1px solid #bfdbfe;
      color: #1e40af;
      padding: 10px 14px;
      border-radius: 8px;
      font-size: 13px;
    }
    .info-alert mat-icon {
      font-size: 20px;
      width: 20px;
      height: 20px;
      flex-shrink: 0;
    }
    .preview-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 12px;
    }
    .detected-badge {
      display: flex;
      align-items: center;
      gap: 6px;
      color: #0d9488;
      font-size: 14px;
    }
    .detected-badge mat-icon {
      font-size: 18px;
      width: 18px;
      height: 18px;
    }
    .count-tag {
      background: #f1f5f9;
      color: #475569;
      padding: 4px 10px;
      border-radius: 12px;
      font-size: 12px;
      font-weight: 600;
    }
    .warnings-box {
      display: flex;
      gap: 8px;
      background: #fffbeb;
      border: 1px solid #fef3c7;
      color: #b45309;
      padding: 10px 14px;
      border-radius: 8px;
      font-size: 13px;
      margin-bottom: 12px;
    }
    .warnings-box mat-icon {
      font-size: 20px;
      width: 20px;
      height: 20px;
      flex-shrink: 0;
    }
    .table-container {
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      overflow: hidden;
      max-height: 320px;
      overflow-y: auto;
    }
    .preview-table {
      width: 100%;
    }
    .val-weight { font-weight: 600; color: #0284c7; }
    .val-fat { font-weight: 600; color: #ea580c; }
    .val-muscle { font-weight: 600; color: #16a34a; }
    .text-muted { color: #94a3b8; }
    .already-badge {
      font-size: 10px;
      background: #fee2e2;
      color: #991b1b;
      padding: 2px 6px;
      border-radius: 8px;
      margin-left: 6px;
      font-weight: 600;
    }
    .actions-bar {
      padding: 12px 24px 16px;
      border-top: 1px solid #e2e8f0;
    }
  `]
})
export class BioimpedanceImportDialogComponent {
  selectedDevice = 'auto';
  selectedFile: File | null = null;
  isDragOver = false;
  loading = false;

  previewData: any = null;
  displayedColumns = ['select', 'date', 'weight', 'bodyFat', 'muscleMass', 'visceralFat'];

  constructor(
    public dialogRef: MatDialogRef<BioimpedanceImportDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: BioimpedanceDialogData,
    private clientService: ClientService,
    private snackBar: MatSnackBar
  ) {}

  onDragOver(e: DragEvent): void {
    e.preventDefault();
    e.stopPropagation();
    this.isDragOver = true;
  }

  onDragLeave(e: DragEvent): void {
    e.preventDefault();
    e.stopPropagation();
    this.isDragOver = false;
  }

  onDrop(e: DragEvent): void {
    e.preventDefault();
    e.stopPropagation();
    this.isDragOver = false;
    if (e.dataTransfer?.files && e.dataTransfer.files.length > 0) {
      this.selectedFile = e.dataTransfer.files[0];
    }
  }

  onFileSelected(e: Event): void {
    const target = e.target as HTMLInputElement;
    if (target.files && target.files.length > 0) {
      this.selectedFile = target.files[0];
    }
  }

  uploadAndPreview(): void {
    if (!this.selectedFile) return;

    this.loading = true;
    this.clientService.previewBioimpedanceImport(this.data.clientId, this.selectedFile, this.selectedDevice).subscribe({
      next: (res) => {
        this.loading = false;
        // Marcar todas seleccionadas por defecto
        if (res?.rows) {
          res.rows.forEach((r: any) => r.selected = true);
        }
        this.previewData = res;
      },
      error: (err) => {
        this.loading = false;
        const msg = err.error?.message || err.error || 'Error al analizar el archivo de bioimpedancia.';
        this.snackBar.open(msg, 'Cerrar', { duration: 5000 });
      }
    });
  }

  resetFile(): void {
    this.previewData = null;
  }

  get selectedRowsCount(): number {
    if (!this.previewData?.rows) return 0;
    return this.previewData.rows.filter((r: any) => r.selected).length;
  }

  isAllSelected(): boolean {
    if (!this.previewData?.rows?.length) return false;
    return this.previewData.rows.every((r: any) => r.selected);
  }

  toggleAll(event: any): void {
    const checked = event.checked;
    this.previewData.rows.forEach((r: any) => r.selected = checked);
  }

  confirmImport(): void {
    const selected = this.previewData.rows.filter((r: any) => r.selected);
    if (selected.length === 0) return;

    this.loading = true;
    this.clientService.confirmBioimpedanceImport(this.data.clientId, selected).subscribe({
      next: (res) => {
        this.loading = false;
        this.snackBar.open(res.message || 'Importación completada con éxito.', 'OK', { duration: 4000 });
        this.dialogRef.close(true); // Cerrar indicando éxito para refrescar
      },
      error: (err) => {
        this.loading = false;
        const msg = err.error?.message || err.error || 'Error al guardar las mediciones biométricas.';
        this.snackBar.open(msg, 'Cerrar', { duration: 5000 });
      }
    });
  }
}
