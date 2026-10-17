import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ErrorReportingService } from './error-reporting.service';

describe('ErrorReportingService', () => {
  let service: ErrorReportingService;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(ErrorReportingService);
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('captures server errors and strips query strings', () => {
    const error = new HttpErrorResponse({
      status: 500,
      statusText: 'Internal Server Error',
      headers: new HttpHeaders({ 'X-Correlation-ID': 'trace-123' })
    });

    service.captureHttpError(error, '/api/clients/42?include=private');

    expect(service.currentError()).toMatchObject({
      code: 'SERVER_ERROR',
      status: 500,
      path: '/api/clients/:id',
      correlationId: 'trace-123'
    });
    expect(service.currentError()?.path).not.toContain('?');
    expect(service.currentError()?.path).not.toContain('42');
  });

  it('does not capture ordinary 4xx errors or support endpoint failures', () => {
    service.captureHttpError(new HttpErrorResponse({ status: 400, error: { reportable: false } }), '/api/clients');
    expect(service.currentError()).toBeNull();

    service.captureHttpError(new HttpErrorResponse({ status: 503 }), '/api/support/tickets');
    expect(service.currentError()).toBeNull();
  });

  it('captures a 4xx domain error only when the API explicitly marks it reportable', () => {
    service.captureHttpError(new HttpErrorResponse({
      status: 409,
      error: { code: 'CLIENT_CONFLICT', reportable: true, detail: 'private server detail' }
    }), '/api/clients/42?returnUrl=private');

    expect(service.currentError()).toMatchObject({
      code: 'CLIENT_CONFLICT',
      status: 409,
      path: '/api/clients/42',
      message: expect.not.stringContaining('private server detail')
    });
  });

  it('captures client runtime errors without exposing the exception message', () => {
    service.captureClientError(new Error('private user data in exception'), '/clients/42?token=private#section');

    expect(service.currentError()).toMatchObject({
      code: 'CLIENT_RUNTIME_ERROR',
      status: 0,
      path: '/clients/:id',
      message: 'Se ha producido un error inesperado en la aplicación.'
    });
    expect(service.currentError()?.message).not.toContain('private user data');
  });

  it('does not capture HTTP errors through the client runtime handler', () => {
    service.captureClientError(new HttpErrorResponse({ status: 500 }), '/clients');

    expect(service.currentError()).toBeNull();
  });


  it('redacts numeric and UUID resource identifiers from paths', () => {
    service.captureHttpError(
      new HttpErrorResponse({ status: 500 }),
      '/api/clients/42/diets/550e8400-e29b-41d4-a716-446655440000?include=private'
    );

    expect(service.currentError()?.path).toBe('/api/clients/:id/diets/:id');
    expect(service.currentError()?.path).not.toContain('42');
    expect(service.currentError()?.path).not.toContain('550e8400');
  });

  it('deduplicates the same status and path for 30 seconds', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-16T12:00:00.000Z'));

    service.captureHttpError(new HttpErrorResponse({ status: 500 }), '/api/clients');
    const firstId = service.currentError()?.id;

    vi.advanceTimersByTime(10_000);
    service.captureHttpError(new HttpErrorResponse({ status: 500 }), '/api/clients?refresh=true');
    expect(service.currentError()?.id).toBe(firstId);

    vi.advanceTimersByTime(20_001);
    service.captureHttpError(new HttpErrorResponse({ status: 500 }), '/api/clients');
    expect(service.currentError()?.id).not.toBe(firstId);
  });

  it('allows a new error from a different route during the deduplication window', () => {
    vi.useFakeTimers();
    service.captureHttpError(new HttpErrorResponse({ status: 500 }), '/api/clients');
    const firstId = service.currentError()?.id;

    service.captureHttpError(new HttpErrorResponse({ status: 500 }), '/api/diets');

    expect(service.currentError()?.id).not.toBe(firstId);
    expect(service.currentError()?.path).toBe('/api/diets');
  });
});
