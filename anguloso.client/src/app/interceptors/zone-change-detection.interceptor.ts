import { HttpEvent, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { NgZone, inject } from '@angular/core';
import { tap } from 'rxjs/operators';

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
 * Temporary HTTP diagnostics for the global view-loading issue.
 * This interceptor observes transport events only. It deliberately does not
 * invoke ApplicationRef.tick() or wrap observer callbacks: Angular owns its
 * normal change-detection lifecycle and a forced tick can fail with NG0101
 * when a tick is already in progress.
 */
export const zoneChangeDetectionInterceptor: HttpInterceptorFn = (request, next) => {
  inject(NgZone); // Keep the zone runtime initialized, but do not force a CD pass.
  const requestId = ++diagnosticRequestId;
  const safeUrl = request.url.split('?')[0];
  const label = `[HTTP-CD #${requestId}] ${request.method} ${safeUrl}`;
  const startedAt = Date.now();

  diagnosticLog('info', `${label} start`, { inAngularZone: NgZone.isInAngularZone() });

  return next(request).pipe(
    tap({
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
      },
      error: () => {
        diagnosticLog('error', `${label} HTTP stream error`, {
          elapsedMs: Date.now() - startedAt,
          inAngularZone: NgZone.isInAngularZone()
        });
      },
      complete: () => {
        diagnosticLog('debug', `${label} HTTP stream complete`, {
          elapsedMs: Date.now() - startedAt,
          inAngularZone: NgZone.isInAngularZone()
        });
      }
    })
  );
};
