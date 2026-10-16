import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { NgZone } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { zoneChangeDetectionInterceptor } from './zone-change-detection.interceptor';

describe('zoneChangeDetectionInterceptor', () => {
  let http: HttpClient;
  let httpTesting: HttpTestingController;
  let zone: NgZone;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([zoneChangeDetectionInterceptor])),
        provideHttpClientTesting()
      ]
    });
    http = TestBed.inject(HttpClient);
    httpTesting = TestBed.inject(HttpTestingController);
    zone = TestBed.inject(NgZone);
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

    expect(nextInAngularZone).toBeTrue();
    expect(completeInAngularZone).toBeTrue();
  });

  it('delivers HTTP errors inside Angular zone', () => {
    let errorInAngularZone = false;

    http.get('/api/zone-check').subscribe({
      error: () => errorInAngularZone = NgZone.isInAngularZone()
    });

    const request = httpTesting.expectOne('/api/zone-check');
    zone.runOutsideAngular(() => request.flush('failed', { status: 500, statusText: 'Server Error' }));

    expect(errorInAngularZone).toBeTrue();
  });
});
