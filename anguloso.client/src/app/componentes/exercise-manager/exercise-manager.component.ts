import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpParams } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

interface ExerciseRow {
  id: string; nameEs?: string; nameEn: string; category?: string; bodyPart?: string; equipment?: string;
  primaryMuscles: string[]; secondaryMuscles: string[]; source: string; reviewStatus: string;
  movementPattern?: string; reviewedPrimaryMuscles?: string[]; reviewedSecondaryMuscles?: string[];
  ownerUserId?: number; ownerMovementPattern?: string; ownerPrimaryMuscles?: string[]; ownerSecondaryMuscles?: string[];
  ownerMinimumExperience?: string; ownerSuitableGoals?: string[]; ownerRequiredEquipment?: string[];
  minimumExperience?: string; suitableGoals?: string[]; requiredEquipment?: string[];
  forceType?: string; mechanic?: string; difficulty?: string; descriptionEs?: string;
}

@Component({
  standalone: true, selector: 'app-exercise-manager',
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSnackBarModule],
  template: `
  <main class="page">
    <header class="hero">
      <div><p class="eyebrow">BIBLIOTECA DE ENTRENAMIENTO</p><h1>Gestor de ejercicios</h1>
        <p>Busca, filtra y administra ejercicios. Los cambios de tus ejercicios propios se guardan directamente; los del catálogo general requieren permisos de superadmin.</p></div>
      <div class="total"><strong>{{ rows.length }}</strong><span>resultados</span></div>
    </header>
    <mat-card class="filters">
      <h2>Filtros</h2>
      <div class="filter-grid">
        <mat-form-field appearance="outline"><mat-label>Nombre o palabra clave</mat-label><input matInput name="q" [(ngModel)]="filters.q" (keyup.enter)="search()"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Patrón / tipo de movimiento</mat-label><mat-select name="pattern" [(ngModel)]="filters.movementPattern"><mat-option value="">Todos</mat-option><mat-option *ngFor="let p of patterns" [value]="p">{{ label(p) }}</mat-option></mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Músculo principal</mat-label><mat-select name="primary" [(ngModel)]="filters.primaryMuscle"><mat-option value="">Todos</mat-option><mat-option *ngFor="let m of muscles" [value]="m">{{ label(m) }}</mat-option></mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Músculo sinergista</mat-label><mat-select name="secondary" [(ngModel)]="filters.secondaryMuscle"><mat-option value="">Todos</mat-option><mat-option *ngFor="let m of muscles" [value]="m">{{ label(m) }}</mat-option></mat-select></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Material</mat-label><input matInput name="equipment" [(ngModel)]="filters.equipment" placeholder="Barra, polea…"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Zona corporal</mat-label><input matInput name="bodyPart" [(ngModel)]="filters.bodyPart" placeholder="Espalda, piernas…"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Estado</mat-label><mat-select name="status" [(ngModel)]="filters.status"><mat-option value="">Todos</mat-option><mat-option value="approved">Aprobados</mat-option><mat-option value="pending">Pendientes</mat-option><mat-option value="rejected">Rechazados</mat-option></mat-select></mat-form-field>
      </div>
      <div class="actions"><button mat-flat-button color="primary" type="button" (click)="search()" [disabled]="loading">Buscar</button><button mat-stroked-button type="button" (click)="reset()">Limpiar filtros</button><span *ngIf="loading">Consultando catálogo…</span></div>
    </mat-card>
    <p class="error" *ngIf="error" role="alert">{{ error }}</p>
    <section class="results">
      <article class="exercise" *ngFor="let item of rows; trackBy: trackById">
        <div class="exercise-main">
          <div><h3>{{ item.nameEs || item.nameEn }}</h3><p>{{ item.nameEs ? item.nameEn : (item.descriptionEs || 'Sin descripción') }}</p></div>
          <span class="status" [class.pending]="item.reviewStatus === 'pending'">{{ statusLabel(item.reviewStatus) }}</span>
        </div>
        <div class="chips">
          <span>{{ label(patternOf(item)) }}</span><span *ngFor="let m of primaryOf(item)">{{ label(m) }}</span>
          <span *ngIf="item.equipment">{{ item.equipment }}</span><span *ngIf="item.bodyPart">{{ item.bodyPart }}</span>
        </div>
        <div class="exercise-foot"><small>{{ item.source === 'professional-contribution' ? 'Ejercicio profesional' : 'Catálogo general' }}<ng-container *ngIf="item.ownerUserId"> · ID profesional {{ item.ownerUserId }}</ng-container></small>
          <button mat-stroked-button type="button" *ngIf="item.source === 'professional-contribution' || isSuperAdmin" (click)="edit(item)">Editar</button>
        </div>
      </article>
      <p class="empty" *ngIf="!loading && rows.length === 0">No hay ejercicios que coincidan con estos filtros.</p>
    </section>
    <mat-card class="editor" *ngIf="draft">
      <div class="editor-heading"><div><p class="eyebrow">EDICIÓN</p><h2>{{ draft.id ? 'Editar ejercicio' : 'Añadir ejercicio' }}</h2></div><button mat-button type="button" (click)="draft = null">Cerrar</button></div>
      <form (ngSubmit)="save()">
        <mat-form-field appearance="outline"><mat-label>Nombre *</mat-label><input matInput name="name" [(ngModel)]="draft.name" maxlength="200" required></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Descripción</mat-label><textarea matInput name="description" [(ngModel)]="draft.description" rows="3"></textarea></mat-form-field>
        <div class="filter-grid">
          <mat-form-field appearance="outline"><mat-label>Material descriptivo</mat-label><input matInput name="equipment" [(ngModel)]="draft.equipment"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Zona corporal</mat-label><input matInput name="bodyPart" [(ngModel)]="draft.bodyPart"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Patrón *</mat-label><mat-select name="movementPattern" [(ngModel)]="draft.movementPattern" required><mat-option *ngFor="let p of patterns" [value]="p">{{ label(p) }}</mat-option></mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Experiencia mínima *</mat-label><mat-select name="experience" [(ngModel)]="draft.minimumExperience"><mat-option value="Beginner">Principiante</mat-option><mat-option value="Intermediate">Intermedio</mat-option><mat-option value="Advanced">Avanzado</mat-option></mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Músculos principales *</mat-label><input matInput name="primaryMuscles" [(ngModel)]="draft.primaryMuscles"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Sinergistas</mat-label><input matInput name="secondaryMuscles" [(ngModel)]="draft.secondaryMuscles"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Objetivos compatibles *</mat-label><mat-select name="goals" [(ngModel)]="draft.suitableGoals" multiple><mat-option value="GeneralFitness">Condición física general</mat-option><mat-option value="Hypertrophy">Hipertrofia</mat-option><mat-option value="Strength">Fuerza</mat-option></mat-select></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Material para el generador</mat-label><input matInput name="requiredEquipment" [(ngModel)]="draft.requiredEquipment"></mat-form-field>
        </div>
        <p class="hint">Los grupos musculares y el material del generador se introducen como códigos separados por comas (por ejemplo: UpperBack, Lats o cable, dumbbell).</p>
        <div class="actions"><button mat-flat-button color="primary" type="submit" [disabled]="saving || !draft.name.trim()"> {{ saving ? 'Guardando…' : 'Guardar cambios' }}</button><button mat-stroked-button type="button" (click)="draft = null">Cancelar</button></div>
      </form>
    </mat-card>
  </main>`,
  styles: [`
    :host{display:block;color:#17243a}.page{max-width:1250px;margin:auto;padding:28px;display:grid;gap:18px}
    .hero{display:flex;justify-content:space-between;align-items:center;gap:20px}.hero h1{margin:4px 0;font-size:2rem}.hero p{max-width:760px;color:#64748b}
    .eyebrow{font-size:.72rem;letter-spacing:.14em;font-weight:800;color:#5874a5;margin:0}.total{background:#edf3fb;border-radius:16px;padding:16px 24px;text-align:center;min-width:90px}.total strong{display:block;font-size:2rem}.total span{font-size:.8rem;color:#64748b}
    mat-card{padding:20px;border-radius:16px}.filters h2,.editor h2{margin:0 0 14px}.filter-grid{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:10px}.filter-grid mat-form-field{width:100%}
    .actions{display:flex;align-items:center;gap:10px;flex-wrap:wrap}.actions span,.hint{color:#64748b;font-size:.85rem}.results{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px}
    .exercise{background:white;border:1px solid #e3eaf2;border-radius:14px;padding:16px;box-shadow:0 3px 14px #102b4d0a}.exercise-main,.exercise-foot,.editor-heading{display:flex;align-items:center;justify-content:space-between;gap:12px}.exercise h3{margin:0;font-size:1.05rem}.exercise p{margin:5px 0;color:#64748b;font-size:.85rem}.status{font-size:.72rem;border-radius:20px;padding:5px 9px;background:#e6f5eb;color:#267246;white-space:nowrap}.status.pending{background:#fff4dc;color:#946200}
    .chips{display:flex;flex-wrap:wrap;gap:6px;margin:12px 0}.chips span{background:#f0f4f8;border-radius:20px;padding:5px 9px;font-size:.73rem;color:#41546b}.exercise-foot small{color:#8491a3}.editor form{display:grid;gap:8px}.error{color:#b42318}.empty{text-align:center;color:#64748b;padding:30px}
    @media(max-width:800px){.page{padding:14px}.filter-grid,.results{grid-template-columns:1fr}.hero{align-items:flex-start}.total{padding:10px}}
  `]
})
export class ExerciseManagerComponent implements OnInit {
  rows: ExerciseRow[] = []; loading = false; saving = false; error = ''; draft: any = null;
  readonly isSuperAdmin = true;
  filters = { q: '', movementPattern: '', primaryMuscle: '', secondaryMuscle: '', equipment: '', bodyPart: '', status: '' };
  patterns = ['Squat','Hinge','HorizontalPush','VerticalPush','HorizontalPull','VerticalPull','SingleLeg','Core'];
  muscles = ['Chest','Lats','UpperBack','Deltoids','Biceps','Triceps','Forearms','Abdominals','Obliques','ErectorSpinae','Glutes','Quadriceps','Hamstrings','Calves'];
  constructor(private http: HttpClient, private snack: MatSnackBar) {}
  ngOnInit(): void { this.search(); }
  trackById(_: number, item: ExerciseRow): string { return item.id; }
  label(value?: string): string { return value ? value.replace(/([a-z])([A-Z])/g, '$1 $2') : 'Sin clasificar'; }
  statusLabel(value: string): string { return value === 'approved' ? 'Aprobado' : value === 'rejected' ? 'Rechazado' : 'Pendiente'; }
  patternOf(item: ExerciseRow): string | undefined { return item.movementPattern || item.ownerMovementPattern; }
  primaryOf(item: ExerciseRow): string[] { return item.reviewedPrimaryMuscles || item.ownerPrimaryMuscles || item.primaryMuscles || []; }
  search(): void {
    this.loading = true; this.error = '';
    let params = new HttpParams().set('limit', '10000').set('all', 'true');
    Object.entries(this.filters).forEach(([key, value]) => { if (value.trim()) params = params.set(key, value.trim()); });
    this.http.get<ExerciseRow[]>('/api/exercise-catalog', { params }).subscribe({
      next: rows => { this.rows = rows || []; this.loading = false; },
      error: e => { this.loading = false; this.error = e?.error?.error || 'No se pudo cargar el catálogo de ejercicios.'; }
    });
  }
  reset(): void { this.filters = { q: '', movementPattern: '', primaryMuscle: '', secondaryMuscle: '', equipment: '', bodyPart: '', status: '' }; this.search(); }
  edit(item: ExerciseRow): void {
    const primary = this.primaryOf(item);
    const secondary = item.reviewedSecondaryMuscles || item.ownerSecondaryMuscles || item.secondaryMuscles || [];
    this.draft = { id: item.id, name: item.nameEs || item.nameEn, description: item.descriptionEs || '', equipment: item.equipment || '',
      bodyPart: item.bodyPart || '', movementPattern: this.patternOf(item) || 'HorizontalPull',
      minimumExperience: item.ownerMinimumExperience || item.minimumExperience || 'Beginner',
      primaryMuscles: primary.join(', '), secondaryMuscles: secondary.join(', '),
      suitableGoals: item.ownerSuitableGoals || item.suitableGoals || ['GeneralFitness','Hypertrophy','Strength'],
      requiredEquipment: (item.ownerRequiredEquipment || item.requiredEquipment || []).join(', ') };
  }
  save(): void {
    if (!this.draft || this.saving) return;
    const split = (value: string) => value.split(',').map(x => x.trim()).filter(Boolean);
    const payload = { name: this.draft.name.trim(), description: this.draft.description || null, equipment: this.draft.equipment || null,
      bodyPart: this.draft.bodyPart || null, movementPattern: this.draft.movementPattern, minimumExperience: this.draft.minimumExperience,
      primaryMuscles: split(this.draft.primaryMuscles), secondaryMuscles: split(this.draft.secondaryMuscles),
      suitableGoals: this.draft.suitableGoals || [], requiredEquipment: split(this.draft.requiredEquipment), selectionPriority: 50 };
    if (!payload.name || !payload.primaryMuscles.length || !payload.suitableGoals.length) { this.snack.open('Completa nombre, músculos principales y objetivos.', 'Cerrar', { duration: 3500 }); return; }
    this.saving = true;
    const request = this.draft.id
      ? this.http.put('/api/exercise-catalog/' + encodeURIComponent(this.draft.id) + '/manage', payload)
      : this.http.post('/api/exercise-catalog/contributions', payload);
    request.subscribe({
      next: () => { this.saving = false; this.draft = null; this.snack.open('Ejercicio guardado.', 'Cerrar', { duration: 2500 }); this.search(); },
      error: e => { this.saving = false; this.snack.open(e?.error?.error || 'No se pudo guardar el ejercicio.', 'Cerrar', { duration: 4000 }); }
    });
  }
}
