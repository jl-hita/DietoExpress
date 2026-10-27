import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

interface ExerciseReviewStats {
  total: number; pending: number; approved: number; rejected: number;
  repDbTotal: number; repDbPending: number; repDbApproved: number; repDbRejected: number;
  repDbAutoApprovedV2: number; repDbAutoEnriched: number;
  pendingMissingSpanishName: number; pendingMissingDescription: number;
  pendingMissingDifficulty: number; pendingMissingInstructions: number;
  lastSourceUpdate?: string | null;
}
interface ExerciseReviewItem {
  id: string; nameEs?: string; nameEn: string; source: string; category?: string; bodyPart?: string; equipment?: string;
  reviewStatus: string; reviewed: boolean; movementPattern?: string; reviewedPrimaryMuscles?: string[];
  reviewedSecondaryMuscles?: string[]; minimumExperience?: string; suitableGoals?: string[]; requiredEquipment?: string[];
  selectionPriority: number; reviewNotes?: string; reviewedBy?: string; contributor?: string;
}
@Component({
  standalone: true, selector: 'app-admin-exercise-review',
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatIconModule, MatSnackBarModule],
  template: `
  <main class="review-page">
    <header><div><p class="eyebrow">CALIDAD DEL CATÁLOGO</p><h1>Revisión de ejercicios</h1><p>La importación no equivale a una validación humana. Aprueba solo las fichas que hayas comprobado.</p></div><button mat-stroked-button (click)="load()"><mat-icon>refresh</mat-icon> Actualizar</button></header>
    <section class="stats" *ngIf="stats as s">
      <div class="stat"><span>Catálogo completo</span><strong>{{ s.total }}</strong><small>{{ s.repDbTotal }} de RepDB</small></div>
      <div class="stat pending"><span>Pendientes</span><strong>{{ s.pending }}</strong><small>{{ s.repDbPending }} de RepDB</small></div>
      <div class="stat approved"><span>Aprobados</span><strong>{{ s.approved }}</strong><small>{{ s.repDbApproved }} de RepDB</small></div>
      <div class="stat rejected"><span>Rechazados</span><strong>{{ s.rejected }}</strong><small>{{ s.repDbRejected }} de RepDB</small></div>
      <div class="stat"><span>Autoaprobados v2</span><strong>{{ s.repDbAutoApprovedV2 }}</strong><small>Tras enriquecer metadatos</small></div>
    </section>
    <section class="missing-summary" *ngIf="stats as s">
      <strong>Campos que aún faltan en ejercicios RepDB pendientes</strong>
      <span>Nombre español: {{ s.pendingMissingSpanishName }}</span>
      <span>Descripción: {{ s.pendingMissingDescription }}</span>
      <span>Dificultad: {{ s.pendingMissingDifficulty }}</span>
      <span>Instrucciones: {{ s.pendingMissingInstructions }}</span>
      <small *ngIf="s.lastSourceUpdate">Última actualización de origen: {{ s.lastSourceUpdate | date:'dd/MM/yyyy HH:mm' }}</small>
    </section>
    <section class="toolbar"><mat-form-field appearance="outline"><mat-label>Buscar ejercicio</mat-label><input matInput [(ngModel)]="query" (keyup.enter)="load()" placeholder="Nombre o término"/></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Estado</mat-label><mat-select [(ngModel)]="status" (selectionChange)="load()"><mat-option value="pending">Pendientes</mat-option><mat-option value="approved">Aprobados</mat-option><mat-option value="rejected">Rechazados</mat-option></mat-select></mat-form-field><span>{{ items.length }} ejercicios</span></section>
    <p *ngIf="loading">Cargando catálogo…</p>
    <article class="exercise" *ngFor="let item of items"><div class="exercise-head"><div><h2>{{ item.nameEs || item.nameEn }}</h2><p>{{ item.nameEn }} · {{ item.source }} · {{ item.category || item.bodyPart || 'Sin categoría' }}</p></div><span class="status" [class.approved]="item.reviewStatus==='approved'" [class.rejected]="item.reviewStatus==='rejected'">{{ statusLabel(item.reviewStatus) }}</span></div>
      <div class="facts"><span>Material original: {{ item.equipment || 'No indicado' }}</span><span>Zona: {{ item.bodyPart || 'No indicada' }}</span><span *ngIf="item.contributor">Propuesto por: {{ item.contributor }}</span><span *ngIf="item.reviewedBy">Última revisión: {{ item.reviewedBy }}</span></div>
      <div class="editor"><mat-form-field appearance="outline"><mat-label>Patrón</mat-label><mat-select [(ngModel)]="drafts[item.id].pattern"><mat-option *ngFor="let p of patterns" [value]="p">{{ p }}</mat-option></mat-select></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Músculos principales (coma)</mat-label><input matInput [(ngModel)]="drafts[item.id].primary"/></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Músculos secundarios (coma)</mat-label><input matInput [(ngModel)]="drafts[item.id].secondary"/></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Nivel mínimo</mat-label><mat-select [(ngModel)]="drafts[item.id].experience"><mat-option value="Beginner">Principiante</mat-option><mat-option value="Intermediate">Intermedio</mat-option><mat-option value="Advanced">Avanzado</mat-option></mat-select></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Objetivos (coma)</mat-label><input matInput [(ngModel)]="drafts[item.id].goals"/></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Material requerido (coma)</mat-label><input matInput [(ngModel)]="drafts[item.id].equipment"/></mat-form-field>
      <mat-form-field appearance="outline"><mat-label>Prioridad (0–100)</mat-label><input matInput type="number" min="0" max="100" [(ngModel)]="drafts[item.id].priority"/></mat-form-field>
      <mat-form-field appearance="outline" class="notes"><mat-label>Notas / motivo de rechazo</mat-label><textarea matInput rows="2" [(ngModel)]="drafts[item.id].notes"></textarea></mat-form-field></div>
      <footer><button mat-stroked-button (click)="save(item,'pending')" [disabled]="saving===item.id">Guardar borrador</button><button mat-stroked-button color="warn" (click)="save(item,'rejected')" [disabled]="saving===item.id">Rechazar</button><button mat-flat-button color="primary" (click)="save(item,'approved')" [disabled]="saving===item.id">Aprobar</button></footer></article>
    <p class="empty" *ngIf="!loading && !items.length">No hay ejercicios en este estado con los filtros actuales.</p>
  </main>`,
  styles: [`.review-page{max-width:1200px;margin:auto;padding:24px;color:var(--text-color,#202124)}header,.exercise-head,footer,.toolbar{display:flex;align-items:center;justify-content:space-between;gap:16px;flex-wrap:wrap}h1{margin:.15rem 0}.eyebrow{font-size:.75rem;letter-spacing:.12em;font-weight:700;color:#5874a5}.stats{display:grid;grid-template-columns:repeat(auto-fit,minmax(145px,1fr));gap:12px;margin:22px 0 12px}.stat{display:flex;flex-direction:column;gap:5px;border:1px solid #dfe3e8;border-radius:12px;padding:14px;background:var(--surface-color,#fff)}.stat span{font-size:.85rem;color:#687080}.stat strong{font-size:1.7rem;line-height:1.2}.stat small,.missing-summary small{font-size:.78rem;color:#687080}.stat.pending strong{color:#986500}.stat.approved strong{color:#167447}.stat.rejected strong{color:#b42318}.missing-summary{display:flex;gap:12px 22px;align-items:center;flex-wrap:wrap;border-radius:10px;background:#f5f7fa;padding:12px 16px;font-size:.9rem}.missing-summary strong{width:100%}.toolbar{justify-content:flex-start;margin:20px 0}.toolbar mat-form-field{min-width:220px}.exercise{border:1px solid #dfe3e8;border-radius:14px;padding:20px;margin:14px 0;background:var(--surface-color,#fff)}h2{margin:0}.exercise-head p{margin:.4rem 0;color:#687080}.status{border-radius:20px;background:#fff0cf;padding:6px 12px;font-size:.85rem}.status.approved{background:#d9f5e4}.status.rejected{background:#ffe0e0}.facts{display:flex;gap:20px;flex-wrap:wrap;color:#687080;font-size:.9rem}.editor{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:10px;margin-top:18px}.editor mat-form-field{width:100%}.notes{grid-column:1/-1}footer{justify-content:flex-end;border-top:1px solid #eee;padding-top:14px}.empty{padding:36px;text-align:center;color:#687080}@media(max-width:650px){.review-page{padding:12px}.exercise{padding:14px}footer button{flex:1}}`]
})
export class AdminExerciseReviewComponent implements OnInit {
  items: ExerciseReviewItem[] = []; stats: ExerciseReviewStats | null = null; drafts: Record<string, any> = {}; query = ''; status = 'pending'; loading = false; saving = '';
  patterns = ['Squat','Hinge','HorizontalPush','VerticalPush','HorizontalPull','VerticalPull','SingleLeg','Core','Carry','Rotation','Mobility','Cardio'];
  constructor(private http: HttpClient, private snack: MatSnackBar, private changeDetector: ChangeDetectorRef) {}
  ngOnInit(): void { this.load(); }
  load(): void {
    this.loading = true;
    this.http.get<ExerciseReviewStats>('/api/exercise-catalog/review-stats').subscribe({
      next: stats => { this.stats = stats; this.changeDetector.detectChanges(); },
      error: () => { this.stats = null; this.changeDetector.detectChanges(); }
    });
    this.http.get<ExerciseReviewItem[]>('/api/exercise-catalog/review-queue', { params: { q: this.query, status: this.status, limit: '100' } }).subscribe({
      next: items => { this.items = items; this.drafts = {}; for (const item of items) this.drafts[item.id] = {
        pattern: item.movementPattern || '', primary: (item.reviewedPrimaryMuscles || []).join(', '), secondary: (item.reviewedSecondaryMuscles || []).join(', '),
        experience: item.minimumExperience || 'Beginner', goals: (item.suitableGoals || ['GeneralFitness','Hypertrophy','Strength']).join(', '),
        equipment: (item.requiredEquipment || []).join(', '), priority: item.selectionPriority ?? 50, notes: item.reviewNotes || ''
      }; this.loading = false; this.changeDetector.detectChanges(); },
      error: () => { this.loading = false; this.changeDetector.detectChanges(); this.snack.open('No se pudo cargar la cola de revisión.', 'Cerrar', {duration: 4000}); }
    });
  }
  statusLabel(value: string): string { return value === 'approved' ? 'Aprobado por persona' : value === 'rejected' ? 'Rechazado' : 'Pendiente de revisión'; }
  save(item: ExerciseReviewItem, status: string): void {
    const d = this.drafts[item.id]; this.saving = item.id; const split = (value: string) => value.split(',').map(x => x.trim()).filter(Boolean);
    this.http.put('/api/exercise-catalog/' + encodeURIComponent(item.id) + '/review', {
      status, movementPattern: d.pattern || null, primaryMuscles: split(d.primary), secondaryMuscles: split(d.secondary),
      minimumExperience: d.experience, suitableGoals: split(d.goals), requiredEquipment: split(d.equipment),
      selectionPriority: Number(d.priority), notes: d.notes || null
    }).subscribe({
      next: () => { this.saving = ''; this.snack.open(status === 'approved' ? 'Ejercicio aprobado.' : status === 'rejected' ? 'Ejercicio rechazado.' : 'Borrador guardado.', 'Cerrar', {duration: 2500}); this.load(); },
      error: e => { this.saving = ''; this.snack.open(e?.error?.error || 'No se pudo guardar la revisión.', 'Cerrar', {duration: 4000}); }
    });
  }
}
