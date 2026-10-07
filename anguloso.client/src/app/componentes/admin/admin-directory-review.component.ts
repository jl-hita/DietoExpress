import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';

@Component({
  selector: 'app-admin-directory-review',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule],
  template: `
    <mat-card>
      <h1>Revisión documental del directorio</h1>
      <p *ngFor="let item of items">
        <strong>{{ item.fullName || item.username }}</strong> — {{ item.evidenceType }} — {{ item.originalFileName }}
        <button mat-button (click)="open(item.id)">Abrir</button>
        <button mat-button (click)="review(item.id, 'approved')">Aprobar</button>
        <button mat-button (click)="review(item.id, 'rejected')">Rechazar</button>
      </p>
      <p *ngIf="!items.length">No hay documentos pendientes.</p>
    </mat-card>
  `
})
export class AdminDirectoryReviewComponent implements OnInit {
  items: any[] = [];
  constructor(private http: HttpClient) {}
  ngOnInit(): void { this.load(); }
  load(): void { this.http.get<any[]>('/api/directory-verification/evidence/admin?status=pending').subscribe(x => this.items = x); }
  open(id: number): void { this.http.get('/api/directory-verification/evidence/' + id, { responseType: 'blob' }).subscribe(b => window.open(URL.createObjectURL(b), '_blank')); }
  review(id: number, status: string): void { this.http.put('/api/directory-verification/evidence/admin/' + id, { status }).subscribe(() => this.load()); }
}