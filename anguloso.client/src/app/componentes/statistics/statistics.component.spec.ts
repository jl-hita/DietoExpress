import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { ProfessionalStatistics, ProfessionalStatisticsService } from '../../servicios/professional-statistics.service';
import { StatisticsComponent } from './statistics.component';

describe('StatisticsComponent (OnPush state)', () => {
  let fixture: ComponentFixture<StatisticsComponent>;
  let response: Subject<ProfessionalStatistics>;
  let service: jasmine.SpyObj<ProfessionalStatisticsService>;

  const statistics: ProfessionalStatistics = {
    from: '2026-09-17',
    to: '2026-10-16',
    scope: 'nutritionist',
    activePatients: 12,
    newPatients: 3,
    archivedPatients: 1,
    totalAppointments: 8,
    completedAppointments: 6,
    noShowAppointments: 1,
    cancelledAppointments: 1,
    checkins: 5,
    reviewedCheckins: 4,
    averageAdherence: 82.5,
    subscriptionRevenue: 0,
    paidPayments: 0,
    nutritionistWorkload: [],
    activeSubscriptions: 0,
    scheduledCancellations: 0,
    cancelledSubscriptions: 0,
    newSubscriptions: 0,
    weightChangeKg: -1.2,
    bodyFatChangePoints: -0.5,
    muscleMassChangeKg: 0.2,
    series: [
      { month: '2026-10-01', newPatients: 3, completedAppointments: 6, checkins: 5 }
    ]
  };

  beforeEach(async () => {
    response = new Subject<ProfessionalStatistics>();
    service = jasmine.createSpyObj<ProfessionalStatisticsService>('ProfessionalStatisticsService', ['get']);
    service.get.and.returnValue(response.asObservable());

    await TestBed.configureTestingModule({
      imports: [StatisticsComponent],
      providers: [{ provide: ProfessionalStatisticsService, useValue: service }]
    }).compileComponents();

    fixture = TestBed.createComponent(StatisticsComponent);
  });

  it('renders asynchronous HTTP data and clears the loading state', () => {
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Cargando estadísticas');

    response.next(statistics);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Pacientes activos');
    expect(fixture.nativeElement.textContent).toContain('12');
    expect(fixture.nativeElement.textContent).toContain('Tasa de no asistencia');
    expect(fixture.nativeElement.textContent).not.toContain('Cargando estadísticas');
  });

  it('renders the error state when the request fails', () => {
    fixture.detectChanges();
    response.error({ error: { message: 'Error de prueba' } });
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Error de prueba');
    expect(fixture.nativeElement.textContent).not.toContain('Cargando estadísticas');
  });
});
