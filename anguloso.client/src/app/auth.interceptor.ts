import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    // Las sesiones profesionales y de pacientes usan cookies HttpOnly.
    // El navegador adjunta la cookie automáticamente; JavaScript nunca accede al JWT.
    return next.handle(req);
  }
}
