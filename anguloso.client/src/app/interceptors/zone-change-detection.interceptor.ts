import { HttpEvent, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { ApplicationRef, NgZone, inject } from '@angular/core';
import { Observable } from 'rxjs';

let diagnosticRequestId = 0;

type DiagnosticLevel = 'info' | 'debug' | 'error';

function describeError(error: unknown): Record<string, unknown> {
  if (error instanceof Error) {
    return { name: error.name, message: error.message, stack: error.stack };
  }
  return { value: String(error) };
}

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
 * Temporary diagnostics for the global HTTP/change-detection issue.
 * Logs transport events, delivery to the application, and the subsequent
 * application-wide change-detection pass. Do not log query strings or bodies.
 */
export const zoneChangeDetectionInterceptor: HttpInterceptorFn = (request, next) => {
  const zone = inject(NgZone);
  const appRef = inject(ApplicationRef);
  const requestId = ++diagnosticRequestId;
  const safeUrl = request.url.split('?')[0];
  const label = `[HTTP-CD #${requestId}] ${request.method} ${safeUrl}`;
  const startedAt = Date.now();

  diagnosticLog('info', `${label} start`, { inAngularZone: NgZone.isInAngularZone() });

  const notify = (kind: string, callback: () => void): void => {
    diagnosticLog('debug', `${label} deliver ${kind}`, {
      inAngularZoneBefore: NgZone.isInAngularZone()
    });

    let callbackFailed = false;
    let callbackError: unknown;

    zone.run(() => {
      try {
        callback();
        diagnosticLog('debug', `${label} delivered ${kind}`, {
          inAngularZone: NgZone.isInAngularZone()
        });
      } catch (error) {
        callbackFailed = true;
        callbackError = error;
        diagnosticLog('error', `${label} downstream observer threw during ${kind}`, describeError(error));
      }

      try {
        diagnosticLog('debug', `${label} change-detection tick begin`);
        appRef.tick();
        diagnosticLog('debug', `${label} change-detection tick end`, {
          elapsedMs: Date.now() - startedAt
        });
      } catch (error) {
        diagnosticLog('error', `${label} change-detection tick failed after ${kind}`, describeError(error));
        if (!callbackFailed) {
          callbackFailed = true;
          callbackError = error;
        }
      }
    });

    if (callbackFailed) {
      throw callbackError;
    }
  };

  return new Observable<HttpEvent<unknown>>(observer => {
    diagnosticLog('debug', `${label} subscribing to HTTP event stream`);

    const subscription = next(request).subscribe({
      next: event => {
        if (event instanceof HttpResponse) {
          diagnosticLog('info', `${label} response`, {
            status: event.status,
            elapsedMs: Date.now() - startedAt
          });
        } else {
          diagnosticLog('debug', `${label} HTTP event`, {
            type: event.type,
            elapsedMs: Date.now() - startedAt
          });
        }

        notify(event instanceof HttpResponse ? `response ${event.status}` : `event ${event.type}`, () => observer.next(event));
      },
      error: error => {
        diagnosticLog('error', `${label} HTTP stream error`, {
          elapsedMs: Date.now() - startedAt
        });
        notify('error', () => observer.error(error));
      },
      complete: () => {
        diagnosticLog('info', `${label} HTTP stream complete`, {
          elapsedMs: Date.now() - startedAt
        });
        notify('complete', () => observer.complete());
      }
    });

    return () => {
      diagnosticLog('debug', `${label} subscription teardown`, {
        closed: subscription.closed,
        elapsedMs: Date.now() - startedAt
      });
      subscription.unsubscribe();
    };
  });
};
