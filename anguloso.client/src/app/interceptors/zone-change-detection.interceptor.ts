import { HttpEvent, HttpInterceptorFn, HttpResponse } from '@angular/common/http';
import { ApplicationRef, NgZone, inject } from '@angular/core';
import { Observable } from 'rxjs';

let diagnosticRequestId = 0;

/**
 * Temporary diagnostics for the global HTTP/change-detection issue.
 * Logs transport events, delivery to the application, and the subsequent
 * application-wide change-detection pass. Do not log query strings or bodies.
 */
export const zoneChangeDetectionInterceptor: HttpInterceptorFn = (request, next) => {
  const zone = inject(NgZone);
  const appRef = inject(ApplicationRef);
  const requestId = ++diagnosticRequestId;
  const label = `[HTTP-CD #${requestId}] ${request.method} ${request.url}`;
  const startedAt = Date.now();

  console.info(`${label} start`, {
    inAngularZone: NgZone.isInAngularZone(),
    appStable: appRef.isStable
  });

  const notify = (kind: string, callback: () => void): void => {
    console.debug(`${label} deliver ${kind}`, {
      inAngularZoneBefore: NgZone.isInAngularZone()
    });

    zone.run(() => {
      try {
        callback();
        console.debug(`${label} delivered ${kind}`, {
          inAngularZone: NgZone.isInAngularZone()
        });
      } catch (error) {
        console.error(`${label} downstream observer threw during ${kind}`, error);
        throw error;
      } finally {
        try {
          console.debug(`${label} change-detection tick begin`);
          appRef.tick();
          console.debug(`${label} change-detection tick end`, {
            elapsedMs: Date.now() - startedAt,
            appStable: appRef.isStable
          });
        } catch (error) {
          console.error(`${label} change-detection tick failed after ${kind}`, error);
          throw error;
        }
      }
    });
  };

  return new Observable<HttpEvent<unknown>>(observer => {
    console.debug(`${label} subscribing to HTTP event stream`);

    const subscription = next(request).subscribe({
      next: event => {
        if (event instanceof HttpResponse) {
          console.info(`${label} response`, {
            status: event.status,
            elapsedMs: Date.now() - startedAt
          });
        } else {
          console.debug(`${label} HTTP event`, {
            type: event.type,
            elapsedMs: Date.now() - startedAt
          });
        }

        notify(event instanceof HttpResponse ? `response ${event.status}` : `event ${event.type}`, () => observer.next(event));
      },
      error: error => {
        console.error(`${label} HTTP stream error`, {
          elapsedMs: Date.now() - startedAt,
          error
        });
        notify('error', () => observer.error(error));
      },
      complete: () => {
        console.info(`${label} HTTP stream complete`, {
          elapsedMs: Date.now() - startedAt
        });
        notify('complete', () => observer.complete());
      }
    });

    return () => {
      console.debug(`${label} subscription teardown`, {
        closed: subscription.closed,
        elapsedMs: Date.now() - startedAt
      });
      subscription.unsubscribe();
    };
  });
};
