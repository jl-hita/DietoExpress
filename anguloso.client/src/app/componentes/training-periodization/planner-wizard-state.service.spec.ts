import { PlannerWizardStateService } from './planner-wizard-state.service';

describe('PlannerWizardStateService', () => {
  let service: PlannerWizardStateService;

  beforeEach(() => {
    sessionStorage.clear();
    service = new PlannerWizardStateService();
  });

  it('clears the saved wizard draft when its generated plan is deleted', () => {
    const draft = service.createDefault();
    draft.serverPlanId = 123;
    draft.phases = [{
      name: 'Fase antigua',
      sequenceNumber: 1,
      startDate: '2026-11-10',
      endDate: '2026-12-07',
      objective: 'Propuesta anterior',
      phaseType: 'accumulation'
    }];
    service.save(7, draft);

    service.clearIfPlanMatches(7, 123);

    expect(sessionStorage.getItem('dietoexpress:training-planner:v1:client:7')).toBeNull();
  });

  it('preserves a wizard draft when a different plan is deleted', () => {
    const draft = service.createDefault();
    draft.serverPlanId = 456;
    service.save(7, draft);

    service.clearIfPlanMatches(7, 123);

    const stored = sessionStorage.getItem('dietoexpress:training-planner:v1:client:7');
    expect(stored).not.toBeNull();
    expect(JSON.parse(stored!).serverPlanId).toBe(456);
  });
});
