import { of, throwError } from 'rxjs';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { GuidedConsultationComponent } from './guided-consultation.component';

/**
 * Pruebas de comportamiento de la navegación de la consulta guiada (sin TestBed: el componente solo depende
 * de servicios inyectados por constructor, que aquí se sustituyen por dobles).
 */
describe('GuidedConsultationComponent navigation', () => {
  let component: any;
  let portal: { updateGuidedConsultationProgress: ReturnType<typeof vi.fn>; reviewCheckin: ReturnType<typeof vi.fn> };
  let router: { navigate: ReturnType<typeof vi.fn> };
  let snack: { open: ReturnType<typeof vi.fn> };

  const serverState = (currentStep: string, status = 'in_progress') => ({
    status, currentStep, consultationType: 'follow_up', completedSteps: []
  });

  beforeEach(() => {
    portal = { updateGuidedConsultationProgress: vi.fn(), reviewCheckin: vi.fn() };
    router = { navigate: vi.fn() };
    snack = { open: vi.fn() };
    component = new GuidedConsultationComponent(
      { snapshot: { paramMap: { get: () => '1' } } } as any, router as any, portal as any,
      {} as any, {} as any, {} as any, snack as any);
    component.data = { appointment: { id: 1, clientId: 7 }, suggestedConsultationType: 'follow_up' };
    component.consultation = serverState('evolution');
    component.currentStep = 'evolution';
    component.stepIndex = component.visibleSteps.findIndex((s: any) => s.key === 'evolution');
  });

  it('retroceder con guardado fallido vuelve realmente al paso de origen', () => {
    // 1.er guardado (progreso clínico) correcto; 2.º (cambio de paso) falla.
    portal.updateGuidedConsultationProgress
      .mockReturnValueOnce(of(serverState('evolution')))
      .mockReturnValueOnce(throwError(() => ({ error: { message: 'fallo' } })));

    component.previousStep();

    expect(component.currentStep).toBe('evolution');
    expect(component.savingProgress).toBe(false);
    expect(component.navigationPending).toBe(false);
  });

  it('el selector lateral con guardado fallido vuelve al paso de origen', () => {
    portal.updateGuidedConsultationProgress
      .mockReturnValueOnce(of(serverState('evolution')))
      .mockReturnValueOnce(throwError(() => ({ error: { message: 'fallo' } })));

    component.goToStep('goals');

    expect(component.currentStep).toBe('evolution');
    expect(component.savingProgress).toBe(false);
  });

  it('retroceder con guardado correcto avanza al paso anterior', () => {
    portal.updateGuidedConsultationProgress
      .mockReturnValueOnce(of(serverState('evolution')))
      .mockReturnValueOnce(of(serverState('summary')));

    component.previousStep();

    expect(component.currentStep).toBe('summary');
  });

  it('sin consulta cargada los callbacks de guardado se liberan', () => {
    component.consultation = null;
    const callback = vi.fn();

    component.saveClinicalProgress(callback);

    expect(callback).toHaveBeenCalledOnce();
  });

  it('una salida normal espera al guardado y luego navega', () => {
    portal.updateGuidedConsultationProgress.mockReturnValueOnce(of(serverState('evolution')));

    component.openPatientMessages();

    expect(portal.updateGuidedConsultationProgress).toHaveBeenCalled();
    expect(router.navigate).toHaveBeenCalledWith(['/clients', 7, 'messages']);
    expect(component.navigationPending).toBe(false);
  });

  it('con el guardado en error la salida pide confirmación y no deja atrapado al profesional', () => {
    component.clinicalSaveState = 'error';
    const confirm = vi.spyOn(window, 'confirm');

    confirm.mockReturnValueOnce(false);
    component.openPatientDiet();
    expect(router.navigate).not.toHaveBeenCalled();

    confirm.mockReturnValueOnce(true);
    component.openPatientDiet();
    expect(router.navigate).toHaveBeenCalledOnce();
    confirm.mockRestore();
  });

  it('una consulta cerrada navega sin guardar y bloquea las acciones que modifican datos clínicos', () => {
    component.consultation = serverState('evolution', 'completed');
    component.data = { ...component.data, latestCheckin: { id: 5 } };

    component.nextStep();
    component.reviewCheckin();

    expect(portal.updateGuidedConsultationProgress).not.toHaveBeenCalled();
    expect(portal.reviewCheckin).not.toHaveBeenCalled();
    expect(component.isReviewMode).toBe(true);
    expect(component.currentStep).not.toBe('evolution');
  });

  it('ir a la agenda al fallar el inicio no espera guardados', () => {
    component.backToAgenda();
    expect(router.navigate).toHaveBeenCalledWith(['/appointments']);
  });
});
