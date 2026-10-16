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

  it('captures only server errors from the application API and strips query strings', () => {
    const error = new HttpErrorResponse({
      status: 500,
      statusText: 'Internal Server Error',
      headers: new HttpHeaders({ 'X-Correlation-ID': 'trace-123' })
    });

    service.captureHttpError(error, '/api/clients/42?include=private');

    expect(service.currentError()).toMatchObject({
      code: 'SERVER_ERROR',
      status: 500,
      path: '/api/clients/42',
      correlationId: 'trace-123'
    });
    expect(service.currentError()?.path).not.toContain('?');
  });

  it('does not capture client errors or support endpoint failures', () => {
    service.captureHttpError(new HttpErrorResponse({ status: 400 }), '/api/clients');
    expect(service.currentError()).toBeNull();

    service.captureHttpError(new HttpErrorResponse({ status: 503 }), '/api/support/tickets');
    expect(service.currentError()).toBeNull();
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
