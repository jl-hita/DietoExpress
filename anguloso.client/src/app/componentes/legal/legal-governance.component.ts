import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { LegalGovernanceService, LegalRatActivity, LegalRiskAssessment, LegalEipdDecision } from '../../servicios/legal-governance.service';

@Component({selector:'app-legal-governance',standalone:true,imports:[CommonModule,FormsModule,MatButtonModule,MatCardModule,MatFormFieldModule,MatInputModule],template:`
<section class="grid">
 <mat-card><mat-card-header><mat-card-title>Registro de Actividades de Tratamiento</mat-card-title></mat-card-header><mat-card-content>
  <div class="row" *ngFor="let a of rat"><strong>{{a.name}}</strong><span>{{a.role}} · {{a.status}}</span></div>
  <div class="form">
   <mat-form-field><mat-label>Actividad</mat-label><input matInput [(ngModel)]="newRat.name"></mat-form-field>
   <mat-form-field><mat-label>Finalidad</mat-label><input matInput [(ngModel)]="newRat.purpose"></mat-form-field>
   <mat-form-field><mat-label>Base jurídica</mat-label><input matInput [(ngModel)]="newRat.legalBasis"></mat-form-field>
   <mat-form-field><mat-label>Categorías de datos</mat-label><input matInput [(ngModel)]="newRat.dataCategories"></mat-form-field>
   <mat-form-field><mat-label>Conservación</mat-label><input matInput [(ngModel)]="newRat.retention"></mat-form-field>
   <button mat-flat-button (click)="addRat()">Añadir actividad</button>
  </div>
 </mat-card-content></mat-card>
 <mat-card><mat-card-header><mat-card-title>Evaluación de riesgos</mat-card-title></mat-card-header><mat-card-content>
  <div class="row" *ngFor="let r of risks"><strong>{{r.name}}</strong><span>Impacto {{r.impact}} · Probabilidad {{r.likelihood}} · {{r.status}}</span></div>
  <div class="form">
   <mat-form-field><mat-label>Riesgo</mat-label><input matInput [(ngModel)]="newRisk.name"></mat-form-field>
   <mat-form-field><mat-label>Descripción</mat-label><input matInput [(ngModel)]="newRisk.riskDescription"></mat-form-field>
   <mat-form-field><mat-label>Probabilidad (1-5)</mat-label><input type="number" matInput [(ngModel)]="newRisk.likelihood"></mat-form-field>
   <mat-form-field><mat-label>Impacto (1-5)</mat-label><input type="number" matInput [(ngModel)]="newRisk.impact"></mat-form-field>
   <mat-form-field><mat-label>Medidas</mat-label><input matInput [(ngModel)]="newRisk.measures"></mat-form-field>
   <button mat-flat-button (click)="addRisk()">Añadir riesgo</button>
  </div>
 </mat-card-content></mat-card>
 <mat-card><mat-card-header><mat-card-title>Decisión EIPD</mat-card-title></mat-card-header><mat-card-content>
  <div *ngFor="let e of eipd" class="row"><strong>{{e.decision}}</strong><span>{{e.decidedAt|date:'shortDate'}} · {{e.justification}}</span></div>
  <div class="form"><mat-form-field><mat-label>Decisión</mat-label><input matInput [(ngModel)]="newEipd.decision" placeholder="required / not_required / pending"></mat-form-field>
   <mat-form-field><mat-label>Justificación</mat-label><textarea matInput [(ngModel)]="newEipd.justification"></textarea></mat-form-field>
   <button mat-flat-button (click)="addEipd()">Registrar decisión</button>
  </div>
 </mat-card-content></mat-card>
</section>`,styles:[`.grid{display:grid;gap:16px;padding:20px 0}.form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin-top:14px}.row{display:flex;justify-content:space-between;gap:12px;padding:10px;border-bottom:1px solid #e2e8f0}.row span{color:#64748b}@media(max-width:800px){.form{grid-template-columns:1fr}.row{display:grid}}`]})
export class LegalGovernanceComponent implements OnInit {
 rat:LegalRatActivity[]=[];risks:LegalRiskAssessment[]=[];eipd:LegalEipdDecision[]=[];
 newRat:any={name:'',purpose:'',role:'controller',legalBasis:'',dataCategories:'',retention:'',status:'draft'};
 newRisk:any={name:'',riskDescription:'',likelihood:1,impact:1,measures:'',status:'open'};
 newEipd:any={decision:'pending',justification:'',additionalMeasures:'',documentReference:''};
 constructor(private api:LegalGovernanceService){}
 ngOnInit(){this.reload();}
 reload(){this.api.listRat().subscribe(x=>this.rat=x);this.api.listRisks().subscribe(x=>this.risks=x);this.api.listEipd().subscribe(x=>this.eipd=x);}
 addRat(){if(!this.newRat.name||!this.newRat.purpose)return;this.api.createRat(this.newRat).subscribe(()=>{this.newRat={name:'',purpose:'',role:'controller',legalBasis:'',dataCategories:'',retention:'',status:'draft'};this.reload();});}
 addRisk(){if(!this.newRisk.name||!this.newRisk.riskDescription)return;this.api.createRisk(this.newRisk).subscribe(()=>{this.newRisk={name:'',riskDescription:'',likelihood:1,impact:1,measures:'',status:'open'};this.reload();});}
 addEipd(){if(!this.newEipd.justification)return;this.api.createEipd(this.newEipd).subscribe(()=>{this.newEipd={decision:'pending',justification:'',additionalMeasures:'',documentReference:''};this.reload();});}
}