import { describe, expect, it } from 'vitest';
import { compareFollowupPeriods, countMissedCheckinWeeks, detectPersistentFollowupSignals, type AdherenceCheckinRecord } from './adherence-analytics';

const record = (id: number, week: string, values: Partial<AdherenceCheckinRecord> = {}): AdherenceCheckinRecord => ({
  id, week_start: week, submitted_at: `${week}T12:00:00Z`, ...values
});

describe('adherence analytics', () => {
  it('compares periods and ignores missing values rather than treating them as zero', () => {
    const result = compareFollowupPeriods([
      record(1, '2026-09-07', { adherence: 80, hunger: null }),
      record(2, '2026-09-14', { adherence: 70 }),
      record(3, '2026-09-21', { adherence: 60, hunger: 5 }),
      record(4, '2026-09-28', { adherence: 50, hunger: 7 })
    ], 2);
    const adherence = result.find(metric => metric.key === 'adherence')!;
    expect(adherence.currentAverage).toBe(55);
    expect(adherence.previousAverage).toBe(75);
    const hunger = result.find(metric => metric.key === 'hunger')!;
    expect(hunger.currentCount).toBe(2);
    expect(hunger.previousCount).toBe(0);
    expect(hunger.difference).toBeNull();
  });

  it('flags only persistent, directional changes supported by three valid check-ins', () => {
    const signals = detectPersistentFollowupSignals([
      record(1, '2026-09-07', { adherence: 90, hunger: 3 }),
      record(2, '2026-09-14', { adherence: 80, hunger: 4 }),
      record(3, '2026-09-21', { adherence: 70, hunger: 6 })
    ]);
    expect(signals.map(signal => signal.key)).toContain('adherence');
    expect(signals.map(signal => signal.key)).toContain('hunger');
    expect(detectPersistentFollowupSignals([
      record(1, '2026-09-07', { adherence: 90 }),
      record(2, '2026-09-14', { adherence: 70 })
    ])).toHaveLength(0);
  });

  it('counts only complete missed weeks and leaves the current week open', () => {
    expect(countMissedCheckinWeeks([record(1, '2026-10-12')], new Date('2026-10-19T12:00:00Z'))).toBe(0);
    expect(countMissedCheckinWeeks([record(1, '2026-10-05')], new Date('2026-10-19T12:00:00Z'))).toBe(1);
    expect(countMissedCheckinWeeks([], new Date('2026-10-19T12:00:00Z'))).toBe(0);
  });
});
