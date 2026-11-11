import { describe, expect, it } from 'vitest';
import { EQUIPMENT_OPTIONS, equipmentSummary, normalizeEquipmentCode, parseEquipmentCsv } from './training-equipment.catalog';

describe('training equipment catalog', () => {
  it('normaliza igual que el servidor: minúsculas y guiones', () => {
    expect(normalizeEquipmentCode(' Pull_Up Bar ')).toBe('pull-up-bar');
    expect(parseEquipmentCsv('barbell, Dumbbell, barbell,,')).toEqual(['barbell', 'dumbbell']);
  });

  it('no repite códigos en el catálogo', () => {
    const codes = EQUIPMENT_OPTIONS.map(option => option.code);
    expect(new Set(codes).size).toBe(codes.length);
  });

  it('resume en español y trata la lista vacía como todo el material', () => {
    expect(equipmentSummary('')).toContain('Todo el material');
    expect(equipmentSummary('barbell, dumbbell')).toBe('Barra, Mancuernas');
    expect(equipmentSummary('barbell, dumbbell, cable, bench')).toBe('Barra, Mancuernas, Poleas y 1 más');
    expect(equipmentSummary('algo-raro')).toBe('algo-raro');
  });
});
