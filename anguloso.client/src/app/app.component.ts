import { RouterOutlet } from '@angular/router';
import { CommonModule } from '@angular/common';
import { Component, HostListener, inject, ChangeDetectionStrategy} from '@angular/core';
import { AuthService } from './servicios/auth.service';

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
  imports: [CommonModule, RouterOutlet],
  styleUrl: './app.component.css'
})
// El componente raíz coordina el estado global mínimo de la aplicación; la lógica de negocio permanece en servicios y componentes especializados.
export class AppComponent {
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
