import { CommonModule, DatePipe } from '@angular/common';
import { Component, Input, OnChanges, SimpleChanges, OnInit } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';

interface ChatMessage { id: number; senderType: 'patient' | 'professional'; senderId?: number | null; body: string; createdAt: string; readAt?: string | null; }

@Component({
  selector: 'app-patient-chat',
  standalone: true,
  imports: [CommonModule, FormsModule, DatePipe, MatIconModule],
  templateUrl: './patient-chat.component.html',
  styleUrls: ['./patient-chat.component.css']
})
export class PatientChatComponent implements OnChanges, OnInit {
  @Input() clientId?: number;
  @Input() patientMode = false;
  messages: ChatMessage[] = [];
  draft = '';
  loading = false;
  sending = false;
  error: string | null = null;

  constructor(private http: HttpClient, private route: ActivatedRoute) {}

  ngOnInit(): void {
    if (!this.patientMode && !this.clientId) {
      const id = Number(this.route.snapshot.paramMap.get('id'));
      if (id > 0) { this.clientId = id; this.load(); }
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['clientId'] || changes['patientMode']) && (this.patientMode || this.clientId)) this.load();
  }

  load(): void {
    this.loading = true; this.error = null;
    const url = this.patientMode ? '/api/messages/patient' : '/api/messages/client/' + this.clientId;
    this.http.get<ChatMessage[]>(url).subscribe({
      next: messages => { this.messages = messages || []; this.loading = false; this.markRead(); },
      error: err => { this.loading = false; this.error = err.status === 404 ? 'No tienes acceso a esta conversación.' : 'No se ha podido cargar la conversación.'; }
    });
  }

  send(): void {
    const body = this.draft.trim();
    if (!body || this.sending || (!this.patientMode && !this.clientId)) return;
    this.sending = true;
    const url = this.patientMode ? '/api/messages/patient' : '/api/messages/client/' + this.clientId;
    this.http.post<{ id: number }>(url, { body }).subscribe({
      next: () => { this.draft = ''; this.sending = false; this.load(); },
      error: err => { this.sending = false; this.error = err.error?.message || 'No se ha podido enviar el mensaje.'; }
    });
  }

  onKeydown(event: KeyboardEvent): void { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); this.send(); } }

  private markRead(): void {
    const url = this.patientMode ? '/api/messages/patient/read' : '/api/messages/client/' + this.clientId + '/read';
    this.http.patch(url, {}).subscribe();
  }
}