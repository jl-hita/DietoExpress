import { HttpEvent, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { NgZone, inject } from '@angular/core';
import { Observable } from 'rxjs';

let diagnosticRequestId = 0;

type DiagnosticLevel = 'info' | 'debug' | 'error';

/** Emits metadata only: never request/response bodies or query strings. */
function diagnosticLog(level: DiagnosticLevel, message: string, details?: Record<string, unknown>): void {
  console[level](message, details ?? '');
  if (typeof window !== 'undefined') {
    window.dispatchEvent(new CustomEvent('dieto-http-cd-log', {
      detail: { time: new Date().toLocaleTimeString(), level, message, details }
    }));
  }
}

/**
 * Observes both transport events and their delivery to downstream subscribers.
 * It deliberately does not invoke ApplicationRef.tick() or alter the zone.
 */
export const zoneChangeDetectionInterceptor: HttpInterceptorFn = (request, next) => {
  inject(NgZone);
  const requestId = ++diagnosticRequestId;
  const safeUrl = request.url.split('?')[0];
  const label = `[HTTP-CD #${requestId}] ${request.method} ${safeUrl}`;
  const startedAt = Date.now();

  diagnosticLog('info', `${label} start`, { inAngularZone: NgZone.isInAngularZone() });

  return new Observable<HttpEvent<unknown>>(observer => {
    const subscription = next(request).subscribe({
      next: event => {
        if (event instanceof HttpResponse) {
          diagnosticLog('info', `${label} response`, {
            status: event.status,
            elapsedMs: Date.now() - startedAt,
            inAngularZone: NgZone.isInAngularZone()
          });
        } else {
          diagnosticLog('debug', `${label} HTTP event`, {
            type: event.type,
            elapsedMs: Date.now() - startedAt,
            inAngularZone: NgZone.isInAngularZone()
          });
        }

        diagnosticLog('debug', `${label} downstream notification begin`, {
          eventType: event instanceof HttpResponse ? 'response' : event.type,
          inAngularZone: NgZone.isInAngularZone()
        });
        observer.next(event);
        diagnosticLog('debug', `${label} downstream notification returned`, {
          elapsedMs: Date.now() - startedAt,
          inAngularZone: NgZone.isInAngularZone(),
          observerClosed: observer.closed
        });
      },
      error: error => {
        diagnosticLog('error', `${label} HTTP stream error`, {
          elapsedMs: Date.now() - startedAt,
          inAngularZone: NgZone.isInAngularZone(),
          errorName: error instanceof Error ? error.name : typeof error
        });
        observer.error(error);
      },
      complete: () => {
        diagnosticLog('debug', `${label} HTTP stream complete`, {
          elapsedMs: Date.now() - startedAt,
          inAngularZone: NgZone.isInAngularZone()
        });
        observer.complete();
      }
    });

    return () => subscription.unsubscribe();
  });
};
