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
export class MessagesComponent implements OnInit, OnDestroy {
  conversations: Conversation[] = [];
  selectedClientId: number | null = null;
  loading = true;
  error: string | null = null;
  private refreshSubscription?: Subscription;

  constructor(private http: HttpClient, private router: Router) {}

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

  ngOnDestroy(): void { this.refreshSubscription?.unsubscribe(); }

  select(conversation: Conversation): void { this.selectedClientId = conversation.clientId; }

  openClient(): void {
    if (this.selectedClientId) this.router.navigate(['/clients', this.selectedClientId]);
  }

  get totalUnread(): number { return this.conversations.reduce((sum, c) => sum + c.unreadCount, 0); }

  trackByClient(_: number, item: Conversation): number { return item.clientId; }
}