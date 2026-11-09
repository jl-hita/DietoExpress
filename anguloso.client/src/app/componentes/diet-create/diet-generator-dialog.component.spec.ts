import { FormBuilder } from '@angular/forms';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { DietGeneratorDialogComponent } from './diet-generator-dialog.component';

describe('DietGeneratorDialogComponent sin pan', () => {
  const create = () => {
    const dietService = { generateDiet: vi.fn().mockReturnValue(of({ id: 1 })) };
    const dialogRef = { close: vi.fn() };
    const component = new DietGeneratorDialogComponent(
      new FormBuilder(), dietService as any, {} as any, dialogRef as any, { clientId: null, defaultKcal: 2000 } as any);
    return { component, dietService };
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
});
