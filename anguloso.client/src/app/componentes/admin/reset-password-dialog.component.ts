import { Component, Inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatDialogRef, MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { AdminUser } from '../../servicios/admin.service';

@Component({
  selector: 'app-reset-password-dialog',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatDialogModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule
  ],
  template: `
    <h2 mat-dialog-title>Restablecer Contraseña: {{ data.username }}</h2>
    <mat-dialog-content class="dialog-content">
      <p class="desc">Introduce la nueva contraseña temporal o definitiva para este usuario.</p>

      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Nueva Contraseña</mat-label>
        <input matInput type="password" [(ngModel)]="newPassword" placeholder="Mínimo 12 caracteres" />
      </mat-form-field>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">Cancelar</button>
      <button mat-flat-button color="warn" [disabled]="!newPassword || newPassword.length < 12" (click)="save()">
        Restablecer
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .dialog-content {
      display: flex;
      flex-direction: column;
      min-width: 320px;
    }
    .desc {
      color: #64748b;
      font-size: 14px;
      margin-top: 0;
    }
    .full-width {
      width: 100%;
    }
  `]
})
export class ResetPasswordDialogComponent {
  newPassword = '';

  constructor(
    public dialogRef: MatDialogRef<ResetPasswordDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: AdminUser
  ) {}

  save(): void {
    this.dialogRef.close(this.newPassword);
  }
}
