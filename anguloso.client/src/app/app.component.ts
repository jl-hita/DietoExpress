import { RouterOutlet } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { Component, HostListener, inject, ChangeDetectionStrategy} from '@angular/core';
import { AuthService } from './servicios/auth.service';
import { ErrorReportBannerComponent } from './componentes/error-report-banner/error-report-banner.component';

interface MaintenanceNotice {
  active: boolean;
  title: string;
  message: string;
  startedAtUtc: string;
}

interface HttpDiagnosticEntry {
  time: string;
  level: 'info' | 'debug' | 'error';
  message: string;
  details?: Record<string, unknown>;
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-root',
  templateUrl: './app.component.html',
  standalone: true,
  imports: [CommonModule, RouterOutlet, ErrorReportBannerComponent],
  styleUrl: './app.component.css'
})
// El componente raíz coordina el estado global mínimo de la aplicación; la lógica de negocio permanece en servicios y componentes especializados.
export class AppComponent {
  professionalPushBusy = false;
  professionalPushMessage = '';
  professionalPushSupported = typeof window !== 'undefined' && 'Notification' in window && 'serviceWorker' in navigator && 'PushManager' in window;

  constructor(private readonly http: HttpClient) {}

  get canEnableProfessionalPush(): boolean {
    const role = this.authService.getRole();
    return this.professionalPushSupported && (role === 'nutritionist' || role === 'clinic_admin');
  }

  async enableProfessionalPush(): Promise<void> {
    if (!this.canEnableProfessionalPush || this.professionalPushBusy) return;
    this.professionalPushBusy = true;
    this.professionalPushMessage = '';
    try {
      if (await Notification.requestPermission() !== 'granted') {
        this.professionalPushMessage = 'Permite las notificaciones en el navegador para recibir recordatorios.';
        return;
      }
      const key = await this.http.get<{ publicKey: string }>('/api/professional/push/vapid-public-key').toPromise();
      if (!key?.publicKey) {
        this.professionalPushMessage = 'Las notificaciones push no están configuradas todavía.';
        return;
      }
      await navigator.serviceWorker.register('/sw.js');
      const registration = await navigator.serviceWorker.ready;
      let subscription = await registration.pushManager.getSubscription();
      if (!subscription) {
        const padding = '='.repeat((4 - key.publicKey.length % 4) % 4);
        const raw = window.atob((key.publicKey + padding).replace(/-/g, '+').replace(/_/g, '/'));
        const applicationServerKey = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; i++) applicationServerKey[i] = raw.charCodeAt(i);
        subscription = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey });
      }
      const json = subscription.toJSON();
      if (!json.endpoint || !json.keys?.['p256dh'] || !json.keys?.['auth']) throw new Error('Suscripción incompleta');
      await this.http.post('/api/professional/push/subscriptions', { endpoint: json.endpoint, p256dh: json.keys['p256dh'], auth: json.keys['auth'] }).toPromise();
      this.professionalPushMessage = 'Avisos de cita activados en este navegador.';
    } catch {
      this.professionalPushMessage = 'No se han podido activar los avisos. Comprueba los permisos del navegador e inténtalo de nuevo.';
    } finally {
      this.professionalPushBusy = false;
    }
  }

  title = 'anguloso.client';
  maintenanceNotice?: MaintenanceNotice;
  readonly authService = inject(AuthService);

  httpDiagnosticsOpen = false;
  httpDiagnostics: HttpDiagnosticEntry[] = [];
  private readonly maxDiagnosticEntries = 250;

  @HostListener('window:dieto-http-cd-log', ['$event'])
  onHttpDiagnostic(event: Event): void {
    // Los diagnósticos contienen rutas y detalles técnicos: solo se recopilan para soporte superadmin.
    if (!this.authService.isSuperAdmin()) return;
    const customEvent = event as CustomEvent<HttpDiagnosticEntry>;
    if (!customEvent.detail) return;
    this.httpDiagnostics = [...this.httpDiagnostics, customEvent.detail].slice(-this.maxDiagnosticEntries);
  }

  clearHttpDiagnostics(): void {
    this.httpDiagnostics = [];
  }

  async copyHttpDiagnostics(): Promise<void> {
    const text = this.httpDiagnostics.map(entry => {
      const details = entry.details ? ` ${JSON.stringify(entry.details)}` : '';
      return `[${entry.time}] ${entry.message}${details}`;
    }).join('\n');
    try {
      await navigator.clipboard.writeText(text);
    } catch {
      // La lista permanece visible para poder seleccionarla y copiarla manualmente.
    }
  }
}
