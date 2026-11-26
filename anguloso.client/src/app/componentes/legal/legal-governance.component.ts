import { CommonModule } from '@angular/common';
import { Component, OnInit, ChangeDetectionStrategy, signal} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { apiErrorMessage } from '../../shared/api-error';
import { LegalGovernanceService, LegalRatActivity, LegalRiskAssessment, LegalEipdDecision } from '../../servicios/legal-governance.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,selector:'app-legal-governance',standalone:true,imports:[CommonModule,FormsModule,MatButtonModule,MatCardModule,MatFormFieldModule,MatInputModule],template:`
<section class="grid">
 <p class="error" role="alert" *ngIf="error()">{{ error() }}</p>
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
</section>`,styles:[`.error{color:#b91c1c;margin:0}.grid{display:grid;gap:16px;padding:20px 0}.form{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;margin-top:14px}.row{display:flex;justify-content:space-between;gap:12px;padding:10px;border-bottom:1px solid #e2e8f0}.row span{color:#64748b}@media(max-width:800px){.form{grid-template-columns:1fr}.row{display:grid}}`]})
export class LegalGovernanceComponent implements OnInit {
 private readonly ratState = signal<LegalRatActivity[]>([]);
 get rat(): LegalRatActivity[] { return this.ratState(); }
 set rat(value: LegalRatActivity[]) { this.ratState.set(value); }

 private readonly risksState = signal<LegalRiskAssessment[]>([]);
 get risks(): LegalRiskAssessment[] { return this.risksState(); }
 set risks(value: LegalRiskAssessment[]) { this.risksState.set(value); }

 private readonly eipdState = signal<LegalEipdDecision[]>([]);
 get eipd(): LegalEipdDecision[] { return this.eipdState(); }
 set eipd(value: LegalEipdDecision[]) { this.eipdState.set(value); }
 newRat:any={name:'',purpose:'',role:'controller',legalBasis:'',dataCategories:'',retention:'',status:'draft'};
 newRisk:any={name:'',riskDescription:'',likelihood:1,impact:1,measures:'',status:'open'};
 newEipd:any={decision:'pending',justification:'',additionalMeasures:'',documentReference:''};
 readonly error=signal('');
 private fail(fallback:string){return {error:(e:unknown)=>this.error.set(apiErrorMessage(e,fallback))};}
 constructor(private api:LegalGovernanceService){}
 ngOnInit(){this.reload();}
 reload(){
  const failed=this.fail('No se ha podido cargar el gobierno legal.');
  this.api.listRat().subscribe({next:x=>this.rat=x,...failed});this.api.listRisks().subscribe({next:x=>this.risks=x,...failed});this.api.listEipd().subscribe({next:x=>this.eipd=x,...failed});
 }
 addRat(){if(!this.newRat.name||!this.newRat.purpose)return;this.error.set('');this.api.createRat(this.newRat).subscribe({next:()=>{this.newRat={name:'',purpose:'',role:'controller',legalBasis:'',dataCategories:'',retention:'',status:'draft'};this.reload();},...this.fail('No se ha podido añadir la actividad.')});}
 addRisk(){if(!this.newRisk.name||!this.newRisk.riskDescription)return;this.error.set('');this.api.createRisk(this.newRisk).subscribe({next:()=>{this.newRisk={name:'',riskDescription:'',likelihood:1,impact:1,measures:'',status:'open'};this.reload();},...this.fail('No se ha podido añadir el riesgo. Revisa que probabilidad e impacto estén entre 1 y 5.')});}
 addEipd(){if(!this.newEipd.justification)return;this.error.set('');this.api.createEipd(this.newEipd).subscribe({next:()=>{this.newEipd={decision:'pending',justification:'',additionalMeasures:'',documentReference:''};this.reload();},...this.fail('No se ha podido registrar la decisión. Debe ser required, not_required o pending.')});}
}