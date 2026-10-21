import { beforeEach, describe, expect, it, type MockedObject, vi } from "vitest";
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, Subject } from 'rxjs';
import { ProfessionalDashboard, ProfessionalDashboardService } from '../../servicios/professional-dashboard.service';
import { DashboardComponent } from './dashboard.component';

describe('DashboardComponent (signal state)', () => {
    let fixture: ComponentFixture<DashboardComponent>;
    let response: Subject<ProfessionalDashboard>;
    let service: { get: ReturnType<typeof vi.fn>; getFollowupAttention: ReturnType<typeof vi.fn> };

    const dashboard: ProfessionalDashboard = {
        openTaskCount: 0, overdueTaskCount: 0, unreadMessageCount: 0,
        pendingDocumentCount: 0, pendingPatientDataCount: 0, pendingCheckinCount: 0,
        documentProvisioningRetryCount: 0, documentProvisioningFailedCount: 0,
        documentProvisioningIncompleteCount: 0, documentProvisioningIssues: [],
        openTasks: [], todayAppointments: [], upcomingAppointments: [],
        pendingCheckins: [], pendingDocuments: [], pendingPatientData: []
    };

    beforeEach(async () => {
        response = new Subject<ProfessionalDashboard>();
        service = {
            get: vi.fn().mockName("ProfessionalDashboardService.get"),
            getFollowupAttention: vi.fn().mockName("ProfessionalDashboardService.getFollowupAttention").mockReturnValue(of([]))
        };
        service.get.mockReturnValue(response.asObservable());
        await TestBed.configureTestingModule({
            imports: [DashboardComponent],
            providers: [{ provide: ProfessionalDashboardService, useValue: service }]
        }).compileComponents();
        fixture = TestBed.createComponent(DashboardComponent);
    });

    it('updates the view when asynchronous data arrives', () => {
        fixture.detectChanges();
        expect(fixture.nativeElement.textContent).toContain('Cargando tu jornada');
        response.next(dashboard);
        fixture.detectChanges();
        expect(fixture.nativeElement.textContent).toContain('Tu jornada de hoy');
        expect(fixture.nativeElement.textContent).not.toContain('Cargando tu jornada');
    });

    it('clears loading and shows the error when the request fails', () => {
        fixture.detectChanges();
        response.error({ error: { message: 'Error de prueba' } });
        fixture.detectChanges();
        expect(fixture.nativeElement.textContent).toContain('Error de prueba');
        expect(fixture.nativeElement.textContent).not.toContain('Cargando tu jornada');
    });
});
