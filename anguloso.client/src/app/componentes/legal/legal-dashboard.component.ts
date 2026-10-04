import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTabsModule } from '@angular/material/tabs';
import { LegalSettingsComponent } from '../legal-settings/legal-settings.component';
import { LegalDocumentGeneratorService, LegalGeneratedDocument } from '../../servicios/legal-document-generator.service';
import { LegalGovernanceComponent } from './legal-governance.component';
import { LegalEvidenceComponent } from './legal-evidence.component';
import { LegalGovernanceService } from '../../servicios/legal-governance.service';
import { LegalEvidenceService } from '../../servicios/legal-evidence.service';

@Component({
  selector: 'app-legal-dashboard',
  standalone: true,
  imports: [CommonModule, MatButtonModule, MatCardModule, MatIconModule, MatTabsModule, LegalSettingsComponent, LegalGovernanceComponent, LegalEvidenceComponent],
  template: `
    <main class="page">
      <header>
        <div><h1>Legal y cumplimiento</h1><p>Un único espacio para preparar, revisar y mantener la documentación legal de tu cuenta.</p></div>
      </header>

      <section class="status" [class.ok]="pending===0 && documents.length>0">
        <mat-icon>{{ pending===0 && documents.length>0 ? 'check_circle' : 'pending_actions' }}</mat-icon>
        <div>
          <strong>{{ pending===0 && documents.length>0 ? 'No hay placeholders pendientes en los documentos generados' : 'Hay documentación pendiente' }}</strong>
          <span>{{ pending }} documento(s) requieren completar o revisar información.</span>
        </div>
      </section>

      <mat-tab-group animationDuration="0ms">
        <mat-tab label="Resumen">
          <section class="cards">
            <mat-card><mat-card-header><mat-icon mat-card-avatar>description</mat-icon><mat-card-title>Documentos</mat-card-title></mat-card-header>
              <mat-card-content><strong>{{documents.length}}</strong> documentos generados</mat-card-content>
            </mat-card>
            <mat-card><mat-card-header><mat-icon mat-card-avatar>fact_check</mat-icon><mat-card-title>Información pendiente</mat-card-title></mat-card-header>
              <mat-card-content><strong>{{pending}}</strong> documentos con variables sin resolver</mat-card-content>
            </mat-card>
            <mat-card><mat-card-header><mat-icon mat-card-avatar>verified_user</mat-icon><mat-card-title>Publicación</mat-card-title></mat-card-header>
              <mat-card-content>Los borradores se generan sin publicar automáticamente.</mat-card-content>
            </mat-card>
            <mat-card><mat-card-header><mat-icon mat-card-avatar>account_tree</mat-icon><mat-card-title>RAT</mat-card-title></mat-card-header><mat-card-content><strong>{{ratCount}}</strong> actividades registradas</mat-card-content></mat-card>
            <mat-card><mat-card-header><mat-icon mat-card-avatar>warning</mat-icon><mat-card-title>Riesgos abiertos</mat-card-title></mat-card-header><mat-card-content><strong>{{openRisks}}</strong> requieren seguimiento</mat-card-content></mat-card>
            <mat-card><mat-card-header><mat-icon mat-card-avatar>privacy_tip</mat-icon><mat-card-title>Privacidad</mat-card-title></mat-card-header><mat-card-content><strong>{{openRequests}}</strong> solicitudes · <strong>{{openIncidents}}</strong> incidentes activos</mat-card-content></mat-card>
            <mat-card><mat-card-header><mat-icon mat-card-avatar>policy</mat-icon><mat-card-title>EIPD</mat-card-title></mat-card-header><mat-card-content>Decisión: <strong>{{eipdDecision}}</strong></mat-card-content></mat-card>
          </section>
          <mat-card *ngIf="pendingDocs.length">
            <mat-card-header><mat-icon mat-card-avatar>warning</mat-icon><mat-card-title>Qué falta</mat-card-title></mat-card-header>
            <mat-card-content class="pending-list">
              <div *ngFor="let doc of pendingDocs"><strong>{{doc.title}}</strong><span>{{doc.unresolved?.join(', ')}}</span></div>
            </mat-card-content>
          </mat-card>
          <mat-card class="scope-note"><mat-icon>info</mat-icon><span>Esta pantalla trabaja con la configuración y los documentos del ámbito de tu cuenta. La publicación oficial de documentos de plataforma está reservada al SuperAdmin.</span></mat-card>
        </mat-tab>

        <mat-tab label="Configuración">
          <app-legal-settings></app-legal-settings>
        </mat-tab>

        <mat-tab label="Documentos">
          <section class="documents">
            <div class="toolbar"><div><h2>Documentos generados</h2><p>Genera borradores con la configuración actual y revisa qué información queda pendiente.</p></div>
              <button mat-stroked-button *ngFor="let template of templates" (click)="generate(template.key)" [disabled]="generating">{{template.key}}</button>
            </div>
            <div class="doc-row" *ngFor="let doc of documents">
              <div><strong>{{doc.title}}</strong><small>v{{doc.version}} · {{doc.status}}</small></div>
              <span *ngIf="doc.hasUnresolvedPlaceholders" class="pending">Pendientes: {{doc.unresolved?.join(', ')}}</span>
              <span *ngIf="!doc.hasUnresolvedPlaceholders" class="ready">Sin placeholders</span>
            </div>
            <p *ngIf="!documents.length" class="empty">Todavía no hay documentos generados en este ámbito.</p>
          </section>
        </mat-tab>

        <mat-tab label="RAT y riesgos"><app-legal-governance></app-legal-governance>
          <!--
          <section class="placeholder-section">
            <mat-card><mat-card-header><mat-icon mat-card-avatar>account_tree</mat-icon><mat-card-title>Registro de Actividades de Tratamiento</mat-card-title></mat-card-header>
              <mat-card-content>La estructura de documentación RAT ya está contemplada en el gate. En este siguiente nivel se incorporará su edición estructurada por actividad.</mat-card-content>
            </mat-card>
            <mat-card><mat-card-header><mat-icon mat-card-avatar>security</mat-icon><mat-card-title>Riesgos y EIPD</mat-card-title></mat-card-header>
              <mat-card-content>La evaluación de riesgos y la decisión sobre EIPD deben reflejar la situación real y no se completan automáticamente con valores genéricos.</mat-card-content>
            </mat-card>
          </section> -->
        </mat-tab>

        <mat-tab label="Evidencias"><app-legal-evidence></app-legal-evidence></mat-tab>
      </mat-tab-group>
    </main>`,
  styles: [`
    .page{padding:24px;display:grid;gap:18px;max-width:1200px}.page h1{margin:0}.page header p{color:#64748b}
    .status{display:flex;gap:14px;align-items:center;padding:18px;border-radius:12px;background:#fff7ed;color:#9a3412}.status.ok{background:#f0fdf4;color:#166534}.status mat-icon{font-size:32px;width:32px;height:32px}.status span{display:block;margin-top:3px}
    .cards{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:14px;padding:20px 0}.cards strong{font-size:28px}.pending-list{display:grid;gap:10px}.pending-list div{display:flex;justify-content:space-between;gap:16px;padding:10px;border-bottom:1px solid #e2e8f0}.pending{color:#b45309}.ready{color:#15803d}.scope-note{display:flex;gap:10px;margin-top:16px}.documents,.placeholder-section{display:grid;gap:14px;padding:20px 0}.toolbar{display:flex;align-items:flex-end;gap:10px;flex-wrap:wrap}.toolbar>div{flex:1 1 100%}.toolbar h2{margin:0 0 4px}.toolbar p{color:#64748b}.doc-row{display:flex;justify-content:space-between;gap:12px;padding:12px;border:1px solid #e2e8f0;border-radius:8px}.doc-row small{display:block;color:#64748b}.empty{color:#64748b}@media(max-width:800px){.cards{grid-template-columns:1fr}.pending-list div,.doc-row{display:grid}}
  `]
})
export class LegalDashboardComponent implements OnInit {
  documents: LegalGeneratedDocument[] = [];
  ratCount=0; openRisks=0; openRequests=0; openIncidents=0; eipdDecision='pending';
  pendingDocs: LegalGeneratedDocument[] = [];
  pending = 0;
  templates: {key:string; file:string}[] = [];
  generating = false;

  constructor(private generator: LegalDocumentGeneratorService, private governance: LegalGovernanceService, private evidence: LegalEvidenceService) {}

  ngOnInit(): void {
    this.loadDocuments();
    this.generator.getTemplates().subscribe({next: templates => this.templates=templates});
    this.governance.listRat().subscribe({next:x=>this.ratCount=x.filter(a=>a.status!=='archived').length});
    this.governance.listRisks().subscribe({next:x=>this.openRisks=x.filter(a=>a.status==='open').length});
    this.governance.listEipd().subscribe({next:x=>this.eipdDecision=x.length ? x[0].decision : 'pending'});
    this.evidence.requests().subscribe({next:x=>this.openRequests=x.filter(a=>!['resolved','rejected','cancelled'].includes(a.status)).length});
    this.evidence.incidents().subscribe({next:x=>this.openIncidents=x.filter(a=>!['closed','false_positive'].includes(a.status)).length});
  }

  loadDocuments(): void {
    this.generator.list().subscribe({next: docs => {
      this.documents=docs;
      this.pendingDocs=docs.filter(d => !!d.hasUnresolvedPlaceholders || !!d.unresolved?.length);
      this.pending=this.pendingDocs.length;
    }});
  }

  generate(templateKey:string): void {
    if(this.generating)return;
    this.generating=true;
    this.generator.generate(templateKey).subscribe({
      next:()=>{this.generating=false;this.loadDocuments();},
      error:()=>{this.generating=false;}
    });
  }
}