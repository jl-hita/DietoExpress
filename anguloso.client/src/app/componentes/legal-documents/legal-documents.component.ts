import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { LegalDocument, LegalService } from '../../servicios/legal.service';

@Component({
  selector: 'app-legal-documents',
  standalone: true,
  imports: [CommonModule, MatCardModule, MatIconModule],
  template: `
    <main class="legal-page">
      <header>
        <span class="eyebrow">Información legal</span>
        <h1>Documentos legales de DietoExpress</h1>
        <p>Consulta siempre la versión publicada de cada documento.</p>
      </header>

      <section *ngIf="loading" class="state">Cargando documentación…</section>
      <section *ngIf="!loading && !documents.length" class="state">
        Los documentos legales todavía no están publicados.
      </section>

      <section *ngFor="let document of documents" class="document-card">
        <div class="document-heading">
          <div>
            <h2>{{ document.title }}</h2>
            <span>Versión {{ document.version }} · {{ document.publishedAt | date:'dd/MM/yyyy' }}</span>
          </div>
          <mat-icon>verified</mat-icon>
        </div>
        <article class="document-content">{{ document.content }}</article>
        <small>Identificador de versión: {{ document.sha256 }}</small>
      </section>
    </main>
  `,
  styles: [`
    .legal-page { max-width: 960px; margin: 0 auto; padding: 48px 24px 64px; color: #0f172a; }
    .eyebrow { font-size: 12px; font-weight: 700; letter-spacing: 1.2px; text-transform: uppercase; color: #0f766e; }
    h1 { margin: 8px 0; font-size: 32px; }
    header p { color: #64748b; }
    .document-card { margin-top: 24px; padding: 24px; border: 1px solid #e2e8f0; border-radius: 14px; background: white; }
    .document-heading { display: flex; justify-content: space-between; gap: 16px; align-items: flex-start; }
    .document-heading h2 { margin: 0 0 5px; font-size: 22px; }
    .document-heading span, small { color: #64748b; font-size: 12px; }
    .document-heading mat-icon { color: #0f766e; }
    .document-content { margin: 22px 0; white-space: pre-wrap; line-height: 1.65; font-size: 14px; }
    .state { margin-top: 28px; padding: 24px; border-radius: 12px; background: #f8fafc; color: #64748b; }
    @media (max-width: 700px) { .legal-page { padding: 32px 16px 48px; } }
  `]
})
export class LegalDocumentsComponent implements OnInit {
  documents: LegalDocument[] = [];
  loading = true;

  constructor(private legalService: LegalService) {}

  ngOnInit(): void {
    this.legalService.getCurrent().subscribe({
      next: documents => { this.documents = documents; this.loading = false; },
      error: () => { this.loading = false; }
    });
  }
}
