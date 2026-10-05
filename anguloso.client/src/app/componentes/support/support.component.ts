import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AuthService } from '../../servicios/auth.service';
import { SupportCategory, SupportPriority, SupportService, SupportStatus, SupportTicket, SupportTicketSummary } from '../../servicios/support.service';

@Component({
  selector: 'app-support',
  standalone: true,
  imports: [
    CommonModule, FormsModule, MatButtonModule, MatCardModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatSelectModule, MatProgressSpinnerModule
  ],
  templateUrl: './support.component.html',
  styleUrls: ['./support.component.css']
})
export class SupportComponent implements OnInit, OnDestroy {
  tickets: SupportTicketSummary[] = [];
  selected: SupportTicket | null = null;
  loading = false;
  saving = false;
  error = '';
  reply = '';
  internal = false;

  statusFilter = '';
  categoryFilter = '';
  priorityFilter = '';

  newSubject = '';
  newCategory: SupportCategory = 'question';
  newPriority: SupportPriority = 'normal';
  newBody = '';
  creating = false;

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
    private readonly auth: AuthService
  ) {}

  get isSuperAdmin(): boolean {
    return this.auth.isSuperAdmin();
  }

  ngOnInit(): void {
    this.loadTickets();
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
      priority: this.priorityFilter || undefined
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

  openTicket(ticket: SupportTicketSummary): void {
    this.error = '';
    this.support.getTicket(ticket.id).subscribe({
      next: value => this.selected = value,
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

  changeTicket(status: SupportStatus | undefined, priority: SupportPriority | undefined): void {
    if (!this.selected || !this.isSuperAdmin || this.saving) return;
    this.saving = true;
    this.support.updateTicket(this.selected.id, { status, priority }).subscribe({
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

  trackById(_: number, item: { id: number }): number {
    return item.id;
  }
}
