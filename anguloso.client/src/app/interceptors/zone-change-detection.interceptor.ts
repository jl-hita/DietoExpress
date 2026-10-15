import { HttpInterceptorFn } from '@angular/common/http';
import { NgZone, inject } from '@angular/core';
import { Observable } from 'rxjs';

/**
 * Compatibility bridge for the existing subscription-based screens.
 *
 * Keep HttpClient notifications inside Angular's zone so components that update
 * ordinary fields in subscribe() trigger change detection without needing
 * ChangeDetectorRef.detectChanges() in each view.
 */
export const zoneChangeDetectionInterceptor: HttpInterceptorFn = (request, next) => {
  const zone = inject(NgZone);

  return new Observable(observer => next(request).subscribe({
    next: event => zone.run(() => observer.next(event)),
    error: error => zone.run(() => observer.error(error)),
    complete: () => zone.run(() => observer.complete())
  }));
};
