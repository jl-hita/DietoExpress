import { FormBuilder } from '@angular/forms';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { DietGeneratorDialogComponent } from './diet-generator-dialog.component';

describe('DietGeneratorDialogComponent sin pan', () => {
  const create = (clientId: number | null = null) => {
    const dietService = { generateDiet: vi.fn().mockReturnValue(of({ id: 1 })) };
    const clientService = { getClient: vi.fn().mockReturnValue(of({ foodPreferences: { excludeBread: true } })), getEnergyRequirements: vi.fn().mockReturnValue(of({})) };
    const dialogRef = { close: vi.fn() };
    const component = new DietGeneratorDialogComponent(
      new FormBuilder(), dietService as any, clientService as any, dialogRef as any, { clientId, defaultKcal: 2500 } as any);
    return { component, dietService, clientService };
  };

  it('por defecto el pan está permitido', () => {
    const { component, dietService } = create();
    component.generate();
    expect(dietService.generateDiet.mock.calls[0][0].excludeBread).toBe(false);
  });

  it('al marcar "Sin pan" la petición lo envía antes de generar', () => {
    const { component, dietService } = create();
    component.form.patchValue({ excludeBread: true });
    component.generate();
    expect(dietService.generateDiet.mock.calls[0][0].excludeBread).toBe(true);
  });

  it('con paciente, la preferencia guardada en su ficha marca "Sin pan" por defecto y se puede desmarcar', () => {
    const { component, dietService } = create(7);
    component.ngOnInit();
    expect(component.form.value.excludeBread).toBe(true);

    component.form.patchValue({ excludeBread: false });
    component.generate();
    expect(dietService.generateDiet.mock.calls[0][0].excludeBread).toBe(false);
  });
});
