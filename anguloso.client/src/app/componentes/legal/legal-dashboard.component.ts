import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { LegalDocumentGeneratorService, LegalGeneratedDocument } from '../../servicios/legal-document-generator.service';

@Component({
  selector: 'app-legal-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, MatButtonModule, MatCardModule, MatIconModule],
  template: `
    <main class="page">
      <header><div><h1>Legal y cumplimiento</h1><p>Gestiona la documentación legal, su revisión y las evidencias de DietoExpress.</p></div></header>

      <section class="status" [class.ok]="pending===0 && documents.length>0">
        <mat-icon>{{ pending===0 && documents.length>0 ? 'check_circle' : 'pending_actions' }}</mat-icon>
        <div><strong>{{ pending===0 && documents.length>0 ? 'Documentación sin placeholders pendientes' : 'Hay documentación pendiente' }}</strong>
        <span>{{ pending }} documento(s) requieren completar o revisar información.</span></div>
      </section>

      <section class="cards">
        <mat-card><mat-card-header><mat-icon mat-card-avatar>description</mat-icon><mat-card-title>Documentos</mat-card-title></mat-card-header>
          <mat-card-content><strong>{{documents.length}}</strong> documentos generados</mat-card-content>
          <mat-card-actions><a mat-button routerLink="/admin/legal">Gestionar documentos</a></mat-card-actions>
        </mat-card>
        <mat-card><mat-card-header><mat-icon mat-card-avatar>fact_check</mat-icon><mat-card-title>Configuración legal</mat-card-title></mat-card-header>
          <mat-card-content>Identidad, privacidad, proveedores, contratación y conservación.</mat-card-content>
          <mat-card-actions><a mat-button routerLink="/settings">Abrir Ajustes</a></mat-card-actions>
        </mat-card>
        <mat-card><mat-card-header><mat-icon mat-card-avatar>verified_user</mat-icon><mat-card-title>Revisión</mat-card-title></mat-card-header>
          <mat-card-content>Los documentos no se publican automáticamente. La publicación exige validación explícita.</mat-card-content>
        </mat-card>
      </section>

      <mat-card *ngIf="pendingDocs.length">
        <mat-card-header><mat-icon mat-card-avatar>warning</mat-icon><mat-card-title>Documentos pendientes</mat-card-title></mat-card-header>
        <mat-card-content class="pending-list">
          <div *ngFor="let doc of pendingDocs"><strong>{{doc.title}}</strong><span>{{doc.unresolved?.join(', ')}}</span></div>
        </mat-card-content>
      </mat-card>
    </main>`,
  styles: [`
    .page{padding:24px;display:grid;gap:18px;max-width:1100px}.page h1{margin:0}.page header p{color:#64748b}
    .status{display:flex;gap:14px;align-items:center;padding:18px;border-radius:12px;background:#fff7ed;color:#9a3412}.status.ok{background:#f0fdf4;color:#166534}.status mat-icon{font-size:32px;width:32px;height:32px}
    .cards{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px}.cards strong{font-size:28px}.pending-list{display:grid;gap:10px}.pending-list div{display:flex;justify-content:space-between;gap:16px;padding:10px;border-bottom:1px solid #e2e8f0}.pending-list span{color:#b45309}@media(max-width:800px){.cards{grid-template-columns:1fr}.pending-list div{display:grid}}
  `]
})
export class LegalDashboardComponent implements OnInit {
  documents: LegalGeneratedDocument[] = [];
  pendingDocs: LegalGeneratedDocument[] = [];
  pending = 0;
  constructor(private generator: LegalDocumentGeneratorService) {}
  ngOnInit(): void {
    this.generator.list().subscribe({next: docs => { this.documents=docs; this.pendingDocs=docs.filter(d => !!d.hasUnresolvedPlaceholders || !!d.unresolved?.length); this.pending=this.pendingDocs.length; }});
  }
}