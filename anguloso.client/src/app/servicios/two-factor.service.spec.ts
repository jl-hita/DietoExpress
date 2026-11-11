import { beforeEach, afterEach, describe, expect, it } from "vitest";
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { TwoFactorService, isTwoFactorChallenge } from './two-factor.service';

describe('TwoFactorService', () => {
    let service: TwoFactorService;
    let httpTesting: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
        service = TestBed.inject(TwoFactorService);
        httpTesting = TestBed.inject(HttpTestingController);
    });

    afterEach(() => httpTesting.verify());

    it('completes the login with the challenge and the code, never sending credentials', () => {
        service.verifyLogin('challenge-token', '123456').subscribe();

        const req = httpTesting.expectOne('/api/auth/login/2fa');
        expect(req.request.method).toBe('POST');
        expect(req.request.body).toEqual({ challengeToken: 'challenge-token', code: '123456' });
        req.flush({ username: 'ana', role: 'nutritionist' });
    });

    it('requires password and code to disable', () => {
        service.disable('secreto-largo-123', '654321').subscribe();

        const req = httpTesting.expectOne('/api/auth/2fa/disable');
        expect(req.request.body).toEqual({ password: 'secreto-largo-123', code: '654321' });
        req.flush(null, { status: 204, statusText: 'No Content' });
    });

    it('recognizes a two-factor challenge only when the backend asks for it', () => {
        expect(isTwoFactorChallenge({ requiresTwoFactor: true, challengeToken: 'abc' })).toBe(true);
        expect(isTwoFactorChallenge({ requiresTwoFactor: true })).toBe(false);
        expect(isTwoFactorChallenge({ username: 'ana', role: 'nutritionist' })).toBe(false);
        expect(isTwoFactorChallenge(null)).toBe(false);
    });
});
