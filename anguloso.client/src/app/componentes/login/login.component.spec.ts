import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { LoginComponent } from './login.component';
import { flushPendingHttpRequests } from '../../testing/http-test-flusher';

describe('LoginComponent', () => {
    let component: LoginComponent;
    let fixture: ComponentFixture<LoginComponent>;
    let httpTesting: HttpTestingController;
    let originalGoogle: unknown;

    beforeEach(async () => {
        await TestBed.configureTestingModule({
            imports: [LoginComponent],
            providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
        })
            .compileComponents();

        originalGoogle = (window as any).google;
        (window as any).google = { accounts: { id: { initialize: () => undefined, renderButton: () => undefined } } };
        fixture = TestBed.createComponent(LoginComponent);
        component = fixture.componentInstance;
        httpTesting = TestBed.inject(HttpTestingController);
        fixture.detectChanges();
        flushPendingHttpRequests(httpTesting, fixture);
    });

    afterEach(() => {
        httpTesting.verify();
        if (originalGoogle === undefined)
            delete (window as any).google;
        else
            (window as any).google = originalGoogle;
    });

    it('should create', () => {
        expect(component).toBeTruthy();
    });

    it('asks for the second factor instead of opening a session when the account has 2FA', () => {
        component.form.setValue({ username: 'ana', password: 'una-contrasena-larga' });
        component.login();

        httpTesting.expectOne(request => request.url.endsWith('/auth/login')).flush({ requiresTwoFactor: true, challengeToken: 'challenge-1' });
        fixture.detectChanges();

        expect(component.challengeToken).toBe('challenge-1');
        expect(fixture.nativeElement.querySelector('input[name="twoFactorCode"]')).toBeTruthy();
        expect(fixture.nativeElement.querySelector('input[formControlName="password"]')).toBeFalsy();
    });

    it('sends the challenge and the code to the second step and then leaves the 2FA state', () => {
        component.form.setValue({ username: 'ana', password: 'una-contrasena-larga' });
        component.login();
        httpTesting.expectOne(request => request.url.endsWith('/auth/login')).flush({ requiresTwoFactor: true, challengeToken: 'challenge-1' });

        component.twoFactorCode = '123456';
        component.verifyTwoFactor();
        const req = httpTesting.expectOne('/api/auth/login/2fa');
        expect(req.request.body).toEqual({ challengeToken: 'challenge-1', code: '123456' });
        req.flush({ username: 'ana', role: 'nutritionist' });

        expect(component.challengeToken).toBeNull();
    });

    it('returns to the password step when the challenge has expired', () => {
        component.form.setValue({ username: 'ana', password: 'una-contrasena-larga' });
        component.login();
        httpTesting.expectOne(request => request.url.endsWith('/auth/login')).flush({ requiresTwoFactor: true, challengeToken: 'challenge-1' });

        component.twoFactorCode = '123456';
        component.verifyTwoFactor();
        httpTesting.expectOne('/api/auth/login/2fa').flush('La verificación ha caducado. Inicia sesión de nuevo.', { status: 401, statusText: 'Unauthorized' });

        expect(component.challengeToken).toBeNull();
    });
});
