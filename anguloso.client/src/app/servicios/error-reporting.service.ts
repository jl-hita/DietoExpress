import { Injectable, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { TimeZonePreferenceService } from './time-zone-preference.service';

export interface ReportableApplicationError {
  id: string;
  code: string;
  message: string;
  occurredAt: string;
  occurredAtLocal: string;
  timeZoneId: string;
  status: number;
  path: string;
  correlationId?: string;
}

interface ApiProblemMetadata {
  code?: unknown;
  reportable?: unknown;
  correlationId?: unknown;
}

@Injectable({ providedIn: 'root' })
export class ErrorReportingService {
  constructor(private readonly timeZones: TimeZonePreferenceService) {}

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
    if (!requestUrl.startsWith('/api/') || requestUrl.startsWith('/api/support/')
      || requestUrl.startsWith('/api/client-diagnostics/')) return;

    const problem = this.getProblemMetadata(error.error);
    const isServerError = error.status >= 500;
    const explicitlyReportable = error.status >= 400 && error.status < 500 && problem?.reportable === true;
    if (!isServerError && !explicitlyReportable) return;

    const path = this.sanitizePath(requestUrl);
    const fingerprint = error.status + ':' + path;
    const now = Date.now();
    if (fingerprint === this.lastFingerprint && now - this.lastCapturedAt < 30_000) return;

    this.lastFingerprint = fingerprint;
    this.lastCapturedAt = now;
    this.sequence += 1;
    const correlationId = this.getSafeCorrelationId(
      error.headers?.get('X-Correlation-ID') ?? problem?.correlationId
    );
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
      occurredAtLocal: this.timeZones.formatLocalTimestamp(new Date(now)),
      timeZoneId: this.timeZones.timeZoneId(),
      status: error.status,
      path,
      ...(correlationId ? { correlationId } : {})
    });
  }

  /**
   * Captura errores JavaScript no controlados sin conservar el mensaje de excepción,
   * que puede contener datos internos o del usuario. El diagnóstico técnico se envía
   * por separado al endpoint autenticado; los errores HTTP se procesan en el interceptor.
   */
  captureClientError(error: unknown, currentPath: string): void {
    if (error instanceof HttpErrorResponse) return;

    const path = this.sanitizePath(currentPath);
    const fingerprint = 'CLIENT_RUNTIME_ERROR:' + path;
    const now = Date.now();
    if (fingerprint === this.lastFingerprint && now - this.lastCapturedAt < 30_000) return;

    this.lastFingerprint = fingerprint;
    this.lastCapturedAt = now;
    this.sequence += 1;
    this.currentError.set({
      id: 'DEX-' + now.toString(36).toUpperCase() + '-' + this.sequence.toString(36).toUpperCase(),
      code: 'CLIENT_RUNTIME_ERROR',
      message: 'Se ha producido un error inesperado en la aplicación.',
      occurredAt: new Date(now).toISOString(),
      occurredAtLocal: this.timeZones.formatLocalTimestamp(new Date(now)),
      timeZoneId: this.timeZones.timeZoneId(),
      status: 0,
      path
    });
  }

  dismiss(): void {
    this.currentError.set(null);
  }

  private sanitizePath(value: string): string {
    const path = value.split('?')[0].split('#')[0] || '/';
    return path.replace(/\\/(?:\\d+|[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12})(?=\\/|$)/gi, '/:id');
  }


  private getSafeCorrelationId(value: unknown): string | undefined {
    return typeof value === 'string' && /^[A-Za-z0-9._:-]{1,128}$/.test(value)
      ? value
      : undefined;
  }

  private getProblemMetadata(value: unknown): ApiProblemMetadata | undefined {
    if (typeof value !== 'object' || value === null || Array.isArray(value)) return undefined;
    return value as ApiProblemMetadata;
  }
}
