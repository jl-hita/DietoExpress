import { describe, expect, it } from 'vitest';
import { formatDateDisplay, parseDateDisplay } from './date-input.directive';

describe('date input DD/MM/AAAA conversion', () => {
  it('converts Spanish input into the canonical API format without swapping day and month', () => {
    expect(parseDateDisplay('04/05/2020')).toBe('2020-05-04');
    expect(formatDateDisplay('2020-05-04')).toBe('04/05/2020');
  });

  it('accepts valid leap days and rejects invalid calendar dates', () => {
    expect(parseDateDisplay('29/02/2024')).toBe('2024-02-29');
    expect(parseDateDisplay('29/02/2023')).toBeNull();
    expect(parseDateDisplay('31/04/2024')).toBeNull();
    expect(parseDateDisplay('00/12/2024')).toBeNull();
    expect(parseDateDisplay('12/13/2024')).toBeNull();
  });

  it('keeps empty values empty and rejects partial or non-padded input', () => {
    expect(parseDateDisplay('')).toBe('');
    expect(parseDateDisplay('  ')).toBe('');
    expect(parseDateDisplay('4/5/2020')).toBeNull();
    expect(parseDateDisplay('04/05/20')).toBeNull();
    expect(parseDateDisplay('04/')).toBeNull();
  });

  it('formats ISO date-time values using their date portion without timezone conversion', () => {
    expect(formatDateDisplay('2020-05-04T23:30:00.000Z')).toBe('04/05/2020');
  });

  it('formats Date objects using their local calendar date', () => {
    expect(formatDateDisplay(new Date(2020, 4, 4))).toBe('04/05/2020');
  });
});
