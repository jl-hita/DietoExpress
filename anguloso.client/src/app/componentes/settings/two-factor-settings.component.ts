import { ChangeDetectionStrategy, Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Observable } from 'rxjs';
import { TwoFactorService, TwoFactorSetup, TwoFactorStatus } from '../../servicios/two-factor.service';

type Mode = 'idle' | 'setup' | 'codes' | 'disable' | 'regenerate';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-two-factor-settings',
  standalone: true,
  imports: [CommonModule, FormsModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatProgressSpinnerModule],
  template: `
    <section class="two-factor" aria-labelledby="two-factor-title">
      <h3 id="two-factor-title"><mat-icon>verified_user</mat-icon> Verificación en dos pasos</h3>

      <p *ngIf="loading()" class="muted"><mat-spinner diameter="18"></mat-spinner> Cargando…</p>

      <ng-container *ngIf="!loading() && status() as st">
        <p class="muted" *ngIf="mode() === 'idle'">
          {{ st.enabled
            ? 'Activada. Al iniciar sesión se te pedirá un código de tu app autenticadora.'
            : 'Añade una capa extra: además de la contraseña, se pedirá un código temporal de una app autenticadora (Google Authenticator, Microsoft Authenticator, Authy, 1Password…). Muy recomendable al manejar datos de salud.' }}
        </p>
        <p class="muted" *ngIf="mode() === 'idle' && st.enabled">
          Códigos de recuperación disponibles: <strong>{{ st.recoveryCodesRemaining }}</strong>
          <span *ngIf="st.recoveryCodesRemaining <= 2" class="warn"> — te quedan pocos, genera un juego nuevo.</span>
        </p>

        <div class="actions" *ngIf="mode() === 'idle'">
          <button mat-raised-button color="primary" type="button" *ngIf="!st.enabled" (click)="startSetup()" [disabled]="busy()">
            <mat-icon>qr_code_2</mat-icon> Activar verificación en dos pasos
          </button>
          <button mat-stroked-button type="button" *ngIf="st.enabled" (click)="openMode('regenerate')">
            <mat-icon>autorenew</mat-icon> Nuevos códigos de recuperación
          </button>
          <button mat-stroked-button color="warn" type="button" *ngIf="st.enabled" (click)="openMode('disable')">
            <mat-icon>lock_open</mat-icon> Desactivar
          </button>
        </div>
      </ng-container>

      <!-- Alta: escanear QR y confirmar con un código -->
      <div *ngIf="mode() === 'setup' && setupData() as s" class="panel">
        <ol>
          <li>Abre tu app autenticadora y escanea este código QR.</li>
          <li>Si no puedes escanearlo, introduce esta clave manualmente: <code class="secret">{{ s.secret }}</code></li>
          <li>Escribe el código de 6 dígitos que muestra la app para confirmar.</li>
        </ol>
        <img class="qr" [src]="qrDataUri(s)" alt="Código QR para configurar la verificación en dos pasos" width="200" height="200" />
        <mat-form-field appearance="outline" class="code-field">
          <mat-label>Código de 6 dígitos</mat-label>
          <input matInput [(ngModel)]="code" inputmode="numeric" autocomplete="one-time-code" maxlength="7" (keyup.enter)="confirmEnable()" />
        </mat-form-field>
        <div class="actions">
          <button mat-raised-button color="primary" type="button" (click)="confirmEnable()" [disabled]="busy() || !code.trim()">Activar</button>
          <button mat-button type="button" (click)="cancel()">Cancelar</button>
        </div>
      </div>

      <!-- Códigos de recuperación: se muestran una única vez -->
      <div *ngIf="mode() === 'codes'" class="panel">
        <p><strong>Guarda estos códigos de recuperación en un lugar seguro.</strong> Cada uno sirve una sola vez si pierdes el móvil y no se volverán a mostrar.</p>
        <ul class="codes"><li *ngFor="let c of recoveryCodes()"><code>{{ c }}</code></li></ul>
        <div class="actions">
          <button mat-stroked-button type="button" (click)="copyCodes()"><mat-icon>content_copy</mat-icon> Copiar</button>
          <button mat-raised-button color="primary" type="button" (click)="cancel()">He guardado los códigos</button>
        </div>
      </div>

      <!-- Desactivar -->
      <div *ngIf="mode() === 'disable'" class="panel">
        <p>Para desactivar la verificación en dos pasos confirma tu identidad.</p>
        <mat-form-field appearance="outline" class="full-width" *ngIf="needsPassword">
          <mat-label>Contraseña actual</mat-label>
          <input matInput type="password" [(ngModel)]="password" autocomplete="current-password" />
        </mat-form-field>
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>Código de la app o de recuperación</mat-label>
          <input matInput [(ngModel)]="code" autocomplete="one-time-code" maxlength="16" (keyup.enter)="confirmDisable()" />
        </mat-form-field>
        <div class="actions">
          <button mat-raised-button color="warn" type="button" (click)="confirmDisable()" [disabled]="busy() || !code.trim() || (needsPassword && !password)">Desactivar</button>
          <button mat-button type="button" (click)="cancel()">Cancelar</button>
        </div>
      </div>

      <!-- Regenerar códigos -->
      <div *ngIf="mode() === 'regenerate'" class="panel">
        <p>Los códigos de recuperación actuales dejarán de funcionar. Introduce el código actual de tu app autenticadora.</p>
        <mat-form-field appearance="outline" class="code-field">
          <mat-label>Código de 6 dígitos</mat-label>
          <input matInput [(ngModel)]="code" inputmode="numeric" autocomplete="one-time-code" maxlength="7" (keyup.enter)="confirmRegenerate()" />
        </mat-form-field>
        <div class="actions">
          <button mat-raised-button color="primary" type="button" (click)="confirmRegenerate()" [disabled]="busy() || !code.trim()">Generar</button>
          <button mat-button type="button" (click)="cancel()">Cancelar</button>
        </div>
      </div>

      <p *ngIf="error()" class="error" role="alert">{{ error() }}</p>
    </section>
  `,
  styles: [`
    .two-factor h3 { display: flex; align-items: center; gap: 8px; margin: 0 0 8px; font-size: 1rem; }
    .muted { color: #64748b; font-size: 14px; }
    .warn { color: #b45309; }
    .error { color: #b91c1c; font-size: 14px; }
    .actions { display: flex; flex-wrap: wrap; gap: 8px; margin-top: 8px; }
    .panel { margin-top: 12px; }
    .qr { display: block; margin: 12px 0; background: #fff; padding: 8px; border-radius: 8px; }
    .secret { word-break: break-all; font-weight: 600; }
    .codes { list-style: none; display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 6px; padding: 0; }
    .codes code { font-size: 15px; }
    .full-width { width: 100%; }
    .code-field { width: 220px; max-width: 100%; }
  `]
})
// Alta, baja y renovación de códigos de recuperación de la verificación en dos pasos del profesional.
export class TwoFactorSettingsComponent implements OnInit {
  readonly status = signal<TwoFactorStatus | null>(null);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly mode = signal<Mode>('idle');
  readonly setupData = signal<TwoFactorSetup | null>(null);
  readonly recoveryCodes = signal<string[]>([]);
  readonly error = signal('');

  code = '';
  password = '';

  constructor(private twoFactor: TwoFactorService) { }

  /** Las cuentas creadas con Google no tienen contraseña local que pedir al desactivar. */
  get needsPassword(): boolean { return this.status()?.requiresPassword !== false; }

  ngOnInit(): void { this.refresh(); }

  qrDataUri(setup: TwoFactorSetup): string {
    // El SVG lo genera nuestro propio backend; se sirve como imagen de datos para que el navegador no lo ejecute como HTML.
    return 'data:image/svg+xml;base64,' + btoa(String.fromCharCode(...new TextEncoder().encode(setup.qrSvg)));
  }

  startSetup(): void {
    this.run(this.twoFactor.setup(), data => { this.setupData.set(data); this.code = ''; this.mode.set('setup'); });
  }

  confirmEnable(): void {
    this.run(this.twoFactor.enable(this.code), res => this.showCodes(res.recoveryCodes), 'El código no es válido.');
  }

  confirmDisable(): void {
    this.run(this.twoFactor.disable(this.password, this.code), () => { this.cancel(); this.refresh(); }, 'La contraseña o el código no son correctos.');
  }

  confirmRegenerate(): void {
    this.run(this.twoFactor.regenerateRecoveryCodes(this.code), res => this.showCodes(res.recoveryCodes), 'El código no es válido.');
  }

  openMode(mode: Mode): void {
    this.error.set('');
    this.code = '';
    this.password = '';
    this.mode.set(mode);
  }

  cancel(): void {
    this.mode.set('idle');
    this.setupData.set(null);
    this.recoveryCodes.set([]);
    this.code = '';
    this.password = '';
    this.error.set('');
    this.refresh();
  }

  copyCodes(): void {
    try { void navigator.clipboard?.writeText(this.recoveryCodes().join('\n')); } catch { /* El usuario puede copiarlos a mano. */ }
  }

  private showCodes(codes: string[]): void {
    this.recoveryCodes.set(codes);
    this.setupData.set(null);
    this.code = '';
    this.password = '';
    this.mode.set('codes');
  }

  private refresh(): void {
    this.twoFactor.status().subscribe({
      next: status => { this.status.set(status); this.loading.set(false); },
      error: () => { this.loading.set(false); this.error.set('No se pudo consultar el estado de la verificación en dos pasos.'); }
    });
  }

  private run<T>(request: Observable<T>, onSuccess: (value: T) => void, fallbackError = 'No se pudo completar la operación.'): void {
    this.error.set('');
    this.busy.set(true);
    request.subscribe({
      next: value => { this.busy.set(false); onSuccess(value); },
      error: (err: HttpErrorResponse) => {
        this.busy.set(false);
        this.error.set(typeof err.error === 'string' && err.error ? err.error : fallbackError);
      }
    });
  }
}
