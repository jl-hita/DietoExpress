import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';

interface AdminPatient {
  id: number; fullName: string; email: string; phone: string; tenantId?: number | null;
  createdAt?: string | null; archivedAt?: string | null; lastPortalAccess?: string | null;
}
@Component({
  standalone: true,
  selector: 'app-admin-patients',
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatSnackBarModule],
  template: `
  <main class="patients-admin">
    <header><div><h1>Gestión global de pacientes</h1><p>Todos los tenants. Archivar revoca el acceso; anonimizar es irreversible y no equivale a borrar los datos clínicos sujetos a conservación.</p></div><button mat-stroked-button (click)="load()">Actualizar</button></header>
    <mat-card>
      <div class="filters">
        <mat-form-field appearance="outline"><mat-label>Buscar nombre, email, teléfono o ID</mat-label><input matInput [(ngModel)]="search" (keyup.enter)="load()" maxlength="120"></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Estado</mat-label><mat-select [(ngModel)]="status" (selectionChange)="load()"><mat-option value="active">Activos</mat-option><mat-option value="archived">Archivados</mat-option><mat-option value="all">Todos</mat-option></mat-select></mat-form-field>
        <button mat-flat-button color="primary" (click)="load()">Buscar</button>
      </div>
      <p *ngIf="loading">Cargando pacientes…</p><p *ngIf="error" role="alert">{{error}}</p>
      <div class="table-wrap"><table><thead><tr><th>ID</th><th>Paciente</th><th>Tenant</th><th>Alta</th><th>Estado</th><th>Último acceso</th><th>Acciones</th></tr></thead>
      <tbody><tr *ngFor="let p of patients"><td>{{p.id}}</td><td><strong>{{p.fullName}}</strong><br><small>{{p.email}} · {{p.phone}}</small></td><td>{{p.tenantId ?? '—'}}</td><td>{{p.createdAt ? (p.createdAt | date:'dd/MM/yyyy') : '—'}}</td><td>{{p.archivedAt ? 'Archivado' : 'Activo'}}</td><td>{{p.lastPortalAccess ? (p.lastPortalAccess | date:'dd/MM/yyyy HH:mm') : '—'}}</td><td class="actions"><button mat-stroked-button (click)="archive(p)" [disabled]="!!p.archivedAt">Archivar</button><button mat-flat-button color="warn" (click)="erase(p)">Anonimizar cuenta</button></td></tr></tbody></table></div>
      <p *ngIf="!loading && !patients.length">No hay pacientes para estos filtros.</p>
      <footer><button mat-button (click)="changePage(-1)" [disabled]="page<=1">Anterior</button><span>Página {{page}} · {{total}} pacientes</span><button mat-button (click)="changePage(1)" [disabled]="page*pageSize>=total">Siguiente</button></footer>
    </mat-card>
    <mat-card class="warning"><strong>Antes de anonimizar:</strong> confirma que la solicitud y la identidad se han verificado. La acción elimina identificadores directos y revoca el acceso, pero no suprime automáticamente mensajes, documentos, historia clínica, auditorías ni copias de seguridad. Estos datos requieren revisión y tramitación adicional según su base jurídica y conservación aplicable.</mat-card>
  </main>`,
  styles: [`
  .patients-admin{padding:24px;max-width:1400px;margin:auto;color:var(--mat-sys-on-surface,#24342d)}header{display:flex;align-items:center;justify-content:space-between;gap:16px;margin-bottom:20px}h1{margin:0 0 8px}p{color:#52635b}.filters{display:flex;gap:12px;align-items:center;flex-wrap:wrap}.filters mat-form-field:first-child{flex:1;min-width:260px}.table-wrap{overflow:auto}table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:12px 10px;border-bottom:1px solid #dce7e0;vertical-align:top}small{color:#52635b;overflow-wrap:anywhere}.actions{display:flex;gap:8px;flex-wrap:wrap}footer{display:flex;justify-content:space-between;align-items:center;padding-top:12px}.warning{margin-top:18px;background:#fff8e1;border-left:4px solid #c18b00;padding:18px}@media(max-width:700px){header{align-items:flex-start;flex-direction:column}.patients-admin{padding:12px}}`]
})
export class AdminPatientsComponent {
  private readonly http = inject(HttpClient);
  private readonly snack = inject(MatSnackBar);
  patients: AdminPatient[] = []; search = ''; status = 'active'; page = 1; pageSize = 25; total = 0; loading = false; error = '';
  load(): void {
    this.loading = true; this.error = '';
    const params: Record<string,string> = { status: this.status, page: String(this.page), pageSize: String(this.pageSize) };
    if (this.search.trim()) params['search'] = this.search.trim();
    this.http.get<{items:AdminPatient[];totalCount:number}>('/api/admin/patients',{params}).subscribe({
      next: result => { this.patients=result.items; this.total=result.totalCount; this.loading=false; },
      error: err => { this.error=err?.error?.message ?? 'No se pudieron cargar los pacientes.'; this.loading=false; }
    });
  }
  changePage(delta:number): void { this.page=Math.max(1,this.page+delta); this.load(); }
  archive(p:AdminPatient): void {
    if (!confirm('¿Archivar al paciente '+p.id+'? Se revocará su acceso y se conservará el expediente.')) return;
    this.http.post('/api/admin/patients/'+p.id+'/archive',{}).subscribe({
      next:()=>{this.snack.open('Paciente archivado y acceso revocado.','OK',{duration:3500});this.load();},
      error:err=>this.snack.open(err?.error?.message ?? 'No se pudo archivar el paciente.','Cerrar',{duration:5000})
    });
  }
  erase(p:AdminPatient): void {
    const typed=prompt('Acción irreversible. Se anonimizarán los identificadores directos, pero los datos clínicos, mensajes, documentos y copias requieren revisión separada. Escribe ELIMINAR para continuar.');
    if (typed !== 'ELIMINAR') return;
    if (!confirm('¿Confirmas la anonimización de la cuenta del paciente ID '+p.id+'?')) return;
    this.http.post<{message:string}>('/api/admin/patients/'+p.id+'/erase',{confirmId:p.id,confirmText:typed}).subscribe({
      next:r=>{this.snack.open(r.message,'OK',{duration:7000});this.load();},
      error:err=>this.snack.open(err?.error?.message ?? 'No se pudo anonimizar la cuenta.','Cerrar',{duration:5000})
    });
  }
}
