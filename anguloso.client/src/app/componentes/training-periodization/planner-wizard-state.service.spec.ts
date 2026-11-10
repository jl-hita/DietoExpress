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

  it('replaces an orphaned plan draft with a clean first-step draft', () => {
    const draft = service.createDefault();
    draft.currentStep = 3;
    draft.serverPlanId = 9;
    draft.serverMacrocycleSaved = true;
    draft.serverSplitSaved = true;
    draft.phases = [{
      name: 'Fase antigua',
      sequenceNumber: 1,
      startDate: '2026-11-10',
      endDate: '2026-12-07',
      objective: 'Propuesta anterior',
      phaseType: 'accumulation'
    }];
    draft.generatedPhaseKeys = ['2026-11-10|2026-12-07'];
    service.save(7, draft);

    const reset = service.reset(7);

    expect(reset.serverPlanId).toBeNull();
    expect(reset.currentStep).toBe(0);
    expect(reset.phases).toEqual([]);
    expect(reset.generatedPhaseKeys).toEqual([]);
    expect(reset.serverMacrocycleSaved).toBe(false);
    expect(reset.serverSplitSaved).toBe(false);
    const stored = JSON.parse(sessionStorage.getItem('dietoexpress:training-planner:v1:client:7')!);
    expect(stored.serverPlanId).toBeNull();
    expect(stored.phases).toEqual([]);
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
