import { CommonModule, DatePipe } from '@angular/common';
import { Component, OnInit, OnDestroy } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { PatientChatComponent } from '../patient-chat/patient-chat.component';
import { Subscription, interval, startWith, switchMap, catchError, of } from 'rxjs';

interface Conversation {
  conversationId: number; clientId: number; clientName: string; updatedAt: string; lastMessage: string; unreadCount: number;
}

@Component({
  selector: 'app-messages',
  standalone: true,
  imports: [CommonModule, DatePipe, MatIconModule, PatientChatComponent],
  templateUrl: './messages.component.html',
  styleUrls: ['./messages.component.css']
})
// El componente coordina la carga y actualización de conversaciones sin asumir autorización propia; el backend determina qué mensajes puede consultar el usuario.
export class MessagesComponent implements OnInit, OnDestroy {
  conversations: Conversation[] = [];
  selectedClientId: number | null = null;
  loading = true;
  error: string | null = null;
  private refreshSubscription?: Subscription;

  constructor(private http: HttpClient, private router: Router) {}

  // Se hace polling periódico porque la mensajería no depende de un canal WebSocket; cada ciclo vuelve a consultar solo el resumen de conversaciones.
  ngOnInit(): void {
    this.refreshSubscription = interval(15000).pipe(
      startWith(0),
      switchMap(() => this.http.get<Conversation[]>('/api/messages/conversations').pipe(catchError(() => of([]))))
    ).subscribe(items => {
      this.conversations = items || [];
      this.loading = false;
      if (this.selectedClientId && !this.conversations.some(c => c.clientId === this.selectedClientId)) {
        this.selectedClientId = null;
      }
    });
  }

  // Cancelar el intervalo es importante para evitar peticiones y actualizaciones sobre un componente que ya no está en pantalla.
  ngOnDestroy(): void { this.refreshSubscription?.unsubscribe(); }

  select(conversation: Conversation): void { this.selectedClientId = conversation.clientId; }

  openClient(): void {
    if (this.selectedClientId) this.router.navigate(['/clients', this.selectedClientId]);
  }

  get totalUnread(): number { return this.conversations.reduce((sum, c) => sum + c.unreadCount, 0); }

  trackByClient(_: number, item: Conversation): number { return item.clientId; }
}