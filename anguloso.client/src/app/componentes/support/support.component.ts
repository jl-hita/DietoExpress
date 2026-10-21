import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnDestroy, OnInit, ChangeDetectionStrategy, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../servicios/auth.service';
import { SupportCategory, SupportPriority, SupportService, SupportStatus, SupportTicket, SupportTicketSummary } from '../../servicios/support.service';
import { DateInputDirective } from '../../directives/date-input.directive';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-support',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatSelectModule, MatProgressSpinnerModule, DateInputDirective
  ],
  templateUrl: './support.component.html',
  styleUrls: ['./support.component.css']
})
export class SupportComponent implements OnInit, OnDestroy {
  private readonly ticketsState = signal<SupportTicketSummary[]>([]);
  get tickets(): SupportTicketSummary[] { return this.ticketsState(); }
  set tickets(value: SupportTicketSummary[]) { this.ticketsState.set(value); }
  private readonly selectedState = signal<SupportTicket | null>(null);
  get selected(): SupportTicket | null { return this.selectedState(); }
  set selected(value: SupportTicket | null) { this.selectedState.set(value); }
  private readonly notificationsState = signal<any[]>([]);
  get notifications(): any[] { return this.notificationsState(); }
  set notifications(value: any[]) { this.notificationsState.set(value); }

  private readonly notificationCountState = signal<number>(0);
  get notificationCount(): number { return this.notificationCountState(); }
  set notificationCount(value: number) { this.notificationCountState.set(value); }
  private readonly loadingState = signal<boolean>(false);
  get loading(): boolean { return this.loadingState(); }
  set loading(value: boolean) { this.loadingState.set(value); }
  private readonly savingState = signal<boolean>(false);
  get saving(): boolean { return this.savingState(); }
  set saving(value: boolean) { this.savingState.set(value); }
  private readonly errorState = signal<string>('');
  get error(): string { return this.errorState(); }
  set error(value: string) { this.errorState.set(value); }
  reply = '';
  internal = false;

  statusFilter = '';
  categoryFilter = '';
  priorityFilter = '';
  searchFilter=''; assignedFilter:number|null=null; fromFilter=''; toFilter='';
  private readonly assigneesState = signal<{id:number;name:string}[]>([]);
  get assignees(): {id:number;name:string}[] { return this.assigneesState(); }
  set assignees(value: {id:number;name:string}[]) { this.assigneesState.set(value); }

  private readonly auditEntriesState = signal<any[]>([]);
  get auditEntries(): any[] { return this.auditEntriesState(); }
  set auditEntries(value: any[]) { this.auditEntriesState.set(value); }

  newSubject = '';
  newCategory: SupportCategory = 'question';
  newPriority: SupportPriority = 'normal';
  newBody = '';
  private readonly creatingState = signal<boolean>(false);
  get creating(): boolean { return this.creatingState(); }
  set creating(value: boolean) { this.creatingState.set(value); }
  broadcastTitle = '';
  broadcastBody = '';
  broadcastAudience = 'professionals';
  private readonly broadcastSendingState = signal<boolean>(false);
  get broadcastSending(): boolean { return this.broadcastSendingState(); }
  set broadcastSending(value: boolean) { this.broadcastSendingState.set(value); }
  private readonly broadcastResultState = signal<string>('');
  get broadcastResult(): string { return this.broadcastResultState(); }
  set broadcastResult(value: string) { this.broadcastResultState.set(value); }
  private readonly broadcastErrorState = signal<string>('');
  get broadcastError(): string { return this.broadcastErrorState(); }
  set broadcastError(value: string) { this.broadcastErrorState.set(value); }

  readonly categories: { value: SupportCategory; label: string }[] = [
    { value: 'problem', label: 'Problema' },
    { value: 'billing', label: 'Facturación' },
    { value: 'account', label: 'Cuenta' },
    { value: 'question', label: 'Consulta' },
    { value: 'suggestion', label: 'Sugerencia' },
    { value: 'other', label: 'Otro' }
  ];
  readonly priorities: { value: SupportPriority; label: string }[] = [
    { value: 'low', label: 'Baja' },
    { value: 'normal', label: 'Normal' },
    { value: 'high', label: 'Alta' },
    { value: 'urgent', label: 'Urgente' }
  ];
  readonly statuses: { value: SupportStatus; label: string }[] = [
    { value: 'open', label: 'Abierto' },
    { value: 'in_progress', label: 'En progreso' },
    { value: 'waiting_user', label: 'Esperando respuesta' },
    { value: 'resolved', label: 'Resuelto' },
    { value: 'closed', label: 'Cerrado' }
  ];

  private refreshSubscription?: Subscription;

  constructor(
    private readonly support: SupportService,
    private readonly auth: AuthService,
    private readonly http: HttpClient
  ) {}

  get isSuperAdmin(): boolean {
    return this.auth.isSuperAdmin();
  }

  ngOnInit(): void {
    this.loadTickets(); this.loadNotifications(); if(this.isSuperAdmin)this.support.getAssignees().subscribe(v=>this.assignees=v);
  }

  ngOnDestroy(): void {
    this.refreshSubscription?.unsubscribe();
  }

  loadTickets(): void {
    this.loading = true;
    this.error = '';
    this.refreshSubscription?.unsubscribe();
    this.refreshSubscription = this.support.getTickets({
      status: this.statusFilter || undefined,
      category: this.categoryFilter || undefined,
      priority: this.priorityFilter || undefined, search: this.searchFilter.trim() || undefined, assignedToUserId: this.assignedFilter || undefined, from: this.fromFilter ? this.fromFilter+'T00:00:00' : undefined, to: this.toFilter ? this.toFilter+'T23:59:59.999' : undefined
    }).subscribe({
      next: tickets => {
        this.tickets = tickets;
        this.loading = false;
        if (this.selected) {
          const current = tickets.find(t => t.id === this.selected?.id);
          if (current && this.selected) {
            this.selected = { ...this.selected, ...current };
          }
        }
      },
      error: () => {
        this.loading = false;
        this.error = 'No se ha podido cargar el soporte.';
      }
    });
  }

  loadNotifications():void{this.support.getNotifications().subscribe(v=>this.notifications=v);this.support.getUnreadNotificationCount().subscribe(v=>this.notificationCount=v);}
  openNotification(n:any):void{this.support.markNotificationRead(n.id).subscribe(()=>{this.loadNotifications();const t=this.tickets.find(x=>x.id===n.ticketId);if(t)this.openTicket(t);});}
  reopenTicket():void{if(!this.selected||this.isSuperAdmin||this.saving)return;this.saving=true;this.support.reopenTicket(this.selected.id).subscribe({next:()=>{this.saving=false;this.loadTickets();},error:()=>{this.saving=false;this.error='No se ha podido reabrir el ticket.';}});}
  loadAudit():void{if(this.selected&&this.isSuperAdmin)this.support.getAudit(this.selected.id).subscribe(v=>this.auditEntries=v);}
  clearFilters():void{this.statusFilter='';this.categoryFilter='';this.priorityFilter='';this.searchFilter='';this.assignedFilter=null;this.fromFilter='';this.toFilter='';this.loadTickets();}

  openTicket(ticket: SupportTicketSummary): void {
    this.error = '';
    this.support.getTicket(ticket.id).subscribe({
      next: value => {this.selected=value;this.auditEntries=[];if(this.isSuperAdmin)this.loadAudit();},
      error: () => this.error = 'No se ha podido abrir el ticket.'
    });
  }

  createTicket(): void {
    if (!this.newSubject.trim() || !this.newBody.trim() || this.creating) return;
    this.creating = true;
    this.error = '';
    this.support.createTicket({
      subject: this.newSubject.trim(),
      category: this.newCategory,
      priority: this.newPriority,
      body: this.newBody.trim()
    }).subscribe({
      next: result => {
        this.creating = false;
        this.newSubject = '';
        this.newBody = '';
        this.newCategory = 'question';
        this.newPriority = 'normal';
        this.loadTickets();
        this.support.getTicket(result.id).subscribe(ticket => this.selected = ticket);
      },
      error: () => {
        this.creating = false;
        this.error = 'No se ha podido crear el ticket.';
      }
    });
  }

  sendReply(): void {
    if (!this.selected || !this.reply.trim() || this.saving) return;
    this.saving = true;
    const id = this.selected.id;
    this.support.addMessage(id, this.reply.trim(), this.isSuperAdmin && this.internal).subscribe({
      next: () => {
        this.reply = '';
        this.internal = false;
        this.saving = false;
        this.support.getTicket(id).subscribe(ticket => {
          this.selected = ticket;
          this.loadTickets();
        });
      },
      error: () => {
        this.saving = false;
        this.error = 'No se ha podido enviar el mensaje.';
      }
    });
  }

  changeTicket(status: SupportStatus | undefined, priority: SupportPriority | undefined, assignedToUserId:number|null|undefined=undefined): void {
    if (!this.selected || !this.isSuperAdmin || this.saving) return;
    this.saving = true;
    this.support.updateTicket(this.selected.id, { status, priority, ...(assignedToUserId !== undefined ? {assignedToUserId} : {}) }).subscribe({
      next: () => {
        this.saving = false;
        this.support.getTicket(this.selected!.id).subscribe(ticket => this.selected = ticket);
        this.loadTickets();
      },
      error: () => {
        this.saving = false;
        this.error = 'No se ha podido actualizar el ticket.';
      }
    });
  }

  statusLabel(status: SupportStatus): string {
    return this.statuses.find(item => item.value === status)?.label ?? status;
  }

  priorityLabel(priority: SupportPriority): string {
    return this.priorities.find(item => item.value === priority)?.label ?? priority;
  }

  categoryLabel(category: SupportCategory): string {
    return this.categories.find(item => item.value === category)?.label ?? category;
  }


  sendBroadcast(): void {
    if (!this.isSuperAdmin || this.broadcastSending || !this.broadcastTitle.trim() || !this.broadcastBody.trim()) return;
    if (!confirm('¿Enviar esta comunicación a todos los destinatarios del público seleccionado?')) return;
    this.broadcastSending = true;
    this.broadcastError = '';
    this.broadcastResult = '';
    this.http.post<{ recipientCount: number }>('/api/broadcast-messages', {
      title: this.broadcastTitle.trim(), body: this.broadcastBody.trim(), audience: this.broadcastAudience
    }).subscribe({
      next: result => {
        this.broadcastSending = false;
        this.broadcastResult = 'Comunicación enviada a ' + result.recipientCount + ' destinatarios.';
        this.broadcastTitle = '';
        this.broadcastBody = '';
      },
      error: err => {
        this.broadcastSending = false;
        this.broadcastError = err?.error?.message || 'No se ha podido enviar la comunicación.';
      }
    });
  }

  trackById(_: number, item: { id: number }): number {
    return item.id;
  }
}

