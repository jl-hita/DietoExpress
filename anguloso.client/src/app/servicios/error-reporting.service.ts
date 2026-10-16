import { Injectable, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';

export interface ReportableApplicationError {
  id: string;
  code: string;
  message: string;
  occurredAt: string;
  status: number;
  path: string;
  correlationId?: string;
}

@Injectable({ providedIn: 'root' })
export class ErrorReportingService {
  readonly currentError = signal<ReportableApplicationError | null>(null);
  private lastFingerprint = '';
  private lastCapturedAt = 0;
  private sequence = 0;

  /**
   * Captura únicamente errores de servidor en rutas propias de la API.
   * No conserva cuerpos, parámetros de consulta, tokens ni datos del usuario.
   */
  captureHttpError(error: unknown, requestUrl: string): void {
    if (!(error instanceof HttpErrorResponse) || error.status < 500) return;
    if (!requestUrl.startsWith('/api/') || requestUrl.startsWith('/api/support/')) return;

    const path = requestUrl.split('?')[0];
    const fingerprint = error.status + ':' + path;
    const now = Date.now();
    if (fingerprint === this.lastFingerprint && now - this.lastCapturedAt < 30_000) return;

    this.lastFingerprint = fingerprint;
    this.lastCapturedAt = now;
    this.sequence += 1;
    const correlationId = error.headers?.get('X-Correlation-ID') ?? undefined;
    this.currentError.set({
      id: 'DEX-' + now.toString(36).toUpperCase() + '-' + this.sequence.toString(36).toUpperCase(),
      code: error.status === 503 ? 'SERVICE_UNAVAILABLE' : 'SERVER_ERROR',
      message: error.status === 503
        ? 'El servicio no está disponible temporalmente.'
        : 'No se ha podido completar la operación.',
      occurredAt: new Date(now).toISOString(),
      status: error.status,
      path,
      ...(correlationId ? { correlationId } : {})
    });
  }

  dismiss(): void {
    this.currentError.set(null);
  }
}
