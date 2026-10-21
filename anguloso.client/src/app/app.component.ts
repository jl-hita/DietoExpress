import { RouterOutlet } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { Component, HostListener, inject, ChangeDetectionStrategy, OnInit } from '@angular/core';
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
export class AppComponent implements OnInit {
  professionalPushBusy = false;
  professionalPushActivated = false;
  professionalPushMessage = '';
  professionalPushSupported = typeof window !== 'undefined' && 'Notification' in window && 'serviceWorker' in navigator && 'PushManager' in window;

  constructor(private readonly http: HttpClient) {}

  ngOnInit(): void {
    void this.restoreProfessionalPushState();
  }

  private async restoreProfessionalPushState(): Promise<void> {
    try {
      // El componente raíz puede inicializarse antes de que el guard haya restaurado
      // la sesión desde la cookie HttpOnly. Esperar aquí evita abandonar la comprobación
      // cuando getRole() todavía devuelve null durante una recarga.
      const authenticated = await this.authService.restoreSession().toPromise();
      if (!authenticated || !this.canEnableProfessionalPush || Notification.permission !== 'granted') return;

      const registration = await navigator.serviceWorker.getRegistration('/');
      const subscription = await registration?.pushManager.getSubscription();
      if (!subscription) return;
      const json = subscription.toJSON();
      if (!json.endpoint || !json.keys?.['p256dh'] || !json.keys?.['auth']) return;
      // Reconciliar la suscripción del navegador con el servidor también cubre sesiones
      // en las que el navegador conservó la suscripción pero la interfaz se recargó.
      await this.http.post('/api/professional/push/subscriptions', {
        endpoint: json.endpoint,
        p256dh: json.keys['p256dh'],
        auth: json.keys['auth']
      }).toPromise();
      this.professionalPushActivated = true;
    } catch (error) {
      console.warn('[DietoExpress] No se pudo restaurar el estado de Web Push.', error);
    }
  }

  get canEnableProfessionalPush(): boolean {
    const role = this.authService.getRole();
    return this.professionalPushSupported && (role === 'nutritionist' || role === 'clinic_admin');
  }

  async enableProfessionalPush(): Promise<void> {
    if (!this.canEnableProfessionalPush || this.professionalPushBusy) return;
    this.professionalPushBusy = true;
    this.professionalPushMessage = '';
    let stage: 'permission' | 'configuration' | 'service-worker' | 'subscription' | 'registration' = 'permission';

    try {
      const permission = await Notification.requestPermission();
      if (permission !== 'granted') {
        this.professionalPushMessage = permission === 'denied'
          ? 'El navegador ha bloqueado los avisos. Cambia el permiso de notificaciones de este sitio en los ajustes del navegador y vuelve a intentarlo.'
          : 'No se ha concedido permiso para enviar avisos.';
        return;
      }

      stage = 'configuration';
      const key = await this.http.get<{ publicKey: string }>('/api/professional/push/vapid-public-key').toPromise();
      if (!key?.publicKey) {
        this.professionalPushMessage = 'Las notificaciones push no están configuradas en el servidor. Contacta con soporte.';
        return;
      }

      stage = 'service-worker';
      const worker = await navigator.serviceWorker.register('/sw.js');
      const registration = await navigator.serviceWorker.ready;
      if (!registration.active && !worker.active && !worker.waiting) {
        throw new Error('El service worker no ha quedado activo');
      }

      stage = 'subscription';
      let subscription = await registration.pushManager.getSubscription();
      if (!subscription) {
        const padding = '='.repeat((4 - key.publicKey.length % 4) % 4);
        const raw = window.atob((key.publicKey + padding).replace(/-/g, '+').replace(/_/g, '/'));
        const applicationServerKey = new Uint8Array(raw.length);
        for (let i = 0; i < raw.length; i++) applicationServerKey[i] = raw.charCodeAt(i);
        subscription = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey });
      }

      const json = subscription.toJSON();
      if (!json.endpoint || !json.keys?.['p256dh'] || !json.keys?.['auth']) {
        throw new Error('La suscripción push devuelta por el navegador está incompleta');
      }

      stage = 'registration';
      await this.http.post('/api/professional/push/subscriptions', {
        endpoint: json.endpoint,
        p256dh: json.keys['p256dh'],
        auth: json.keys['auth']
      }).toPromise();

      this.professionalPushActivated = true;
      this.professionalPushMessage = 'Avisos de cita activados en este navegador.';
    } catch (error) {
      // Registrar etapa y error técnico para diagnosticar sin mostrar detalles internos al usuario.
      console.error('[DietoExpress] No se pudo activar Web Push.', { stage, error });
      const messages = {
        permission: 'No se ha podido comprobar el permiso de notificaciones del navegador.',
        configuration: 'No se ha podido consultar la configuración de avisos del servidor. Recarga la página y vuelve a intentarlo.',
        'service-worker': 'No se ha podido iniciar el componente de avisos del navegador. Comprueba que el sitio está abierto mediante HTTPS y vuelve a intentarlo.',
        subscription: 'El navegador no ha podido crear la suscripción push. Comprueba los permisos de notificaciones del sitio y vuelve a intentarlo.',
        registration: 'El navegador ha creado la suscripción, pero DietoExpress no ha podido guardarla. Recarga la página y vuelve a intentarlo.'
      };
      this.professionalPushMessage = messages[stage];
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
