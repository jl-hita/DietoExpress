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
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSnackBarModule],
  template: `
  <main class="page"><mat-card><p class="eyebrow">BIBLIOTECA COLABORATIVA</p><h1>Proponer un ejercicio</h1>
    <p>Los ejercicios pendientes solo estarán disponibles para ti. Puedes usarlos en rutinas manuales y en el generador automático; los demás profesionales no podrán usarlos hasta que superadmin los apruebe.</p>
    <form (ngSubmit)="submit()"><mat-form-field appearance="outline"><mat-label>Nombre del ejercicio *</mat-label><input matInput name="name" [(ngModel)]="name" maxlength="200" required/></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Descripción e instrucciones</mat-label><textarea matInput name="description" [(ngModel)]="description" rows="4"></textarea></mat-form-field>
      <div class="row"><mat-form-field appearance="outline"><mat-label>Material necesario</mat-label><input matInput name="equipment" [(ngModel)]="equipment" placeholder="Ej. mancuernas, polea"/></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Zona corporal</mat-label><input matInput name="bodyPart" [(ngModel)]="bodyPart" placeholder="Ej. espalda, piernas"/></mat-form-field></div>
      <div class="row"><mat-form-field appearance="outline"><mat-label>Patrón de movimiento *</mat-label><mat-select name="movementPattern" [(ngModel)]="movementPattern" required><mat-option *ngFor="let item of movementPatterns" [value]="item">{{ item }}</mat-option></mat-select></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Experiencia mínima *</mat-label><mat-select name="minimumExperience" [(ngModel)]="minimumExperience" required><mat-option value="Beginner">Principiante</mat-option><mat-option value="Intermediate">Intermedio</mat-option><mat-option value="Advanced">Avanzado</mat-option></mat-select></mat-form-field></div>
      <mat-form-field appearance="outline"><mat-label>Músculos principales * (separados por comas)</mat-label><input matInput name="primaryMuscles" [(ngModel)]="primaryMusclesText" required placeholder="Ej. Quadriceps, Glutes"/><mat-hint>Usa nombres técnicos: Chest, Lats, UpperBack, Deltoids, Biceps, Triceps, Forearms, Abdominals, Obliques, ErectorSpinae, Glutes, Quadriceps, Hamstrings, Calves.</mat-hint></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Músculos secundarios (separados por comas)</mat-label><input matInput name="secondaryMuscles" [(ngModel)]="secondaryMusclesText" placeholder="Ej. Hamstrings, ErectorSpinae"/></mat-form-field>
      <div class="row"><mat-form-field appearance="outline"><mat-label>Objetivos compatibles *</mat-label><mat-select name="suitableGoals" [(ngModel)]="suitableGoals" multiple required><mat-option value="GeneralFitness">Condición física general</mat-option><mat-option value="Hypertrophy">Hipertrofia</mat-option><mat-option value="Strength">Fuerza</mat-option></mat-select></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Material para el generador (códigos separados por comas)</mat-label><input matInput name="requiredEquipment" [(ngModel)]="requiredEquipmentText" placeholder="barbell, dumbbell, cable"/><mat-hint>Deja vacío para peso corporal/sin material. Códigos disponibles: barbell, dumbbell, pull_up_bar, cable, kettlebell, ez_bar, shoulder_press_machine, ab_wheel.</mat-hint></mat-form-field></div>
      <button mat-flat-button color="primary" type="submit" [disabled]="saving || !name.trim()">{{ saving ? 'Enviando…' : 'Enviar a revisión' }}</button>
    </form>
    <p class="success" *ngIf="submittedId">Enviado correctamente. Código: {{ submittedId }}. Estado: pendiente de revisión.</p>
  </mat-card></main>`,
  styles: [`.page{max-width:850px;margin:auto;padding:24px}.page mat-card{padding:24px}.eyebrow{font-size:.75rem;letter-spacing:.12em;font-weight:700;color:#5874a5}.page form{display:flex;flex-direction:column;gap:8px}.row{display:grid;grid-template-columns:1fr 1fr;gap:12px}.row mat-form-field{width:100%}.success{padding:12px;background:#e4f7eb;border-radius:8px;margin-top:16px}@media(max-width:650px){.page{padding:12px}.row{grid-template-columns:1fr}}`]
})
export class ExerciseContributionsComponent {
  name=''; description=''; equipment=''; bodyPart=''; movementPattern='Squat'; minimumExperience='Beginner'; primaryMusclesText=''; secondaryMusclesText=''; suitableGoals:string[]=['GeneralFitness','Hypertrophy','Strength']; requiredEquipmentText=''; saving=false; submittedId='';
  movementPatterns=['Squat','Hinge','HorizontalPush','VerticalPush','HorizontalPull','VerticalPull','SingleLeg','Core'];
  constructor(private http: HttpClient, private snack: MatSnackBar) {}
  submit(): void {
    if (!this.name.trim() || this.saving) return;
    this.saving=true; this.submittedId='';
    const primaryMuscles=this.primaryMusclesText.split(',').map(x=>x.trim()).filter(Boolean); const secondaryMuscles=this.secondaryMusclesText.split(',').map(x=>x.trim()).filter(Boolean); const requiredEquipment=this.requiredEquipmentText.split(',').map(x=>x.trim()).filter(Boolean);
    this.http.post<{id:string}>('/api/exercise-catalog/contributions',{name:this.name.trim(),description:this.description.trim()||null,equipment:this.equipment.trim()||null,bodyPart:this.bodyPart.trim()||null,movementPattern:this.movementPattern,minimumExperience:this.minimumExperience,primaryMuscles,secondaryMuscles,suitableGoals:this.suitableGoals,requiredEquipment,selectionPriority:50}).subscribe({
      next:r=>{this.saving=false;this.submittedId=r.id;this.name='';this.description='';this.equipment='';this.bodyPart='';this.primaryMusclesText='';this.secondaryMusclesText='';this.requiredEquipmentText='';this.snack.open('Ejercicio enviado a revisión.','Cerrar',{duration:3000});},
      error:e=>{this.saving=false;this.snack.open(e?.error?.error||'No se pudo enviar el ejercicio.','Cerrar',{duration:4000});}
    });
  }
}
