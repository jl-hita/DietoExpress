import { of, Subject, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { PatientDietHistoryComponent } from './patient-diet-history.component';

describe('PatientDietHistoryComponent', () => {
  const create = (portal: any, foods: any = {}) => new PatientDietHistoryComponent(portal, foods);

  it('solo pide el historial al desplegarlo', () => {
    const portal = { getMyDietHistory: vi.fn(() => of([{ assignmentId: 1, dietName: 'Plan', startDate: '2026-01-01', isActive: true }])) };
    const component = create(portal);

    expect(portal.getMyDietHistory).not.toHaveBeenCalled();
    component.toggle();

    expect(component.entries()?.length).toBe(1);
  });

  it('una respuesta tardía no sustituye al plan seleccionado', () => {
    const first = new Subject<any>();
    const portal = { getMyDietHistoryDetail: vi.fn().mockReturnValueOnce(first).mockReturnValueOnce(of({ name: 'Segundo' })) };
    const component = create(portal);

    component.select(1);
    component.select(2);
    first.next({ name: 'Primero' });

    expect(component.detail().name).toBe('Segundo');
  });

  it('un fallo al abrir un plan permite reintentar', () => {
    const portal = { getMyDietHistoryDetail: vi.fn().mockReturnValueOnce(throwError(() => new Error())).mockReturnValueOnce(of({ name: 'Plan' })) };
    const component = create(portal);

    component.select(1);
    expect(component.detailError()).toBeTruthy();
    component.select(1);

    expect(component.detail().name).toBe('Plan');
  });

  it('los equivalentes de un intercambio se consultan una sola vez por grupo', () => {
    const foods = { getFoodsInExchangeGroup: vi.fn(() => of([{ foodName: 'Arroz', gramsPerExchange: 20 }])) };
    const component = create({}, foods);

    component.toggleEquivalents(1, 4);
    component.toggleEquivalents(2, 4);

    expect(component.expandedItemId()).toBe(2);
    expect(foods.getFoodsInExchangeGroup).toHaveBeenCalledOnce();
  });
});
