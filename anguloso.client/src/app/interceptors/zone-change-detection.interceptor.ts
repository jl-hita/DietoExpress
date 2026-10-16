import { HttpInterceptorFn } from '@angular/common/http';
import { ApplicationRef, NgZone, inject } from '@angular/core';
import { Observable } from 'rxjs';

/**
 * Compatibility bridge for legacy screens that assign plain component fields
 * in HttpClient subscriptions.
 *
 * Angular's zoneless defaults can leave those views stale even when the HTTP
 * request succeeds. Re-enter the zone and run a single application-wide change
 * detection pass after downstream subscribers have handled each notification.
 * Keeping this here avoids per-component detectChanges() patches.
 */
export const zoneChangeDetectionInterceptor: HttpInterceptorFn = (request, next) => {
  const zone = inject(NgZone);
  const appRef = inject(ApplicationRef);

  const notify = (callback: () => void): void => {
    zone.run(() => {
      try {
        callback();
      } finally {
        // Run after observer.next/error/complete so subscription callbacks have
        // already updated their component state before Angular checks the view.
        appRef.tick();
      }
    });
  };

  return new Observable(observer => next(request).subscribe({
    next: event => notify(() => observer.next(event)),
    error: error => notify(() => observer.error(error)),
    complete: () => notify(() => observer.complete())
  }));
};
