import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { LegalConfigurationService } from '../../servicios/legal-configuration.service';
import { LegalReadiness, LegalService } from '../../servicios/legal.service';
import { LegalDocumentGeneratorService, LegalGeneratedDocument } from '../../servicios/legal-document-generator.service';

@Component({
  selector: 'app-admin-legal-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, MatButtonModule, MatCardModule, MatDividerModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSnackBarModule],
  template: `
    <main class="page" [formGroup]="form">
      <header><div><h1>Configuración legal de DietoExpress</h1><p>Datos del titular y de la plataforma que se insertarán en las plantillas legales.</p></div></header>

      <div class="notice"><mat-icon>warning</mat-icon><span>Completar estos campos no equivale a una revisión jurídica. Los documentos publicados deben estar sin placeholders y revisados antes de activarse.</span></div>
      <mat-card class="readiness" *ngIf="readiness">
        <mat-card-header><mat-icon mat-card-avatar>{{ readiness.ready ? 'verified' : 'pending_actions' }}</mat-icon><mat-card-title>{{ readiness.ready ? 'Preparación técnica para 1.0 completada' : 'Preparación técnica para 1.0 pendiente' }}</mat-card-title></mat-card-header>
        <mat-card-content>
          <p>{{ readiness.ready ? 'Todos los documentos públicos mínimos están publicados y la configuración esencial está completa.' : 'Completa los elementos pendientes antes de considerar cerrado el bloque legal técnico.' }}</p>
          <div class="readiness-grid"><div *ngFor="let document of readiness.documents" [class.ready]="document.published"><mat-icon>{{ document.published ? 'check_circle' : 'radio_button_unchecked' }}</mat-icon><span>{{ document.title }}</span></div></div>
          <div *ngIf="readiness.missingConfiguration.length" class="missing-config"><strong>Configuración pendiente:</strong> {{ readiness.missingConfiguration.join(', ') }}</div>
          <small>{{ readiness.note }}</small>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>business</mat-icon><mat-card-title>Identidad del titular</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content class="grid">
          <mat-form-field appearance="outline"><mat-label>Nombre / denominación legal</mat-label><input matInput formControlName="legal_name"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>NIF / CIF</mat-label><input matInput formControlName="tax_id"></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Domicilio</mat-label><input matInput formControlName="address"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email de contacto</mat-label><input matInput formControlName="contact_email"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Teléfono</mat-label><input matInput formControlName="contact_phone"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email de privacidad</mat-label><input matInput formControlName="privacy_email"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email DPO (si procede)</mat-label><input matInput formControlName="dpo_email"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Web</mat-label><input matInput formControlName="website"></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Registro, autorización o información profesional que proceda</mat-label><textarea matInput rows="2" formControlName="registration_information"></textarea></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>policy</mat-icon><mat-card-title>Privacidad, proveedores y conservación</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content>
          <mat-form-field appearance="outline" class="full"><mat-label>Resumen de proveedores/subencargados</mat-label><textarea matInput rows="2" formControlName="providers_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Transferencias internacionales</mat-label><textarea matInput rows="2" formControlName="international_transfers_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Procedimiento/canal de notificación de brechas</mat-label><textarea matInput rows="2" formControlName="breach_notification_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Referencia a matriz de conservación</mat-label><input matInput formControlName="retention_policy_reference"></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Subencargados y procedimiento de autorización</mat-label><textarea matInput rows="2" formControlName="subprocessors_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Terceros/cookies no esenciales</mat-label><textarea matInput rows="2" formControlName="cookie_third_parties"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Cookies no esenciales</mat-label><textarea matInput rows="2" formControlName="non_essential_cookies_summary"></textarea></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>description</mat-icon><mat-card-title>Contratación, soporte y reclamaciones</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content>
          <mat-form-field appearance="outline" class="full"><mat-label>Cancelación y renovación</mat-label><textarea matInput rows="2" formControlName="cancellation_policy_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Email de soporte</mat-label><input matInput formControlName="support_email"></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Condiciones de soporte</mat-label><textarea matInput rows="2" formControlName="support_policy_summary"></textarea></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Email de reclamaciones</mat-label><input matInput formControlName="claims_email"></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Ley aplicable / jurisdicción</mat-label><textarea matInput rows="2" formControlName="governing_law_summary"></textarea></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>history</mat-icon><mat-card-title>Control documental</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content class="grid">
          <mat-form-field appearance="outline"><mat-label>Versión documental</mat-label><input matInput formControlName="document_version"></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Fecha de actualización</mat-label><input matInput formControlName="last_update_date"></mat-form-field>
        </mat-card-content>
      </mat-card>

      <section class="generator">
        <h2>Generar borradores</h2>
        <p>Genera una nueva versión de cada plantilla con los datos actuales. Siempre se guarda como <strong>borrador</strong>; no se publica automáticamente.</p>
        <div class="template-grid">
          <button mat-stroked-button *ngFor="let template of templates" (click)="generate(template.key)" [disabled]="generating">
            <mat-icon>description</mat-icon>{{ template.key }}
          </button>
        </div>
        <div class="documents"><h2>Documentos generados</h2><div class="doc-row" *ngFor="let doc of documents" (click)="openDocument(doc.id)"><div><strong>{{ doc.title }}</strong><small>v{{doc.version}} · {{doc.status}}</small></div><span *ngIf="doc.hasUnresolvedPlaceholders" class="pending">Pendientes: {{doc.unresolved?.join(", ")}}</span><span *ngIf="!doc.hasUnresolvedPlaceholders" class="ready">Sin placeholders</span></div></div>
      <section *ngIf="selectedDocument" class="editor"><h2>{{selectedDocument.title}} · v{{selectedDocument.version}}</h2><textarea [(ngModel)]="selectedDocument.content" rows="18"></textarea><div class="actions"><button mat-stroked-button (click)="saveDocument()">Guardar borrador</button><button mat-raised-button color="primary" (click)="publishDocument()" [disabled]="selectedDocument.unresolved?.length">Validar y publicar</button></div></section>
      <div *ngIf="lastGenerated" class="generated">
          <strong>{{ lastGenerated.title }}</strong> · versión {{ lastGenerated.version }}
          <span *ngIf="lastGenerated.unresolved?.length"> · Pendientes: {{ (lastGenerated.unresolved ?? []).join(', ') }}</span>
          <span *ngIf="!lastGenerated.unresolved?.length"> · Sin placeholders pendientes</span>
        </div>
      </section>

      <div class="actions"><button mat-raised-button color="primary" (click)="save()" [disabled]="saving"><mat-icon>save</mat-icon>{{ saving ? 'Guardando...' : 'Guardar configuración legal' }}</button></div>
    </main>
  `,
  styles: [`
    .page{padding:24px;display:grid;gap:18px;max-width:1100px}.page h1{margin:0}.page header p{color:#64748b}.notice{display:flex;gap:10px;padding:14px;border-radius:10px;background:#fff7ed;color:#9a3412}.readiness{border-left:4px solid #0f766e}.readiness-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px;margin:12px 0}.readiness-grid>div{display:flex;align-items:center;gap:8px;padding:8px;border-radius:8px;background:#f8fafc}.readiness-grid>div.ready{color:#166534;background:#f0fdf4}.missing-config{margin:10px 0;padding:10px;border-radius:8px;background:#fff7ed;color:#9a3412}.readiness small{display:block;color:#64748b}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;padding-top:18px}.wide{grid-column:1/-1}.full{width:100%}.generator{padding:18px;border:1px solid #e2e8f0;border-radius:12px}.generator h2{margin:0 0 4px}.generator p{color:#64748b}.template-grid{display:flex;flex-wrap:wrap;gap:8px}.generated{margin-top:14px;padding:10px;border-radius:8px;background:#f8fafc}.documents{display:grid;gap:8px}.doc-row{display:flex;justify-content:space-between;gap:12px;padding:12px;border:1px solid #e2e8f0;border-radius:8px;cursor:pointer}.doc-row small{display:block;color:#64748b}.pending{color:#b45309}.ready{color:#15803d}.editor textarea{width:100%;box-sizing:border-box;font:14px/1.5 monospace;padding:12px;border:1px solid #cbd5e1;border-radius:8px}.actions{display:flex;justify-content:flex-end}@media(max-width:700px){.readiness-grid{grid-template-columns:1fr}.grid{grid-template-columns:1fr}.wide{grid-column:auto}}
  `]
})
export class AdminLegalSettingsComponent implements OnInit {
  form: FormGroup;
  saving = false;
  generating = false;
  templates: { key: string; file: string }[] = [];
  lastGenerated: LegalGeneratedDocument | null = null;
  documents: LegalGeneratedDocument[] = [];
  readiness: LegalReadiness | null = null;
  selectedDocument: LegalGeneratedDocument | null = null;

  constructor(private fb: FormBuilder, private legal: LegalConfigurationService, private generator: LegalDocumentGeneratorService, private legalService: LegalService, private snack: MatSnackBar) {
    this.form = this.fb.group({
      legal_name:[''], tax_id:[''], address:[''], contact_email:[''], contact_phone:[''], privacy_email:[''], dpo_email:[''], website:[''],
      registration_information:[''], providers_summary:[''], international_transfers_summary:[''], retention_policy_reference:[''],
      cancellation_policy_summary:[''], support_email:[''], support_policy_summary:[''], claims_email:[''], governing_law_summary:[''],
      non_essential_cookies_summary:[''], cookie_third_parties:[''], subprocessors_summary:[''], breach_notification_summary:[''], document_version:['1'], last_update_date:['']
    });
  }

  ngOnInit(): void {
    this.loadDocuments();
    this.legalService.getReadiness().subscribe({ next: readiness => this.readiness = readiness, error: () => this.readiness = null });
    this.generator.getTemplates().subscribe({ next: templates => this.templates = templates, error: () => this.snack.open('No se pudieron cargar las plantillas.', 'Cerrar', {duration:4000}) });
    this.legal.getPlatform().subscribe({
      next: settings => { const values: Record<string,string> = {}; settings.forEach(s => values[s.key]=s.value ?? ''); this.form.patchValue(values); },
      error: () => this.snack.open('No se pudo cargar la configuración legal de DietoExpress.', 'Cerrar', {duration:4000})
    });
  }

  loadDocuments(): void { this.generator.list().subscribe({ next: docs => this.documents=docs, error:()=>this.snack.open('No se pudieron cargar los documentos.', 'Cerrar', {duration:4000}) }); }
  openDocument(id:number): void { this.generator.get(id).subscribe({next:doc=>this.selectedDocument=doc,error:()=>this.snack.open('No se pudo abrir el documento.', 'Cerrar',{duration:4000})}); }
  saveDocument(): void { if(!this.selectedDocument?.content)return; this.generator.update(this.selectedDocument.id,this.selectedDocument.content).subscribe({next:()=>{this.snack.open('Borrador guardado.','Cerrar',{duration:2500});this.loadDocuments();},error:()=>this.snack.open('No se pudo guardar el borrador.','Cerrar',{duration:4000})}); }
  publishDocument(): void { if(!this.selectedDocument || this.selectedDocument.unresolved?.length)return; if(!confirm('¿Publicar esta versión como documento legal oficial?'))return; this.generator.publish(this.selectedDocument.id).subscribe({next:()=>{this.snack.open('Documento publicado.','Cerrar',{duration:3000});this.selectedDocument=null;this.loadDocuments();},error:()=>this.snack.open('No se pudo publicar el documento.','Cerrar',{duration:4000})}); }

  generate(templateKey: string): void {
    if (this.generating) return;
    this.generating = true;
    this.generator.generate(templateKey).subscribe({
      next: document => {
        this.generating = false;
        this.lastGenerated = document;
        this.snack.open('Borrador generado.', 'Cerrar', { duration: 3000 });
      },
      error: () => {
        this.generating = false;
        this.snack.open('No se pudo generar el borrador.', 'Cerrar', { duration: 4000 });
      }
    });
  }

  save(): void {
    if (this.saving) return;
    this.saving = true;
    this.legal.savePlatform(this.form.getRawValue()).subscribe({
      next: () => { this.saving=false; this.snack.open('Configuración legal de DietoExpress guardada.', 'Cerrar', {duration:3000}); },
      error: () => { this.saving=false; this.snack.open('No se pudo guardar la configuración legal.', 'Cerrar', {duration:4000}); }
    });
  }
}
