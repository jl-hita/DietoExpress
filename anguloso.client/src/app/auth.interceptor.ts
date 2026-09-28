import { Injectable } from '@angular/core';
import { HttpInterceptor, HttpRequest, HttpHandler, HttpEvent } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  intercept(req: HttpRequest<any>, next: HttpHandler): Observable<HttpEvent<any>> {
    const nutritionistToken = localStorage.getItem('auth_token');
    const patientToken = localStorage.getItem('patient_auth_token');

    // El login del paciente mediante enlace mágico no necesita JWT.
    // Además, nunca debemos enviar aquí un token de nutricionista: si se abre
    // el enlace mágico en el mismo navegador, el token de la sesión profesional
    // no puede sustituir al token del paciente.
    if (req.url.includes('/portal/auth')) {
      return next.handle(req);
    }

    let token: string | null;

    if (req.url.includes('/portal/') && !req.urlWithParams.includes('clientId=')) {
      // Portal del paciente: priorizar siempre su token.
      token = patientToken;
    } else {
      // Vista previa desde la ficha del nutricionista u otras peticiones.
      token = nutritionistToken || patientToken;
    }

    if (token) {
      const cloned = req.clone({
        setHeaders: {
          Authorization: `Bearer ${token}`
        }
      });
      return next.handle(cloned);
    }

    return next.handle(req);
  }
}
