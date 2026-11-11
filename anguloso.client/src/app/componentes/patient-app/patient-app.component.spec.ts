import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';

import { PATIENT_APP_ROUTES } from './patient-app.routes';
import { PatientAppShellComponent } from './patient-app-shell.component';
import { PatientHomeComponent } from './patient-home.component';

describe('Patient app (new interface)', () => {
    let httpTesting: HttpTestingController;

    beforeEach(() => {
        TestBed.configureTestingModule({
            providers: [provideRouter([{ path: 'portal', children: PATIENT_APP_ROUTES }, { path: 'patient', children: [] }]), provideHttpClient(), provideHttpClientTesting()]
        });
        httpTesting = TestBed.inject(HttpTestingController);
    });

    afterEach(() => httpTesting.verify());

    it('shows the five Android navigation entries', () => {
        const fixture = TestBed.createComponent(PatientAppShellComponent);
        fixture.detectChanges();
        httpTesting.expectOne('/api/portal/profile').flush({ clientId: 1, fullName: 'Laura Gómez' });
        fixture.detectChanges();

        const labels = [...fixture.nativeElement.querySelectorAll('.pa-nav a span')].map((e: Element) => e.textContent?.trim());
        expect(labels).toEqual(['Inicio', 'Mi dieta', 'Entrenamiento', 'Progreso', 'Citas']);
    });

    it('sends a visitor without a patient session to the current portal', () => {
        const router = TestBed.inject(Router);
        const targets: unknown[] = [];
        const navigate = router.navigate.bind(router);
        router.navigate = ((commands: unknown[]) => { targets.push(commands); return navigate(commands as never); }) as typeof router.navigate;

        const fixture = TestBed.createComponent(PatientAppShellComponent);
        fixture.detectChanges();
        httpTesting.expectOne('/api/portal/profile').flush('', { status: 401, statusText: 'Unauthorized' });

        expect(targets).toContainEqual(['/patient']);
    });

    it('greets by first name and shows the next training and appointment', () => {
        const shell = TestBed.createComponent(PatientAppShellComponent);
        shell.detectChanges();
        httpTesting.expectOne('/api/portal/profile').flush({ clientId: 1, fullName: 'Laura Gómez' });

        const fixture = TestBed.createComponent(PatientHomeComponent);
        fixture.detectChanges();
        const tomorrow = new Date(Date.now() + 86_400_000);
        const pad = (n: number) => String(n).padStart(2, '0');
        const day = `${tomorrow.getFullYear()}-${pad(tomorrow.getMonth() + 1)}-${pad(tomorrow.getDate())}`;
        httpTesting.expectOne('/api/appointments/mine').flush([{ id: 1, startsAt: new Date(Date.now() + 2 * 86_400_000).toISOString(), endsAt: new Date().toISOString(), status: 'confirmed', modality: 'online' }]);
        httpTesting.expectOne('/api/portal/training/sessions').flush({ enabled: true, sessions: [{ id: 5, planId: 1, scheduledDate: day, title: 'Fuerza tren superior', sessionType: 'strength', durationMinutes: 55 }] });
        fixture.detectChanges();

        const text: string = fixture.nativeElement.textContent;
        expect(text).toContain('Hola, Laura');
        expect(text).toContain('Fuerza tren superior');
        expect(text).toContain('55 min');
        expect(text).toContain('Próxima cita');
        expect(text).toContain('Online');
        expect(text).toContain('Accesos rápidos');
    });

    it('keeps the page usable when one of the two requests fails', () => {
        const fixture = TestBed.createComponent(PatientHomeComponent);
        fixture.detectChanges();
        httpTesting.expectOne('/api/appointments/mine').flush('', { status: 500, statusText: 'Error' });
        httpTesting.expectOne('/api/portal/training/sessions').flush({ enabled: false, sessions: [] });
        fixture.detectChanges();

        expect(fixture.nativeElement.textContent).toContain('Accesos rápidos');
        expect(fixture.nativeElement.textContent).not.toContain('Cargando');
    });
});
