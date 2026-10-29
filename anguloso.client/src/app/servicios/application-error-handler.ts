import { HttpClient } from '@angular/common/http';
import { ErrorHandler, Injectable, inject } from '@angular/core';
import { ErrorReportingService } from './error-reporting.service';

interface ClientRuntimeDiagnostic {
  eventId: string;
  path: string;
  occurredAtUtc: string;
  errorName: string;
  errorMessage: string;
  stackTrace: string;
}

/**
 * Captures uncaught JavaScript exceptions. Only a redacted message and a bounded stack trace
 * are sent to the authenticated diagnostics endpoint; request bodies and form data are never sent.
 */
@Injectable()
export class ApplicationErrorHandler implements ErrorHandler {
  private readonly reporting = inject(ErrorReportingService);
  private readonly http = inject(HttpClient);

  private readonly recentDiagnostics = new Map<string, number>();
  private readonly diagnosticSendTimes: number[] = [];

  handleError(error: unknown): void {
    const currentPath = typeof window !== 'undefined' ? window.location.pathname : '/';
    const diagnostic = this.toDiagnostic(error, currentPath);
    if (!this.shouldReport(diagnostic)) return;

    const technicalDetails = [
      'Tipo: ' + diagnostic.errorName,
      'Mensaje depurado: ' + diagnostic.errorMessage,
      'Stack trace (depurado):',
      diagnostic.stackTrace || 'No disponible'
    ].join('\n');

    this.reporting.captureClientError(error, currentPath, diagnostic.eventId, technicalDetails);
    console.error('[DietoExpress client runtime error]', error);

    this.http.post('/api/client-diagnostics/runtime-error', diagnostic).subscribe({
      // Diagnostic reporting must never trigger a second visible error or recursive reporting.
      error: () => undefined
    });
  }

  /** Prevent a rendering/runtime loop from flooding logs, the API, and browser resources. */
  private shouldReport(diagnostic: ClientRuntimeDiagnostic): boolean {
    const now = Date.now();
    const key = [diagnostic.path, diagnostic.errorName, diagnostic.errorMessage, diagnostic.stackTrace.split('\n')[1] ?? ''].join('|');
    const previous = this.recentDiagnostics.get(key);
    if (previous !== undefined && now - previous < 30_000) return false;

    for (const [existingKey, timestamp] of this.recentDiagnostics) {
      if (now - timestamp >= 60_000) this.recentDiagnostics.delete(existingKey);
    }
    if (this.recentDiagnostics.size >= 100) {
      const oldestKey = this.recentDiagnostics.keys().next().value;
      if (oldestKey !== undefined) this.recentDiagnostics.delete(oldestKey);
    }

    while (this.diagnosticSendTimes.length && now - this.diagnosticSendTimes[0] >= 60_000) {
      this.diagnosticSendTimes.shift();
    }
    if (this.diagnosticSendTimes.length >= 5) return false;

    this.recentDiagnostics.set(key, now);
    this.diagnosticSendTimes.push(now);
    return true;
  }

  private toDiagnostic(error: unknown, currentPath: string): ClientRuntimeDiagnostic {
    const candidate = error instanceof Error
      ? error
      : error && typeof error === 'object'
        ? error as { name?: unknown; message?: unknown; stack?: unknown }
        : { name: typeof error, message: String(error), stack: '' };

    const errorName = this.safeText(candidate.name, 100) || 'UnknownError';
    const errorMessage = this.safeText(candidate.message, 500) || 'No exception message';
    const stackTrace = this.safeText(candidate.stack, 6000, true)
      .split('\n')
      .slice(0, 20)
      .join('\n');

    return {
      eventId: 'DEX-CLIENT-' + Date.now().toString(36).toUpperCase(),
      path: this.safePath(currentPath),
      occurredAtUtc: new Date().toISOString(),
      errorName,
      errorMessage,
      stackTrace
    };
  }

  private safePath(value: string): string {
    const path = value.split('?')[0].split('#')[0] || '/';
    return path.replace(/\/(?:\d+|[0-9a-f]{8}-[0-9a-f]{4}-[1-8][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12})(?=\/|$)/gi, '/:id');
  }

  private safeText(value: unknown, maxLength: number, preserveNewlines = false): string {
    if (typeof value !== 'string') return '';
    const normalized = preserveNewlines
      ? value.replace(/\r/g, '').replace(/\t/g, ' ')
      : value.replace(/[\r\n\t]+/g, ' ');
    return normalized
      .replace(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi, '[email]')
      .replace(/\b\d{7,}\b/g, '[number]')
      .replace(/(token|password|authorization|cookie)\s*[:=]\s*\S+/gi, '$1=[redacted]')
      .replace(/https?:\/\/[^\s)]+/gi, url => url.split(/[?#]/)[0])
      .slice(0, maxLength);
  }
}
