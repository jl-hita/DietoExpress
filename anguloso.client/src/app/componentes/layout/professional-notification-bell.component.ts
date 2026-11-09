import { CommonModule } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, HostListener, ElementRef, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatIconModule } from '@angular/material/icon';
import { catchError, of, switchMap, timer } from 'rxjs';

export interface ProfessionalNotification {
  id: number; type: string; title: string; message: string; actionUrl?: string | null; createdAt: string; readAt?: string | null;
}

/** Campana de avisos del profesional (solicitudes de revisión de rutina, etc.). Consulta el contador cada minuto. */
@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-professional-notification-bell',
  standalone: true,
  imports: [CommonModule, MatIconModule],
  styles: [`
    :host { position: relative; display: inline-block; }
    .bell { position: relative; display: inline-flex; align-items: center; justify-content: center; width: 40px; height: 40px;
      border: 0; border-radius: 50%; background: transparent; color: #334155; cursor: pointer; }
    .bell:hover, .bell:focus-visible { background: #f1f5f9; outline: 2px solid #0f766e; outline-offset: 1px; }
    .badge { position: absolute; top: 2px; right: 2px; min-width: 18px; height: 18px; padding: 0 4px; border-radius: 9px;
      background: #dc2626; color: #fff; font-size: 11px; font-weight: 700; display: inline-flex; align-items: center; justify-content: center; }
    .panel { position: absolute; right: 0; top: 46px; width: min(360px, 92vw); max-height: 70vh; overflow: auto; z-index: 100;
      background: #fff; border: 1px solid #e2e8f0; border-radius: 12px; box-shadow: 0 12px 30px rgba(15,23,42,.18); }
    .panel header { display: flex; justify-content: space-between; align-items: center; padding: 10px 14px; border-bottom: 1px solid #e2e8f0; }
    .panel header button, .item { font: inherit; }
    .link { border: 0; background: none; color: #0f766e; cursor: pointer; font-size: 13px; }
    ul { list-style: none; margin: 0; padding: 0; }
    .item { display: block; width: 100%; text-align: left; border: 0; border-bottom: 1px solid #f1f5f9; background: #fff; padding: 10px 14px; cursor: pointer; }
    .item:hover, .item:focus-visible { background: #f8fafc; }
    .item.unread { background: #f0fdfa; border-left: 3px solid #0f766e; }
    .item strong { display: block; font-size: 14px; color: #0f172a; }
    .item span { display: block; font-size: 13px; color: #475569; margin-top: 2px; }
    .item small { color: #64748b; }
    .empty { padding: 18px 14px; color: #64748b; margin: 0; }
  `],
  template: `
    <button type="button" class="bell" (click)="toggle()" [attr.aria-expanded]="open()" aria-haspopup="true"
      [attr.aria-label]="unread() ? 'Avisos: ' + unread() + ' sin leer' : 'Avisos'">
      <mat-icon aria-hidden="true">{{ unread() ? 'notifications_active' : 'notifications_none' }}</mat-icon>
      <span class="badge" *ngIf="unread()" aria-hidden="true">{{ unread() > 9 ? '9+' : unread() }}</span>
    </button>
    <section class="panel" *ngIf="open()" aria-label="Avisos del profesional">
      <header>
        <strong>Avisos</strong>
        <button type="button" class="link" *ngIf="unread()" (click)="markAllRead()">Marcar todo como leído</button>
      </header>
      <p class="empty" *ngIf="loading()" role="status">Cargando…</p>
      <p class="empty" *ngIf="!loading() && error()" role="alert">{{ error() }}</p>
      <p class="empty" *ngIf="!loading() && !error() && !items().length">No tienes avisos.</p>
      <ul *ngIf="items().length">
        <li *ngFor="let item of items()">
          <button type="button" class="item" [class.unread]="!item.readAt" (click)="openItem(item)">
            <strong>{{ item.title }}</strong>
            <span>{{ item.message }}</span>
            <small>{{ item.createdAt | date:'dd/MM/yyyy HH:mm' }}</small>
          </button>
        </li>
      </ul>
    </section>
  `
})
export class ProfessionalNotificationBellComponent {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly host = inject(ElementRef<HTMLElement>);
  readonly unread = signal(0);
  readonly items = signal<ProfessionalNotification[]>([]);
  readonly open = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');

  constructor() {
    timer(0, 60000).pipe(
      switchMap(() => this.http.get<number>('/api/professional/notifications/unread-count').pipe(catchError(() => of(null)))),
      takeUntilDestroyed(inject(DestroyRef))
    ).subscribe(count => { if (typeof count === 'number') this.unread.set(count); });
  }

  toggle(): void {
    this.open.update(value => !value);
    if (this.open()) this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.http.get<ProfessionalNotification[]>('/api/professional/notifications?limit=30').subscribe({
      next: items => { this.items.set(Array.isArray(items) ? items : []); this.loading.set(false); },
      error: () => { this.error.set('No se han podido cargar los avisos.'); this.loading.set(false); }
    });
  }

  openItem(item: ProfessionalNotification): void {
    if (!item.readAt) {
      this.http.post('/api/professional/notifications/' + item.id + '/read', {}).subscribe({ error: () => undefined });
      this.items.update(list => list.map(entry => entry.id === item.id ? { ...entry, readAt: new Date().toISOString() } : entry));
      this.unread.update(count => Math.max(0, count - 1));
    }
    this.open.set(false);
    // Solo rutas internas de la aplicación.
    if (item.actionUrl && item.actionUrl.startsWith('/') && !item.actionUrl.startsWith('//')) this.router.navigateByUrl(item.actionUrl);
  }

  markAllRead(): void {
    this.http.post('/api/professional/notifications/read-all', {}).subscribe({
      next: () => {
        this.unread.set(0);
        this.items.update(list => list.map(entry => ({ ...entry, readAt: entry.readAt ?? new Date().toISOString() })));
      },
      error: () => this.error.set('No se ha podido marcar como leídos.')
    });
  }

  @HostListener('document:keydown.escape')
  closeOnEscape(): void { this.open.set(false); }

  @HostListener('document:click', ['$event'])
  closeOnOutsideClick(event: Event): void {
    if (this.open() && !this.host.nativeElement.contains(event.target)) this.open.set(false);
  }
}
