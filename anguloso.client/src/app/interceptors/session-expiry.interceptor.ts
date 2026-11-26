import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { throwError } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { AuthService } from '../servicios/auth.service';

/** Peticiones del portal del paciente: su 401 no dice nada de la sesión profesional. */
function isPatientRequest(request: HttpRequest<unknown>): boolean {
  return request.headers.has('X-DietoExpress-Patient-Portal') ||
    /\/api\/(portal|messages\/patient|appointments\/(mine|slots))(\/|\?|$)/.test(request.url);
}

/**
 * Ante un 401 de la API profesional comprueba si la sesión sigue viva; si no, vuelve al acceso en lugar de dejar la
 * aplicación aparentando estar conectada.
 */
export const sessionExpiryInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 &&
          !request.url.includes('/api/auth/') && !isPatientRequest(request)) {
        auth.verifySession(() => void router.navigate(['/login']));
      }
      return throwError(() => error);
    })
  );
};
