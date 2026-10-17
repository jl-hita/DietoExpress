import { CommonModule } from '@angular/common';
import { Component, Input, OnChanges, SimpleChanges, OnInit, ChangeDetectionStrategy, signal} from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { TimeZonePreferenceService } from '../../servicios/time-zone-preference.service';

interface ChatMessage { id: number; senderType: 'patient' | 'professional'; senderId?: number | null; body: string; createdAt: string; readAt?: string | null; }

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-patient-chat',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule],
  templateUrl: './patient-chat.component.html',
  styleUrls: ['./patient-chat.component.css']
})
// Documentación: este componente coordina estado local, validación y llamadas asíncronas; la vista solo refleja ese estado.
export class PatientChatComponent implements OnChanges, OnInit {
  @Input() clientId?: number;
  @Input() patientMode = false;
  private readonly messagesState = signal<ChatMessage[]>([]);
  get messages(): ChatMessage[] { return this.messagesState(); }
  set messages(value: ChatMessage[]) { this.messagesState.set(value); }

  draft = '';

  private readonly loadingState = signal(false);
  get loading(): boolean { return this.loadingState(); }
  set loading(value: boolean) { this.loadingState.set(value); }

  private readonly sendingState = signal(false);
  get sending(): boolean { return this.sendingState(); }
  set sending(value: boolean) { this.sendingState.set(value); }

  private readonly errorState = signal<string | null>(null);
  get error(): string | null { return this.errorState(); }
  set error(value: string | null) { this.errorState.set(value); }

  constructor(private http: HttpClient, private route: ActivatedRoute, private timeZones: TimeZonePreferenceService) {}

  // En modo profesional el cliente puede venir de la ruta; en modo paciente la identidad ya la resuelve el backend a partir de la sesión/token.
  ngOnInit(): void {
    if (!this.patientMode && !this.clientId) {
      const id = Number(this.route.snapshot.paramMap.get('id'));
      if (id > 0) { this.clientId = id; this.load(); }
    }
  }

  ngOnChanges(changes: SimpleChanges): void {
    if ((changes['clientId'] || changes['patientMode']) && (this.patientMode || this.clientId)) this.load();
  }

  // Se reutiliza la misma carga tras enviar un mensaje para mantener el historial y los estados de lectura sincronizados con el servidor.
  load(): void {
    this.loading = true; this.error = null;
    const url = this.patientMode ? '/api/messages/patient' : '/api/messages/client/' + this.clientId;
    this.http.get<ChatMessage[]>(url).subscribe({
      next: messages => { this.messages = messages || []; this.loading = false; this.markRead(); },
      error: err => { this.loading = false; this.error = err.status === 404 ? 'No tienes acceso a esta conversación.' : 'No se ha podido cargar la conversación.'; }
    });
  }

  // La validación local evita peticiones vacías o duplicadas, pero la autorización y la asociación paciente-profesional siguen siendo responsabilidad del API.
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

  formatMessageDate(value: string): string { return this.timeZones.formatInPreferredZone(value, { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }); }

  onKeydown(event: KeyboardEvent): void { if (event.key === 'Enter' && !event.shiftKey) { event.preventDefault(); this.send(); } }

  private markRead(): void {
    const url = this.patientMode ? '/api/messages/patient/read' : '/api/messages/client/' + this.clientId + '/read';
    this.http.patch(url, {}).subscribe();
  }
}