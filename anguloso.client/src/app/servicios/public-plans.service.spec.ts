import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';

import { PLAN_FEATURE_LABELS, PublicPlansService, planFeatureLabels, yearlySavingsPercent } from './public-plans.service';

describe('yearlySavingsPercent', () => {
    it('computes the discount of paying yearly against twelve monthly payments', () => {
        expect(yearlySavingsPercent({ monthlyPrice: 29.9, yearlyPrice: 299 })).toBe(17);
        expect(yearlySavingsPercent({ monthlyPrice: 79.9, yearlyPrice: 799 })).toBe(17);
    });

    it('returns 0 when yearly billing is not cheaper or prices are missing', () => {
        expect(yearlySavingsPercent({ monthlyPrice: 10, yearlyPrice: 120 })).toBe(0);
        expect(yearlySavingsPercent({ monthlyPrice: 10, yearlyPrice: 150 })).toBe(0);
        expect(yearlySavingsPercent({ monthlyPrice: 0, yearlyPrice: 100 })).toBe(0);
        expect(yearlySavingsPercent({ monthlyPrice: 10, yearlyPrice: 0 })).toBe(0);
    });
});

describe('planFeatureLabels', () => {
    it('shows only enabled features, in display order, and ignores unknown codes', () => {
        const labels = planFeatureLabels({ features: ['RECIPES', 'UNKNOWN_FEATURE', 'CLIENT_PORTAL'] });
        expect(labels).toEqual(['Portal del paciente y app Android', 'Recetario propio']);
    });

    it('has a label for every feature code the backend can enable', () => {
        const codes = PLAN_FEATURE_LABELS.map(f => f.code);
        for (const code of ['CLIENT_PORTAL', 'PDF_EXPORT', 'RECIPES', 'DIET_TEMPLATES', 'SHARED_DIETS', 'MULTI_NUTRITIONIST', 'CLINIC_DASHBOARD', 'CLIENT_ASSIGNMENT']) {
            expect(codes).toContain(code);
        }
    });
});

describe('PublicPlansService', () => {
    let service: PublicPlansService;
    let httpTesting: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
        service = TestBed.inject(PublicPlansService);
        httpTesting = TestBed.inject(HttpTestingController);
    });

    afterEach(() => httpTesting.verify());

    it('reads the public catalog without credentials', () => {
        let result: unknown;
        service.getPlans().subscribe(r => result = r);

        const req = httpTesting.expectOne('/api/public/plans');
        expect(req.request.method).toBe('GET');
        req.flush({ plans: [] });
        expect(result).toEqual({ plans: [] });
    });
});
