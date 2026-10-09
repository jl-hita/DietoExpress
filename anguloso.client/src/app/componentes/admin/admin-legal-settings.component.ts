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
import { forkJoin } from 'rxjs';
import { LegalConfigurationService } from '../../servicios/legal-configuration.service';
import { LegalReadiness, LegalService } from '../../servicios/legal.service';
import { LegalDocumentGeneratorService, LegalGeneratedDocument } from '../../servicios/legal-document-generator.service';

@Component({
  selector: 'app-admin-legal-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, ReactiveFormsModule, MatButtonModule, MatCardModule, MatDividerModule, MatFormFieldModule, MatIconModule, MatInputModule, MatSnackBarModule],
  template: `
    <main class="page" [formGroup]="form">
      <header><div><h1>Configuración legal de DietoExpress</h1><p>Datos del titular y de la plataforma que se insertarán en las plantillas legales. Cada campo incluye una explicación y ejemplos orientativos; usa solo información real y verificable.</p></div></header>

      <div class="notice"><mat-icon>warning</mat-icon><span>Los ejemplos son orientativos, no afirmaciones legales para copiar sin comprobar. Completar estos campos no equivale a una revisión jurídica. Los documentos publicados deben estar sin placeholders y revisados antes de activarse.</span></div>
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
          <mat-form-field appearance="outline"><mat-label>Nombre / denominación legal</mat-label><input matInput formControlName="legal_name"><mat-hint>{{ fieldHelp['legal_name'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>NIF / CIF</mat-label><input matInput formControlName="tax_id"><mat-hint>{{ fieldHelp['tax_id'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Domicilio</mat-label><input matInput formControlName="address"><mat-hint>{{ fieldHelp['address'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email de contacto</mat-label><input matInput formControlName="contact_email"><mat-hint>{{ fieldHelp['contact_email'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Teléfono</mat-label><input matInput formControlName="contact_phone"><mat-hint>{{ fieldHelp['contact_phone'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email de privacidad</mat-label><input matInput formControlName="privacy_email"><mat-hint>{{ fieldHelp['privacy_email'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Email DPO (si procede)</mat-label><input matInput formControlName="dpo_email"><mat-hint>{{ fieldHelp['dpo_email'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Web</mat-label><input matInput formControlName="website"><mat-hint>{{ fieldHelp['website'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Registro, autorización o información profesional que proceda</mat-label><textarea matInput rows="2" formControlName="registration_information"></textarea><mat-hint>{{ fieldHelp['registration_information'] }}</mat-hint></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>policy</mat-icon><mat-card-title>Privacidad, proveedores y conservación</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content>
          <mat-form-field appearance="outline" class="full"><mat-label>Resumen de proveedores/subencargados</mat-label><textarea matInput rows="2" formControlName="providers_summary"></textarea><mat-hint>{{ fieldHelp['providers_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Transferencias internacionales</mat-label><textarea matInput rows="2" formControlName="international_transfers_summary"></textarea><mat-hint>{{ fieldHelp['international_transfers_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Procedimiento/canal de notificación de brechas</mat-label><textarea matInput rows="2" formControlName="breach_notification_summary"></textarea><mat-hint>{{ fieldHelp['breach_notification_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Referencia a matriz de conservación</mat-label><input matInput formControlName="retention_policy_reference"><mat-hint>{{ fieldHelp['retention_policy_reference'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Subencargados y procedimiento de autorización</mat-label><textarea matInput rows="2" formControlName="subprocessors_summary"></textarea><mat-hint>{{ fieldHelp['subprocessors_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Terceros/cookies no esenciales</mat-label><textarea matInput rows="2" formControlName="cookie_third_parties"></textarea><mat-hint>{{ fieldHelp['cookie_third_parties'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Cookies no esenciales</mat-label><textarea matInput rows="2" formControlName="non_essential_cookies_summary"></textarea><mat-hint>{{ fieldHelp['non_essential_cookies_summary'] }}</mat-hint></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>description</mat-icon><mat-card-title>Contratación, soporte y reclamaciones</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content>
          <mat-form-field appearance="outline" class="full"><mat-label>Cancelación y renovación</mat-label><textarea matInput rows="2" formControlName="cancellation_policy_summary"></textarea><mat-hint>{{ fieldHelp['cancellation_policy_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Email de soporte</mat-label><input matInput formControlName="support_email"><mat-hint>{{ fieldHelp['support_email'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Condiciones de soporte</mat-label><textarea matInput rows="2" formControlName="support_policy_summary"></textarea><mat-hint>{{ fieldHelp['support_policy_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Email de reclamaciones</mat-label><input matInput formControlName="claims_email"><mat-hint>{{ fieldHelp['claims_email'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Ley aplicable / jurisdicción</mat-label><textarea matInput rows="2" formControlName="governing_law_summary"></textarea><mat-hint>{{ fieldHelp['governing_law_summary'] }}</mat-hint></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>payments</mat-icon><mat-card-title>Contratación y condiciones económicas</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content>
          <mat-form-field appearance="outline" class="full"><mat-label>Precios, planes e impuestos</mat-label><textarea matInput rows="3" formControlName="pricing_summary"></textarea><mat-hint>{{ fieldHelp['pricing_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Facturación, renovación y vencimiento</mat-label><textarea matInput rows="3" formControlName="billing_terms_summary"></textarea><mat-hint>{{ fieldHelp['billing_terms_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Cancelación y reembolsos</mat-label><textarea matInput rows="3" formControlName="refund_summary"></textarea><mat-hint>{{ fieldHelp['refund_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Desistimiento/limitaciones aplicables a consumidores</mat-label><textarea matInput rows="3" formControlName="consumer_withdrawal_summary"></textarea><mat-hint>{{ fieldHelp['consumer_withdrawal_summary'] }}</mat-hint></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>fact_check</mat-icon><mat-card-title>RAT y conservación</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content>
          <mat-form-field appearance="outline"><mat-label>Rol del responsable</mat-label><input matInput formControlName="rat_controller_role"><mat-hint>{{ fieldHelp['rat_controller_role'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Fecha de revisión RAT</mat-label><input matInput formControlName="rat_review_date"><mat-hint>{{ fieldHelp['rat_review_date'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Cuentas y seguridad: finalidades, datos, bases y conservación</mat-label><textarea matInput rows="4" formControlName="rat_accounts_summary"></textarea><mat-hint>{{ fieldHelp['rat_accounts_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Suscripciones y facturación: finalidades, datos, bases y conservación</mat-label><textarea matInput rows="4" formControlName="rat_billing_summary"></textarea><mat-hint>{{ fieldHelp['rat_billing_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Pacientes: tratamientos propios/por cuenta de profesionales, categorías y bases</mat-label><textarea matInput rows="4" formControlName="rat_patient_summary"></textarea><mat-hint>{{ fieldHelp['rat_patient_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Seguridad, auditoría e incidencias</mat-label><textarea matInput rows="3" formControlName="rat_security_summary"></textarea><mat-hint>{{ fieldHelp['rat_security_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Derechos y solicitudes</mat-label><textarea matInput rows="3" formControlName="rat_rights_summary"></textarea><mat-hint>{{ fieldHelp['rat_rights_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="full"><mat-label>Matriz de conservación y supresión</mat-label><textarea matInput rows="7" formControlName="retention_matrix_summary"></textarea><mat-hint>{{ fieldHelp['retention_matrix_summary'] }}</mat-hint></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>security</mat-icon><mat-card-title>Análisis de riesgos / EIPD</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content class="grid">
          <mat-form-field appearance="outline"><mat-label>Responsable del análisis</mat-label><input matInput formControlName="risk_owner"><mat-hint>{{ fieldHelp['risk_owner'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Fecha</mat-label><input matInput formControlName="risk_date"><mat-hint>{{ fieldHelp['risk_date'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Versión</mat-label><input matInput formControlName="risk_version"><mat-hint>{{ fieldHelp['risk_version'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Próxima revisión</mat-label><input matInput formControlName="risk_next_review"><mat-hint>{{ fieldHelp['risk_next_review'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Alcance</mat-label><textarea matInput rows="3" formControlName="risk_scope"></textarea><mat-hint>{{ fieldHelp['risk_scope'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Riesgos identificados</mat-label><textarea matInput rows="5" formControlName="risk_summary"></textarea><mat-hint>{{ fieldHelp['risk_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Medidas técnicas y organizativas</mat-label><textarea matInput rows="5" formControlName="risk_controls_summary"></textarea><mat-hint>{{ fieldHelp['risk_controls_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Riesgo residual</mat-label><textarea matInput rows="3" formControlName="risk_residual_risk_summary"></textarea><mat-hint>{{ fieldHelp['risk_residual_risk_summary'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Decisión EIPD</mat-label><input matInput formControlName="risk_dpia_decision"><mat-hint>{{ fieldHelp['risk_dpia_decision'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" class="wide"><mat-label>Justificación EIPD</mat-label><textarea matInput rows="4" formControlName="risk_dpia_justification"></textarea><mat-hint>{{ fieldHelp['risk_dpia_justification'] }}</mat-hint></mat-form-field>
        </mat-card-content>
      </mat-card>

      <mat-card><mat-card-header><mat-icon mat-card-avatar>history</mat-icon><mat-card-title>Control documental</mat-card-title></mat-card-header><mat-divider></mat-divider>
        <mat-card-content class="grid">
          <mat-form-field appearance="outline"><mat-label>Versión documental</mat-label><input matInput formControlName="document_version"><mat-hint>{{ fieldHelp['document_version'] }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline"><mat-label>Fecha de actualización</mat-label><input matInput formControlName="last_update_date"><mat-hint>{{ fieldHelp['last_update_date'] }}</mat-hint></mat-form-field>
        </mat-card-content>
      </mat-card>

      <section class="generator">
        <h2>Generar borradores</h2>
        <p>Genera una nueva versión de cada plantilla con los datos actuales. Siempre se guarda como <strong>borrador</strong>; no se publica automáticamente.</p>
        <div class="generator-actions"><button mat-raised-button color="primary" (click)="generateAll()" [disabled]="generating"><mat-icon>autorenew</mat-icon>{{ generating ? 'Generando...' : 'Generar todos los documentos aplicables' }}</button></div>
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
    .page{padding:24px;display:grid;gap:18px;max-width:1100px}.page h1{margin:0}.page header p{color:#64748b}.notice{display:flex;gap:10px;padding:14px;border-radius:10px;background:#fff7ed;color:#9a3412}.readiness{border-left:4px solid #0f766e}.readiness-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px;margin:12px 0}.readiness-grid>div{display:flex;align-items:center;gap:8px;padding:8px;border-radius:8px;background:#f8fafc}.readiness-grid>div.ready{color:#166534;background:#f0fdf4}.missing-config{margin:10px 0;padding:10px;border-radius:8px;background:#fff7ed;color:#9a3412}.readiness small{display:block;color:#64748b}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:12px;padding-top:18px}.wide{grid-column:1/-1}.full{width:100%}.generator{padding:18px;border:1px solid #e2e8f0;border-radius:12px}.generator h2{margin:0 0 4px}.generator p{color:#64748b}.generator-actions{display:flex;justify-content:flex-start;margin:12px 0}.template-grid{display:flex;flex-wrap:wrap;gap:8px}.generated{margin-top:14px;padding:10px;border-radius:8px;background:#f8fafc}.documents{display:grid;gap:8px}.doc-row{display:flex;justify-content:space-between;gap:12px;padding:12px;border:1px solid #e2e8f0;border-radius:8px;cursor:pointer}.doc-row small{display:block;color:#64748b}.pending{color:#b45309}.ready{color:#15803d}.editor textarea{width:100%;box-sizing:border-box;font:14px/1.5 monospace;padding:12px;border:1px solid #cbd5e1;border-radius:8px}.actions{display:flex;justify-content:flex-end}@media(max-width:700px){.readiness-grid{grid-template-columns:1fr}.grid{grid-template-columns:1fr}.wide{grid-column:auto}}
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

  /** Ayuda contextual por campo: orienta al administrador sin sustituir la revisión jurídica. */
  readonly fieldHelp: Record<string, string> = {
    legal_name: "Nombre legal completo del titular real de DietoExpress (persona o sociedad), no el nombre comercial. Ej.: «Nombre Apellido» o «Empresa Ejemplo, S.L.».",
    tax_id: "NIF de la persona titular o CIF/NIF de la sociedad. No inventes datos; si aún no existe sociedad, indica el NIF personal del titular si es quien explota el servicio.",
    address: "Domicilio legal completo del titular: vía, número, código postal, municipio y país. Usa el domicilio que corresponda legalmente, no una dirección inventada.",
    contact_email: "Correo general para contactar con el titular. Ej.: hola@tudominio.es; debe estar operativo y revisarse con regularidad.",
    contact_phone: "Teléfono de contacto del titular, preferiblemente con prefijo internacional. Ej.: +34 600 000 000.",
    privacy_email: "Canal para consultas de privacidad y ejercicio de derechos. Puede coincidir con el email general si se atiende allí. Ej.: privacidad@tudominio.es.",
    dpo_email: "Solo si has designado formalmente un delegado de protección de datos (DPD/DPO). Introduce su correo; si no procede o no existe designación, déjalo vacío.",
    website: "URL pública principal de la plataforma, incluida la parte https://. Ej.: https://jlhitap.duckdns.org.",
    registration_information: "Datos registrales, autorización administrativa o habilitación profesional solo si son exigibles. Ej.: registro mercantil, tomo/folio/hoja o autoridad autorizante. Si no aplica, déjalo vacío.",
    providers_summary: "Resume los proveedores que realmente tratan datos y para qué. Ej.: alojamiento/servidor, correo transaccional, pagos y videollamadas. Nombra solo servicios que uses y verifica sus condiciones.",
    international_transfers_summary: "Indica si los datos se transfieren fuera del EEE y qué garantías aplican, según los proveedores reales. Ej.: «No se prevén transferencias fuera del EEE» solo si lo has comprobado; si las hay, identifica proveedor, país y mecanismo válido.",
    breach_notification_summary: "Describe el proceso real para detectar, evaluar, documentar y notificar incidentes de seguridad y quién lo gestiona. No prometas plazos/procedimientos que no puedas cumplir.",
    retention_policy_reference: "Referencia breve a la tabla de plazos de conservación que se describe más abajo o al documento interno aprobado. Ej.: «Matriz de conservación y supresión v1.0».",
    subprocessors_summary: "Enumera encargados/subencargados reales, sus servicios y cómo se informa/autoriza su incorporación o cambio. Ej.: proveedor de hosting, email, pagos y videollamadas, únicamente si se usan.",
    cookie_third_parties: "Terceros que instalan o reciben datos mediante cookies/tecnologías similares. Incluye finalidad y proveedor real. Ej.: herramienta analítica concreta si está instalada; si no hay terceros, indícalo tras verificarlo.",
    non_essential_cookies_summary: "Describe cookies no esenciales y su finalidad, duración y cómo se obtiene/rechaza el consentimiento. Ej.: analítica o publicidad solo si se usan; no declares que no existen sin revisar la aplicación.",
    cancellation_policy_summary: "Explica cómo se cancela una suscripción, cuándo surte efecto y si se mantiene el acceso hasta terminar el periodo pagado. Debe coincidir con el comportamiento real de la app y Stripe.",
    support_email: "Correo real de asistencia técnica y consultas sobre el servicio. Ej.: soporte@tudominio.es. Debe existir y estar atendido.",
    support_policy_summary: "Indica canales, horario orientativo y alcance del soporte, así como exclusiones y tiempos objetivo si realmente los garantizas. Ej.: «soporte por email en días laborables» si es cierto.",
    claims_email: "Canal para quejas y reclamaciones contractuales. Puede coincidir con soporte o contacto, siempre que se gestione como tal. Ej.: reclamaciones@tudominio.es.",
    governing_law_summary: "Describe la ley aplicable y los tribunales competentes solo en la medida legalmente válida. Ej.: referencia a la legislación española, respetando las normas imperativas de consumidores; conviene revisión jurídica.",
    pricing_summary: "Resume los planes, precio, periodicidad, moneda e impuestos tal como se muestran antes de contratar. Ej.: Profesional 24,90 €/mes o 249 €/año, más IVA si corresponde; verifica que siga vigente y que el checkout coincida.",
    billing_terms_summary: "Explica cuándo se cobra, renovación automática, método de pago, facturas y qué ocurre al fallar un cobro. Debe reflejar la configuración efectiva de la suscripción y Stripe.",
    refund_summary: "Indica cuándo se admiten reembolsos y cómo solicitarlos, diferenciando cancelación futura de devolución de importes ya cobrados. No prometas una política que no puedas aplicar ni contradigas derechos legales.",
    consumer_withdrawal_summary: "Describe el derecho de desistimiento cuando aplique y las excepciones válidas para servicios digitales/servicios iniciados con consentimiento expreso. No afirmes una renuncia genérica; requiere revisión legal según el producto.",
    rat_controller_role: "Indica el rol del titular en el registro de actividades de tratamiento (RAT). Ej.: «Responsable del tratamiento para cuentas, facturación y seguridad; encargado respecto de ciertos datos de pacientes tratados por cuenta de profesionales», si refleja el flujo real.",
    rat_review_date: "Fecha en que se revisó realmente el RAT, en formato AAAA-MM-DD. Ej.: 2026-10-09. No uses la fecha actual como si se hubiera realizado una revisión que aún no has hecho.",
    rat_accounts_summary: "Para cuentas/autenticación: finalidad, datos usados, base jurídica aplicable, destinatarios y plazo/criterio de conservación. Ej.: email y registros de acceso para gestionar cuenta y proteger el servicio; concreta las bases y plazos tras verificarlos.",
    rat_billing_summary: "Para suscripciones/facturación: datos, finalidad, base jurídica, destinatarios (p. ej., proveedor de pagos real) y conservación contable/fiscal aplicable. No inventes plazos; confírmalos con asesoría.",
    rat_patient_summary: "Describe por separado cuándo DietoExpress actúa como responsable y cuándo como encargado del profesional/clínica. Indica categorías de datos, incluidos datos de salud, finalidad y base jurídica; requiere comprobar el flujo real.",
    rat_security_summary: "Incluye registros de acceso, auditoría e incidencias, finalidad, quién puede acceder y conservación. Ej.: logs técnicos para investigar fallos y proteger cuentas, con acceso restringido y plazo definido.",
    rat_rights_summary: "Explica cómo se reciben, verifican, tramitan y responden solicitudes de acceso, rectificación, supresión, oposición, limitación y portabilidad cuando procedan. Añade el canal operativo y responsables.",
    retention_matrix_summary: "Detalla por categoría: dato, finalidad, plazo o criterio, evento que inicia el plazo, bloqueo legal y borrado/anónimo final. Ej.: «facturas: plazo legal aplicable; logs: X meses según política aprobada». Completa X solo tras verificarlo.",
    risk_owner: "Persona o función que preparó/revisó el análisis de riesgos. Ej.: «Responsable de la plataforma». No implica que esa persona sea DPO.",
    risk_date: "Fecha en la que se hizo realmente el análisis, formato AAAA-MM-DD. Ej.: 2026-10-09.",
    risk_version: "Versión del documento de análisis. Ej.: 1.0; incrementa la versión cuando hagas una revisión sustancial.",
    risk_next_review: "Fecha prevista de próxima revisión, formato AAAA-MM-DD, o tras cambios relevantes/incidentes. Ej.: 2027-04-09 si esa fecha es realista.",
    risk_scope: "Sistemas, usuarios y tratamientos incluidos/excluidos. Ej.: cuentas, pacientes, dietas, citas, chat, pagos, copias de seguridad y videollamadas.",
    risk_summary: "Enumera amenazas y posibles impactos realistas. Ej.: acceso indebido a datos de salud, secuestro de cuenta, envío erróneo, pérdida de datos, indisponibilidad o exposición por proveedor.",
    risk_controls_summary: "Medidas que están implantadas de verdad: control por roles/tenant, cifrado y TLS, copias verificadas, registros, MFA administrativo, actualizaciones y respuesta a incidentes. Distingue medidas pendientes.",
    risk_residual_risk_summary: "Riesgos que quedan tras aplicar las medidas y por qué se consideran aceptables o requieren acciones. Ej.: riesgo residual de indisponibilidad del hosting y medida de recuperación prevista.",
    risk_dpia_decision: "Conclusión documentada sobre si procede una EIPD completa, tras evaluar los criterios legales y el tratamiento real. Ej.: «Pendiente de evaluación» si aún no se ha decidido; no marques «No necesaria» sin análisis.",
    risk_dpia_justification: "Motiva la decisión EIPD con los tratamientos, categorías de datos, escala, personas afectadas y criterios evaluados. Si no se ha analizado, indícalo como pendiente en lugar de inventar una justificación.",
    document_version: "Versión del conjunto de datos/documentos legales. Ej.: 1.0. Actualízala cuando apruebes cambios materiales.",
    last_update_date: "Fecha de la última actualización real de esta configuración/documentación, formato AAAA-MM-DD. Ej.: 2026-10-09.",
  };

  constructor(private fb: FormBuilder, private legal: LegalConfigurationService, private generator: LegalDocumentGeneratorService, private legalService: LegalService, private snack: MatSnackBar) {
    this.form = this.fb.group({
      legal_name:[''], tax_id:[''], address:[''], contact_email:[''], contact_phone:[''], privacy_email:[''], dpo_email:[''], website:[''],
      registration_information:[''], providers_summary:[''], international_transfers_summary:[''], retention_policy_reference:[''],
      cancellation_policy_summary:[''], support_email:[''], support_policy_summary:[''], claims_email:[''], governing_law_summary:[''],
      pricing_summary:[''], billing_terms_summary:[''], refund_summary:[''], consumer_withdrawal_summary:[''],
      non_essential_cookies_summary:[''], cookie_third_parties:[''], subprocessors_summary:[''], breach_notification_summary:[''],
      rat_controller_role:[''], rat_review_date:[''], rat_accounts_summary:[''], rat_billing_summary:[''], rat_patient_summary:[''],
      rat_security_summary:[''], rat_rights_summary:[''], retention_matrix_summary:[''],
      risk_owner:[''], risk_date:[''], risk_version:['1'], risk_scope:[''], risk_summary:[''], risk_controls_summary:[''],
      risk_residual_risk_summary:[''], risk_dpia_decision:[''], risk_dpia_justification:[''], risk_next_review:[''],
      document_version:['1'], last_update_date:['']
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

  generateAll(): void {
    if (this.generating || !this.templates.length) return;
    this.generating = true;
    forkJoin(this.templates.map(template => this.generator.generate(template.key))).subscribe({
      next: documents => {
        this.generating = false;
        this.lastGenerated = documents[documents.length - 1] ?? null;
        this.loadDocuments();
        this.snack.open('Se han generado ' + documents.length + ' borradores con la configuración actual.', 'Cerrar', { duration: 4000 });
      },
      error: () => {
        this.generating = false;
        this.loadDocuments();
        this.snack.open('No se pudieron generar todos los documentos.', 'Cerrar', { duration: 5000 });
      }
    });
  }

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
