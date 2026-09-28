import { Component, Inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AdminUser } from '../../servicios/admin.service';

@Component({
  selector: 'app-delete-account-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title>Eliminar cuenta</h2>

    <mat-dialog-content>
      <div class="warning-box">
        <mat-icon>warning</mat-icon>
        <div>
          <strong>Esta acción es permanente.</strong>
          <p>
            Se eliminará la cuenta de <strong>{{ data.fullName || data.username }}</strong>
            ({{ data.email || data.username }}) junto con sus datos asociados.
            Esta acción no se puede deshacer.
          </p>
        </div>
      </div>

      <p class="confirmation-text">
        ¿Estás seguro de que quieres eliminar esta cuenta?
      </p>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="cancel()">Cancelar</button>
      <button mat-flat-button color="warn" (click)="confirm()">
        <mat-icon>delete_forever</mat-icon>
        Eliminar cuenta
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    mat-dialog-content {
      min-width: 420px;
      max-width: 560px;
    }

    .warning-box {
      display: flex;
      gap: 14px;
      padding: 16px;
      border-radius: 8px;
      background: #fef2f2;
      color: #7f1d1d;
    }

    .warning-box mat-icon {
      flex-shrink: 0;
    }

    .warning-box p {
      margin: 8px 0 0;
      line-height: 1.45;
    }

    .confirmation-text {
      margin: 20px 0 4px;
      font-weight: 600;
      color: #334155;
    }

    @media (max-width: 600px) {
      mat-dialog-content {
        min-width: 0;
      }
    }
  `]
})
export class DeleteAccountDialogComponent {
  constructor(
    private dialogRef: MatDialogRef<DeleteAccountDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: AdminUser
  ) {}

  cancel(): void {
    this.dialogRef.close(false);
  }

  confirm(): void {
    this.dialogRef.close(true);
  }
}
