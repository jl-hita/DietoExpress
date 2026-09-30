import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const nutritionistToken = localStorage.getItem('auth_token');

    // Las sesiones del paciente viven en una cookie HttpOnly y el navegador
    // la envía automáticamente. Nunca exponemos ese JWT a JavaScript.
    if (req.url.includes('/portal/') && !req.urlWithParams.includes('clientId=')) {
      return next.handle(req);
    }

    if (nutritionistToken) {
      return next.handle(req.clone({
        setHeaders: { Authorization: `Bearer ${nutritionistToken}` }
      }));
    }

    return next.handle(req);
  }
}
