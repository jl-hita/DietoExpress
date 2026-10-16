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

interface ApiProblemMetadata {
  code?: unknown;
  reportable?: unknown;
}

@Injectable({ providedIn: 'root' })
export class ErrorReportingService {
  readonly currentError = signal<ReportableApplicationError | null>(null);
  private lastFingerprint = '';
  private lastCapturedAt = 0;
  private sequence = 0;

  /**
   * Captura errores de servidor y errores 4xx marcados explícitamente como reportables
   * por la API. No conserva cuerpos, parámetros de consulta, tokens ni datos del usuario.
   */
  captureHttpError(error: unknown, requestUrl: string): void {
    if (!(error instanceof HttpErrorResponse)) return;
    if (!requestUrl.startsWith('/api/') || requestUrl.startsWith('/api/support/')) return;

    const problem = this.getProblemMetadata(error.error);
    const isServerError = error.status >= 500;
    const explicitlyReportable = error.status >= 400 && error.status < 500 && problem?.reportable === true;
    if (!isServerError && !explicitlyReportable) return;

    const path = requestUrl.split('?')[0];
    const fingerprint = error.status + ':' + path;
    const now = Date.now();
    if (fingerprint === this.lastFingerprint && now - this.lastCapturedAt < 30_000) return;

    this.lastFingerprint = fingerprint;
    this.lastCapturedAt = now;
    this.sequence += 1;
    const correlationId = error.headers?.get('X-Correlation-ID') ?? undefined;
    const code = typeof problem?.code === 'string' && /^[A-Z][A-Z0-9_]{0,63}$/.test(problem.code)
      ? problem.code
      : error.status === 503 ? 'SERVICE_UNAVAILABLE'
      : isServerError ? 'SERVER_ERROR'
      : 'REPORTABLE_REQUEST_ERROR';

    this.currentError.set({
      id: 'DEX-' + now.toString(36).toUpperCase() + '-' + this.sequence.toString(36).toUpperCase(),
      code,
      message: error.status === 503
        ? 'El servicio no está disponible temporalmente.'
        : isServerError
          ? 'No se ha podido completar la operación.'
          : 'La operación no se ha podido completar. Puedes informar a soporte si crees que se trata de un error.',
      occurredAt: new Date(now).toISOString(),
      status: error.status,
      path,
      ...(correlationId ? { correlationId } : {})
    });
  }

  dismiss(): void {
    this.currentError.set(null);
  }

  private getProblemMetadata(value: unknown): ApiProblemMetadata | undefined {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) return undefined;
    return value as ApiProblemMetadata;
  }
}
