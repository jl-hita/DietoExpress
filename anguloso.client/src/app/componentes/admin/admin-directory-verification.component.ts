import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatTableModule } from '@angular/material/table';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { AdminService, DirectoryVerificationItem } from '../../servicios/admin.service';

@Component({
  selector: 'app-admin-directory-verification',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatTableModule, MatSnackBarModule],
  template: `
    <div class="admin-container">
      <mat-card>
        <h1>Verificación del directorio</h1>
        <p>Solo las fichas verificadas y publicadas son visibles públicamente. La revisión de identidad y titulación debe basarse en la documentación que el SuperAdmin haya comprobado.</p>

        <mat-form-field appearance="outline">
          <mat-label>Estado</mat-label>
          <mat-select [(ngModel)]="statusFilter" (selectionChange)="load()">
            <mat-option value="">Todos</mat-option>
            <mat-option value="pending">Pendientes</mat-option>
            <mat-option value="verified">Verificadas</mat-option>
            <mat-option value="published">Publicadas</mat-option>
            <mat-option value="rejected">Rechazadas</mat-option>
          </mat-select>
        </mat-form-field>

        <div class="table-wrap">
          <table mat-table [dataSource]="items">
            <ng-container matColumnDef="professional">
              <th mat-header-cell *matHeaderCellDef>Profesional</th>
              <td mat-cell *matCellDef="let item">
                <strong>{{ item.fullName || item.username }}</strong><br>
                <small>{{ item.email }} · {{ item.city || 'Sin ciudad' }}</small>
              </td>
            </ng-container>

            <ng-container matColumnDef="profile">
              <th mat-header-cell *matHeaderCellDef>Perfil</th>
              <td mat-cell *matCellDef="let item">
                <span>{{ item.specialties || 'Sin especialidades' }}</span><br>
                <small>{{ item.onlineConsultations ? 'Online disponible' : 'Solo presencial' }}</small>
              </td>
            </ng-container>

            <ng-container matColumnDef="status">
              <th mat-header-cell *matHeaderCellDef>Estado</th>
              <td mat-cell *matCellDef="let item">{{ label(item.publicationStatus) }}</td>
            </ng-container>

            <ng-container matColumnDef="actions">
              <th mat-header-cell *matHeaderCellDef>Acciones</th>
              <td mat-cell *matCellDef="let item">
                <button mat-stroked-button *ngIf="item.publicationStatus === 'pending'" (click)="setStatus(item, 'verified')">Verificar</button>
                <button mat-flat-button color="primary" *ngIf="item.publicationStatus === 'verified'" (click)="setStatus(item, 'published')">Publicar</button>
                <button mat-stroked-button *ngIf="item.publicationStatus === 'published'" (click)="setStatus(item, 'verified')">Retirar publicación</button>
                <button mat-button *ngIf="item.publicationStatus !== 'rejected'" (click)="setStatus(item, 'rejected')">Rechazar</button>
                <button mat-button *ngIf="item.publicationStatus === 'rejected'" (click)="setStatus(item, 'pending')">Reabrir revisión</button>
              </td>
            </ng-container>

            <tr mat-header-row *matHeaderRowDef="columns"></tr>
            <tr mat-row *matRowDef="let row; columns: columns;"></tr>
          </table>
          <p *ngIf="!items.length">No hay fichas con el estado seleccionado.</p>
        </div>
      </mat-card>
    </div>
  `,
  styles: [`
    .admin-container { padding: 24px; }
    .table-wrap { overflow-x: auto; margin-top: 16px; }
    table { width: 100%; }
    mat-card { margin-bottom: 24px; }
  `]
})
export class AdminDirectoryVerificationComponent implements OnInit {
  items: DirectoryVerificationItem[] = [];
  columns = ['professional', 'profile', 'status', 'actions'];
  statusFilter = '';

  constructor(private admin: AdminService, private snack: MatSnackBar) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.admin.getDirectoryVerification(this.statusFilter || undefined).subscribe({
      next: items => this.items = items,
      error: () => this.snack.open('No se pudo cargar la revisión del directorio.', 'Cerrar', { duration: 4000 })
    });
  }

  label(status: string): string {
    return ({ draft: 'Borrador', pending: 'Pendiente', verified: 'Verificada', published: 'Publicada', rejected: 'Rechazada' } as Record<string, string>)[status] ?? status;
  }

  setStatus(item: DirectoryVerificationItem, status: 'pending' | 'verified' | 'published' | 'rejected' | 'draft'): void {
    const note = status === 'rejected' ? prompt('Motivo de rechazo (opcional):', item.verificationNote || '') : item.verificationNote || null;
    if (status === 'rejected' && note === null) return;

    this.admin.updateDirectoryVerification(item.id, { status, note }).subscribe({
      next: updated => {
        const index = this.items.findIndex(x => x.id === updated.id);
        if (index >= 0) this.items[index] = updated;
        this.items = [...this.items];
        this.snack.open('Estado actualizado.', 'Cerrar', { duration: 2500 });
      },
      error: error => this.snack.open(error?.error || 'No se pudo actualizar el estado.', 'Cerrar', { duration: 4000 })
    });
  }
}