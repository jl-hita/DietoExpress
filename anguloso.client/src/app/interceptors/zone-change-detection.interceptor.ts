import { HttpEvent, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { NgZone, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AuthService } from '../servicios/auth.service';
import { ErrorReportingService } from '../servicios/error-reporting.service';

let diagnosticRequestId = 0;

type DiagnosticLevel = 'info' | 'debug' | 'error';

/** Emits metadata only: never request/response bodies or query strings. */
function diagnosticLog(enabled: boolean, level: DiagnosticLevel, message: string, details?: Record<string, unknown>): void {
  if (!enabled) return;
  console[level](message, details ?? '');
  if (typeof window !== 'undefined') {
    window.dispatchEvent(new CustomEvent('dieto-http-cd-log', {
      detail: { time: new Date().toLocaleTimeString(), level, message, details }
    }));
  }
}

/**
 * Observes both transport events and their delivery to downstream subscribers.
 * Each downstream notification is delivered inside Angular's zone so ordinary component fields
 * updated by existing HttpClient subscriptions still trigger change detection.
 */
export const zoneChangeDetectionInterceptor: HttpInterceptorFn = (request, next) => {
  const zone = inject(NgZone);
  const authService = inject(AuthService);
  const errorReporting = inject(ErrorReportingService);
  const diagnosticsEnabled = (): boolean => authService.isSuperAdmin();
  const requestId = ++diagnosticRequestId;
  const safeUrl = request.url.split('?')[0];
  const label = `[HTTP-CD #${requestId}] ${request.method} ${safeUrl}`;
  const startedAt = Date.now();

  diagnosticLog(diagnosticsEnabled(), 'info', `${label} start`, { inAngularZone: NgZone.isInAngularZone() });

  return new Observable<HttpEvent<unknown>>(observer => {
    const subscription = next(request).subscribe({
      next: event => {
        if (event instanceof HttpResponse) {
          diagnosticLog(diagnosticsEnabled(), 'info', `${label} response`, {
            status: event.status,
            elapsedMs: Date.now() - startedAt,
            inAngularZone: NgZone.isInAngularZone()
          });
        } else {
          diagnosticLog(diagnosticsEnabled(), 'debug', `${label} HTTP event`, {
            type: event.type,
            elapsedMs: Date.now() - startedAt,
            inAngularZone: NgZone.isInAngularZone()
          });
        }

        diagnosticLog(diagnosticsEnabled(), 'debug', `${label} downstream notification begin`, {
          eventType: event instanceof HttpResponse ? 'response' : event.type,
          inAngularZone: NgZone.isInAngularZone()
        });
        zone.run(() => observer.next(event));
        diagnosticLog(diagnosticsEnabled(), 'debug', `${label} downstream notification returned`, {
          elapsedMs: Date.now() - startedAt,
          inAngularZone: NgZone.isInAngularZone(),
          observerClosed: observer.closed
        });
      },
      error: error => {
        errorReporting.captureHttpError(error, request.url, request.method);
        diagnosticLog(diagnosticsEnabled(), 'error', `${label} HTTP stream error`, {
          elapsedMs: Date.now() - startedAt,
          inAngularZone: NgZone.isInAngularZone(),
          errorName: error instanceof Error ? error.name : typeof error
        });
        zone.run(() => observer.error(error));
      },
      complete: () => {
        diagnosticLog(diagnosticsEnabled(), 'debug', `${label} HTTP stream complete`, {
          elapsedMs: Date.now() - startedAt,
          inAngularZone: NgZone.isInAngularZone()
        });
        zone.run(() => observer.complete());
      }
    });

    return () => subscription.unsubscribe();
  });
};
