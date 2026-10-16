import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { NgZone } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../servicios/auth.service';
import { zoneChangeDetectionInterceptor } from './zone-change-detection.interceptor';

describe('zoneChangeDetectionInterceptor', () => {
    let http: HttpClient;
    let httpTesting: HttpTestingController;
    let zone: NgZone;
    let auth: AuthService;

    beforeEach(() => {
        vi.spyOn(console, 'info').mockReturnValue(undefined);
        vi.spyOn(console, 'debug').mockReturnValue(undefined);
        TestBed.configureTestingModule({
            providers: [
                provideHttpClient(withInterceptors([zoneChangeDetectionInterceptor])),
                provideHttpClientTesting()
            ]
        });
        http = TestBed.inject(HttpClient);
        httpTesting = TestBed.inject(HttpTestingController);
        zone = TestBed.inject(NgZone);
        auth = TestBed.inject(AuthService);
    });

    afterEach(() => httpTesting.verify());

    it('delivers successful HTTP notifications inside Angular zone', () => {
        let nextInAngularZone = false;
        let completeInAngularZone = false;

        http.get('/api/zone-check').subscribe({
            next: () => nextInAngularZone = NgZone.isInAngularZone(),
            complete: () => completeInAngularZone = NgZone.isInAngularZone()
        });

        const request = httpTesting.expectOne('/api/zone-check');
        zone.runOutsideAngular(() => request.flush({ ok: true }));

        expect(nextInAngularZone).toBe(true);
        expect(completeInAngularZone).toBe(true);
    });

    it('does not emit diagnostic logs for non-superadmin sessions', () => {
        http.get('/api/zone-check').subscribe();
        httpTesting.expectOne('/api/zone-check').flush({ ok: true });

        expect(console.info).not.toHaveBeenCalled();
        expect(console.debug).not.toHaveBeenCalled();
    });

    it('emits diagnostic logs for superadmin sessions', () => {
        auth.login({ username: 'admin', role: 'superadmin' });
        http.get('/api/zone-check').subscribe();
        httpTesting.expectOne('/api/zone-check').flush({ ok: true });

        expect(console.info).toHaveBeenCalled();
        expect(console.debug).toHaveBeenCalled();
    });

    it('delivers HTTP errors inside Angular zone', () => {
        vi.spyOn(console, 'error').mockReturnValue(undefined);
        let errorInAngularZone = false;

        http.get('/api/zone-check').subscribe({
            error: () => errorInAngularZone = NgZone.isInAngularZone()
        });

        const request = httpTesting.expectOne('/api/zone-check');
        zone.runOutsideAngular(() => request.flush('failed', { status: 500, statusText: 'Server Error' }));

        expect(errorInAngularZone).toBe(true);
    });
});
