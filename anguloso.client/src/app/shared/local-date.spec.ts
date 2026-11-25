import { describe, expect, it } from 'vitest';
import { localDateInput } from './local-date';

describe('localDateInput', () => {
  it('usa el día del navegador justo después y justo antes de medianoche', () => {
    expect(localDateInput(new Date(2026, 0, 5, 0, 10))).toBe('2026-01-05');
    expect(localDateInput(new Date(2026, 11, 31, 23, 50))).toBe('2026-12-31');
  });
});
