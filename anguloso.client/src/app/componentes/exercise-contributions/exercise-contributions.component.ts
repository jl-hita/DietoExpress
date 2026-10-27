import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

@Component({
  standalone: true, selector: 'app-exercise-contributions',
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSnackBarModule],
  template: `
  <main class="page"><mat-card><p class="eyebrow">BIBLIOTECA COLABORATIVA</p><h1>Proponer un ejercicio</h1>
    <p>Envía ejercicios propios o variaciones útiles. Quedarán pendientes hasta que el equipo de DietoExpress los revise; no se incorporan automáticamente al generador.</p>
    <form (ngSubmit)="submit()"><mat-form-field appearance="outline"><mat-label>Nombre del ejercicio *</mat-label><input matInput name="name" [(ngModel)]="name" maxlength="200" required/></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Descripción e instrucciones</mat-label><textarea matInput name="description" [(ngModel)]="description" rows="4"></textarea></mat-form-field>
      <div class="row"><mat-form-field appearance="outline"><mat-label>Material necesario</mat-label><input matInput name="equipment" [(ngModel)]="equipment" placeholder="Ej. mancuernas, polea"/></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Zona corporal</mat-label><input matInput name="bodyPart" [(ngModel)]="bodyPart" placeholder="Ej. espalda, piernas"/></mat-form-field></div>
      <button mat-flat-button color="primary" type="submit" [disabled]="saving || !name.trim()">{{ saving ? 'Enviando…' : 'Enviar a revisión' }}</button>
    </form>
    <p class="success" *ngIf="submittedId">Enviado correctamente. Código: {{ submittedId }}. Estado: pendiente de revisión.</p>
  </mat-card></main>`,
  styles: [`.page{max-width:850px;margin:auto;padding:24px}.page mat-card{padding:24px}.eyebrow{font-size:.75rem;letter-spacing:.12em;font-weight:700;color:#5874a5}.page form{display:flex;flex-direction:column;gap:8px}.row{display:grid;grid-template-columns:1fr 1fr;gap:12px}.row mat-form-field{width:100%}.success{padding:12px;background:#e4f7eb;border-radius:8px;margin-top:16px}@media(max-width:650px){.page{padding:12px}.row{grid-template-columns:1fr}}`]
})
export class ExerciseContributionsComponent {
  name=''; description=''; equipment=''; bodyPart=''; saving=false; submittedId='';
  constructor(private http: HttpClient, private snack: MatSnackBar) {}
  submit(): void {
    if (!this.name.trim() || this.saving) return;
    this.saving=true; this.submittedId='';
    this.http.post<{id:string}>('/api/exercise-catalog/contributions',{name:this.name.trim(),description:this.description.trim()||null,equipment:this.equipment.trim()||null,bodyPart:this.bodyPart.trim()||null}).subscribe({
      next:r=>{this.saving=false;this.submittedId=r.id;this.name='';this.description='';this.equipment='';this.bodyPart='';this.snack.open('Ejercicio enviado a revisión.','Cerrar',{duration:3000});},
      error:e=>{this.saving=false;this.snack.open(e?.error?.error||'No se pudo enviar el ejercicio.','Cerrar',{duration:4000});}
    });
  }
}
