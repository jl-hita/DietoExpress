import { ErrorHandler, Injectable, inject } from '@angular/core';
import { ErrorReportingService } from './error-reporting.service';

/**
 * Captura excepciones JavaScript no controladas para ofrecer el mismo flujo de soporte.
 * ErrorReportingService guarda solo metadatos genéricos, no el mensaje ni el stack trace.
 */
@Injectable()
export class ApplicationErrorHandler implements ErrorHandler {
  private readonly reporting = inject(ErrorReportingService);

  handleError(error: unknown): void {
    const path = typeof window !== 'undefined' ? window.location.pathname : '/';
    this.reporting.captureClientError(error, path);
    console.error(error);
  }
}
