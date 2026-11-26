import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit, ChangeDetectionStrategy, signal} from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { apiErrorMessage } from '../../shared/api-error';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-admin-directory-review',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule],
  template: `
    <mat-card>
      <h1>Revisión documental del directorio</h1>
      <p role="alert" *ngIf="error()">{{ error() }}</p>
      <p *ngFor="let item of items">
        <strong>{{ item.fullName || item.username }}</strong> — {{ item.evidenceType }} — {{ item.originalFileName }}
        <button mat-button (click)="open(item.id)">Abrir</button>
        <button mat-button (click)="review(item.id, 'approved')">Aprobar</button>
        <button mat-button (click)="review(item.id, 'rejected')">Rechazar</button>
      </p>
      <p *ngIf="!items.length && !error()">No hay documentos pendientes.</p>
    </mat-card>
  `
})
export class AdminDirectoryReviewComponent implements OnInit {
  private readonly itemsSignal = signal<any[]>([]);
  get items(): any[] { return this.itemsSignal(); }
  set items(value: any[]) { this.itemsSignal.set(value); }
  readonly error = signal('');
  constructor(private http: HttpClient) {}
  ngOnInit(): void { this.load(); }
  load(): void {
    this.http.get<any[]>('/api/directory-verification/evidence/admin?status=pending').subscribe({
      next: x => { this.items = x; this.error.set(''); },
      error: e => this.error.set(apiErrorMessage(e, 'No se han podido cargar los documentos.'))
    });
  }

  open(id: number): void {
    // La pestaña se abre durante el clic; abrirla al llegar la respuesta la bloquea el navegador.
    const tab = window.open('', '_blank');
    this.http.get('/api/directory-verification/evidence/' + id, { responseType: 'blob' }).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        if (tab) tab.location.href = url; else window.location.assign(url);
      },
      error: e => { tab?.close(); this.error.set(apiErrorMessage(e, 'No se ha podido abrir el documento.')); }
    });
  }

  review(id: number, status: string): void {
    this.http.put('/api/directory-verification/evidence/admin/' + id, { status }).subscribe({
      next: () => this.load(),
      error: e => this.error.set(apiErrorMessage(e, 'No se ha podido registrar la revisión.'))
    });
  }
}