import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { AndroidTesterAdminRow, AndroidTestersService } from '../../servicios/android-testers.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-admin-android-testers',
  standalone: true,
  imports: [CommonModule],
  template: `
    <main class="testers">
      <header>
        <div>
          <p class="eyebrow">SuperAdmin</p>
          <h1>Testers Android</h1>
          <p>1. Copia los correos pendientes. 2. Añádelos a la lista de testers en Play Console. 3. Envía las invitaciones.</p>
          @if (optInUrl()) { <p>Enlace de participación: <a [href]="optInUrl()" target="_blank" rel="noopener noreferrer">{{ optInUrl() }}</a></p> }
        </div>
        <button type="button" (click)="load()" [disabled]="busy()">Actualizar</button>
      </header>

      <div class="actions">
        <button type="button" (click)="copyPending()" [disabled]="!pending().length">Copiar correos pendientes ({{ pending().length }})</button>
        <button type="button" (click)="invitePending()" [disabled]="busy() || !pending().length">Ya están en Play Console: enviar invitaciones</button>
      </div>
      @if (message()) { <p role="status">{{ message() }}</p> }

      <div class="table-wrap">
        <table>
          <thead><tr><th>Correo</th><th>Paciente</th><th>Solicitado por</th><th>Fecha</th><th>Estado</th><th></th></tr></thead>
          <tbody>
            @for (row of rows(); track row.id) {
              <tr>
                <td>{{ row.googleEmail }}</td>
                <td>{{ row.clientName }}</td>
                <td>{{ row.requestedBy }}</td>
                <td>{{ row.requestedAt | date:'dd/MM/yyyy HH:mm' }}</td>
                <td>{{ row.status === 'invited' ? 'Invitado el ' + (row.invitedAt | date:'dd/MM/yyyy') : 'Pendiente' }}</td>
                <td><button type="button" (click)="invite([row])" [disabled]="busy()">{{ row.status === 'invited' ? 'Reenviar' : 'Invitar' }}</button></td>
              </tr>
            } @empty {
              <tr><td colspan="6">Todavía no hay solicitudes.</td></tr>
            }
          </tbody>
        </table>
      </div>
    </main>
  `,
  styles: [`
    :host { display:block; }
    .testers { max-width:1200px; margin:0 auto; padding:28px 24px 48px; color:#0f172a; }
    header { display:flex; justify-content:space-between; align-items:flex-start; gap:16px; margin-bottom:18px; }
    h1 { margin:4px 0 8px; font-size:30px; }
    .eyebrow { margin:0; color:#0f766e; text-transform:uppercase; font-weight:700; letter-spacing:1px; font-size:12px; }
    a { overflow-wrap:anywhere; }
    .actions { display:flex; flex-wrap:wrap; gap:10px; margin-bottom:12px; }
    button { border:1px solid #cbd5e1; border-radius:7px; padding:9px 13px; background:#f8fafc; color:#0f172a; cursor:pointer; }
    button:disabled { opacity:.55; cursor:default; }
    .table-wrap { overflow-x:auto; background:white; border:1px solid #e2e8f0; border-radius:14px; }
    table { width:100%; border-collapse:collapse; font-size:13px; }
    th,td { padding:11px 12px; border-bottom:1px solid #e2e8f0; text-align:left; }
    th { color:#475569; background:#f8fafc; }
    @media(max-width:600px) { .testers { padding:20px 12px; } header { flex-direction:column; } }
  `]
})
export class AdminAndroidTestersComponent implements OnInit {
  private readonly testers = inject(AndroidTestersService);
  readonly rows = signal<AndroidTesterAdminRow[]>([]);
  readonly pending = computed(() => this.rows().filter(row => row.status === 'pending'));
  readonly optInUrl = signal('');
  readonly busy = signal(false);
  readonly message = signal('');

  ngOnInit(): void { this.load(); }

  load(): void {
    this.testers.list().subscribe({
      next: result => { this.rows.set(result.requests); this.optInUrl.set(result.optInUrl); },
      error: () => this.message.set('No se han podido cargar las solicitudes.')
    });
  }

  copyPending(): void {
    const emails = [...new Set(this.pending().map(row => row.googleEmail))];
    // Un correo por línea: Play Console lo admite al añadir usuarios y también como CSV.
    navigator.clipboard.writeText(emails.join('\n')).then(
      () => this.message.set(`${emails.length} correos copiados.`),
      () => this.message.set('No se ha podido copiar automáticamente.'));
  }

  invitePending(): void { this.invite(this.pending()); }

  invite(rows: AndroidTesterAdminRow[]): void {
    // El servidor admite hasta 200 invitaciones por llamada.
    const batch = rows.slice(0, 200);
    if (!batch.length || !confirm(`Se enviará la invitación a ${batch.length} correo(s). Deben figurar ya en Play Console. ¿Continuar?`)) return;
    this.busy.set(true);
    this.message.set('Enviando…');
    this.testers.invite(batch.map(row => row.id)).subscribe({
      next: ({ sent, failed }) => {
        this.busy.set(false);
        this.message.set(`Invitaciones enviadas: ${sent}.` + (failed ? ` Fallidas: ${failed} (siguen pendientes).` : ''));
        this.load();
      },
      error: () => {
        this.busy.set(false);
        this.message.set('No se han podido enviar las invitaciones.');
        this.load();
      }
    });
  }
}
