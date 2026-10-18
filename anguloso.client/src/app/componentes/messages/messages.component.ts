import { CommonModule } from '@angular/common';
import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, signal} from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { PatientChatComponent } from '../patient-chat/patient-chat.component';
import { Subscription, interval, startWith, switchMap, catchError, of } from 'rxjs';
import { AuthService } from '../../servicios/auth.service';
import { TimeZonePreferenceService } from '../../servicios/time-zone-preference.service';

interface Conversation {
  conversationId: number; clientId: number; clientName: string; updatedAt: string; lastMessage: string; unreadCount: number;
}

interface BroadcastInboxItem { id: number; title: string; body: string; audience: string; createdAt: string; readAt?: string | null; }

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-messages',
  standalone: true,
  imports: [CommonModule, MatIconModule, PatientChatComponent],
  templateUrl: './messages.component.html',
  styleUrls: ['./messages.component.css']
})
// El componente coordina la carga y actualización de conversaciones sin asumir autorización propia; el backend determina qué mensajes puede consultar el usuario.
export class MessagesComponent implements OnInit, OnDestroy {
  private readonly conversationsState = signal<Conversation[]>([]);
  get conversations(): Conversation[] { return this.conversationsState(); }
  set conversations(value: Conversation[]) { this.conversationsState.set(value); }
  private readonly broadcastInboxState = signal<BroadcastInboxItem[]>([]);
  get broadcastInbox(): BroadcastInboxItem[] { return this.broadcastInboxState(); }
  set broadcastInbox(value: BroadcastInboxItem[]) { this.broadcastInboxState.set(value); }
  selectedClientId: number | null = null;
  selectedBroadcastId: number | null = null;
  private readonly loadingState = signal(true);
  get loading(): boolean { return this.loadingState(); }
  set loading(value: boolean) { this.loadingState.set(value); }
  private readonly broadcastLoadingState = signal(true);
  get broadcastLoading(): boolean { return this.broadcastLoadingState(); }
  private readonly conversationErrorState = signal<string | null>(null);
  private readonly broadcastErrorState = signal<string | null>(null);
  get error(): string | null {
    return [this.conversationErrorState(), this.broadcastErrorState()].filter(Boolean).join(' ') || null;
  }
  private refreshSubscription?: Subscription;
  private broadcastRefreshSubscription?: Subscription;

  constructor(private http: HttpClient, private router: Router, private authService: AuthService, private timeZones: TimeZonePreferenceService) {}

  // Se hace polling periódico porque la mensajería no depende de un canal WebSocket; cada ciclo vuelve a consultar solo el resumen de conversaciones.
  ngOnInit(): void {
    // SuperAdmin no tiene tenant para chats privados; evita las llamadas que inevitablemente terminarían en 403.
    if (!this.authService.isSuperAdmin()) {
      this.refreshSubscription = interval(15000).pipe(
        startWith(0),
        switchMap(() => this.http.get<Conversation[]>('/api/messages/conversations').pipe(catchError(() => {
          this.conversationErrorState.set('No se han podido cargar las conversaciones privadas. Se volverá a intentar automáticamente.');
          return of(null);
        })))
      ).subscribe(items => {
        this.loading = false;
        if (items === null) return;
        this.conversationErrorState.set(null);
        this.conversations = items || [];
        if (this.selectedClientId && !this.conversations.some(c => c.clientId === this.selectedClientId)) {
          this.selectedClientId = null;
        }
      });
    } else {
      // SuperAdmin no consulta conversaciones privadas, pero sí puede leer comunicaciones oficiales.
      this.loading = false;
    }

    // La bandeja de comunicaciones oficiales no requiere un tenant profesional.
    this.broadcastRefreshSubscription = interval(15000).pipe(
      startWith(0),
      switchMap(() => this.http.get<BroadcastInboxItem[]>('/api/broadcast-messages/inbox').pipe(catchError(() => {
        this.broadcastErrorState.set('No se han podido cargar las comunicaciones oficiales. Se volverá a intentar automáticamente.');
        return of(null);
      })))
    ).subscribe(items => {
      this.broadcastLoadingState.set(false);
      if (items === null) return;
      this.broadcastErrorState.set(null);
      this.broadcastInbox = items || [];
    });
  }

  // Cancelar el intervalo es importante para evitar peticiones y actualizaciones sobre un componente que ya no está en pantalla.
  ngOnDestroy(): void {
    this.refreshSubscription?.unsubscribe();
    this.broadcastRefreshSubscription?.unsubscribe();
  }

  select(conversation: Conversation): void {
    this.selectedBroadcastId = null;
    this.selectedClientId = conversation.clientId;
  }

  selectBroadcast(item: BroadcastInboxItem): void {
    this.selectedClientId = null;
    this.selectedBroadcastId = item.id;
    if (item.readAt) return;
    this.http.patch('/api/broadcast-messages/' + item.id + '/read', {}).subscribe({
      next: () => item.readAt = new Date().toISOString()
    });
  }

  get selectedBroadcast(): BroadcastInboxItem | undefined {
    return this.broadcastInbox.find(item => item.id === this.selectedBroadcastId);
  }

  openClient(): void {
    if (this.selectedClientId) this.router.navigate(['/clients', this.selectedClientId]);
  }

  get totalUnread(): number {
    return this.conversations.reduce((sum, c) => sum + c.unreadCount, 0) + this.broadcastInbox.filter(item => !item.readAt).length;
  }

  formatCompactDate(value: string): string { return this.timeZones.formatInPreferredZone(value, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); }

  formatMediumDate(value: string): string { return this.timeZones.formatInPreferredZone(value, { dateStyle: 'medium', timeStyle: 'short' }); }

  trackByClient(_: number, item: Conversation): number { return item.clientId; }
}