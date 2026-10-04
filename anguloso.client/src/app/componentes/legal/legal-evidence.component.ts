import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { MatCardModule } from '@angular/material/card';
import { LegalDocumentGeneratorService, LegalGeneratedDocument } from '../../servicios/legal-document-generator.service';
import { LegalEvidenceService, LegalAcceptanceEvidence, PrivacyRequestEvidence, PrivacyIncidentEvidence } from '../../servicios/legal-evidence.service';

@Component({
 selector:'app-legal-evidence',standalone:true,imports:[CommonModule,MatCardModule],
 template:`
 <section class="grid">
  <mat-card><mat-card-header><mat-card-title>Documentos generados</mat-card-title></mat-card-header><mat-card-content>
   <div class="row" *ngFor="let d of documents"><div><strong>{{d.title}}</strong><small>v{{d.version}} · {{d.status}}</small></div><span>{{d.sha256}}</span></div>
   <p *ngIf="!documents.length">No hay documentos generados en este ámbito.</p>
  </mat-card-content></mat-card>
  <mat-card><mat-card-header><mat-card-title>Aceptaciones</mat-card-title></mat-card-header><mat-card-content>
   <div class="row" *ngFor="let a of acceptances"><div><strong>{{a.key}}</strong><small>v{{a.version}} · {{a.context}}</small></div><span>{{a.acceptedAt|date:'medium'}}</span></div>
   <p *ngIf="!acceptances.length">No hay aceptaciones registradas para este usuario.</p>
  </mat-card-content></mat-card>
  <mat-card><mat-card-header><mat-card-title>Solicitudes de derechos</mat-card-title></mat-card-header><mat-card-content>
   <div class="row" *ngFor="let r of requests"><div><strong>{{r.rightType}}</strong><small>{{r.requesterType}} · paciente {{r.clientId||'—'}}</small></div><span>{{r.status}}</span></div>
   <p *ngIf="!requests.length">No hay solicitudes registradas.</p>
  </mat-card-content></mat-card>
  <mat-card><mat-card-header><mat-card-title>Incidentes de privacidad</mat-card-title></mat-card-header><mat-card-content>
   <div class="row" *ngFor="let i of incidents"><div><strong>#{{i.id}}</strong><small>{{i.detectedAt|date:'medium'}}</small></div><span>{{i.status}}</span></div>
   <p *ngIf="!incidents.length">No hay incidentes registrados.</p>
  </mat-card-content></mat-card>
 </section>`,
 styles:[`.grid{display:grid;gap:14px;padding:20px 0}.row{display:flex;justify-content:space-between;gap:16px;padding:11px 0;border-bottom:1px solid #e2e8f0}.row div{min-width:0}.row span{font-size:12px;word-break:break-all}.row small{display:block;color:#64748b;margin-top:3px}p{color:#64748b}@media(max-width:800px){.row{display:grid}}`]
})
export class LegalEvidenceComponent implements OnInit {
 documents:LegalGeneratedDocument[]=[];acceptances:LegalAcceptanceEvidence[]=[];requests:PrivacyRequestEvidence[]=[];incidents:PrivacyIncidentEvidence[]=[];
 constructor(private generator:LegalDocumentGeneratorService,private api:LegalEvidenceService){}
 ngOnInit(){this.generator.list().subscribe(x=>this.documents=x);this.api.acceptances().subscribe(x=>this.acceptances=x);this.api.requests().subscribe(x=>this.requests=x);this.api.incidents().subscribe(x=>this.incidents=x);}
}