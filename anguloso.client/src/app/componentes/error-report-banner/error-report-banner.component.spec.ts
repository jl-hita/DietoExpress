import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ErrorReportBannerComponent } from './error-report-banner.component';
import { AuthService } from '../../servicios/auth.service';
import { SupportService } from '../../servicios/support.service';
import { ErrorReportingService } from '../../servicios/error-reporting.service';

describe('ErrorReportBannerComponent', () => {
  let fixture: ComponentFixture<ErrorReportBannerComponent>;
  let auth: { isLoggedIn: ReturnType<typeof vi.fn>; isSuperAdmin: ReturnType<typeof vi.fn> };
  let support: { createTicket: ReturnType<typeof vi.fn> };
  let errors: ErrorReportingService;

  beforeEach(() => {
    auth = { isLoggedIn: vi.fn().mockReturnValue(true), isSuperAdmin: vi.fn().mockReturnValue(false) };
    support = { createTicket: vi.fn().mockReturnValue(of({ id: 42 })) };
    vi.spyOn(window, 'alert').mockImplementation(() => undefined);
    TestBed.configureTestingModule({
      imports: [ErrorReportBannerComponent],
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: SupportService, useValue: support }
      ]
    });
    fixture = TestBed.createComponent(ErrorReportBannerComponent);
    errors = TestBed.inject(ErrorReportingService);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  function captureError(): void {
    errors.captureHttpError(
      new HttpErrorResponse({
        status: 503,
        headers: new HttpHeaders({ 'X-Correlation-ID': 'trace-safe-123' }),
        error: { code: 'VIDEO_PROVIDER_UNAVAILABLE', reportable: true, detail: 'sensitive provider payload' }
      }),
      '/api/appointments/online?clientId=private'
    );
    fixture.detectChanges();
  }

  it('offers ticket creation to professional users with only sanitized technical metadata', () => {
    captureError();
    expect(fixture.nativeElement.textContent).toContain('Informar a soporte');
    fixture.nativeElement.querySelector('button').click();

    expect(support.createTicket).toHaveBeenCalledOnce();
    const ticket = support.createTicket.mock.calls[0][0];
    expect(ticket.subject).toContain('Error de aplicación');
    expect(ticket.body).toContain('VIDEO_PROVIDER_UNAVAILABLE');
    expect(ticket.body).toContain('/api/appointments/online');
    expect(ticket.body).toContain('trace-safe-123');
    expect(ticket.body).toContain('Fecha UTC: ');
    expect(ticket.body).toContain('Fecha local (con zona horaria): ');
    expect(ticket.body).toContain('Zona horaria IANA: ');
    expect(ticket.body).toMatch(/Fecha UTC: \d{4}-\d{2}-\d{2}T[^\n]+Z/);
    expect(ticket.body).not.toContain('private');
    expect(ticket.body).not.toContain('sensitive provider payload');
    expect(errors.currentError()).toBeNull();
  });

  it('does not show an action that the superadmin API role is forbidden to use', () => {
    auth.isSuperAdmin.mockReturnValue(true);
    captureError();

    expect(fixture.nativeElement.textContent).not.toContain('Informar a soporte');
    expect(support.createTicket).not.toHaveBeenCalled();
  });


  it('does not offer ticket creation when no professional session is authenticated', () => {
    auth.isLoggedIn.mockReturnValue(false);
    captureError();

    expect(fixture.nativeElement.textContent).not.toContain('Informar a soporte');
    expect(support.createTicket).not.toHaveBeenCalled();
  });

  it('keeps the error visible and offers retry when ticket creation fails', () => {
    support.createTicket.mockReturnValue(throwError(() => new Error('private failure')));
    captureError();
    fixture.nativeElement.querySelector('button').click();
    fixture.detectChanges();

    expect(errors.currentError()).not.toBeNull();
    expect(fixture.nativeElement.textContent).toContain('vuelve a intentarlo');
    expect(fixture.nativeElement.textContent).not.toContain('private failure');
  });
});
