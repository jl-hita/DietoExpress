import { describe, expect, it } from "vitest";

import type { PatientAppointment, TrainingEnvelope } from './patient-app.models';
import { firstName, formatDateLabel, formatTimeLabel, isToday, localIsoDate, pickNextAppointment, pickNextTraining } from './patient-app.logic';

const appointment = (id: number, startsAt: string, status = 'confirmed'): PatientAppointment => ({ id, startsAt, endsAt: startsAt, status });
const now = new Date('2026-11-11T10:00:00Z');

describe('firstName', () => {
    it('takes the first word and tolerates empty values', () => {
        expect(firstName('Laura Gómez Pérez')).toBe('Laura');
        expect(firstName('  Ana  ')).toBe('Ana');
        expect(firstName('')).toBe('');
        expect(firstName(null)).toBe('');
        expect(firstName(undefined)).toBe('');
    });
});

describe('pickNextAppointment (same rule as the Android home)', () => {
    it('returns the nearest future appointment that is confirmed or requested', () => {
        const next = pickNextAppointment([
            appointment(1, '2026-11-20T09:00:00Z'),
            appointment(2, '2026-11-12T09:00:00Z', 'requested'),
            appointment(3, '2026-11-15T09:00:00Z')
        ], now);
        expect(next?.id).toBe(2);
    });

    it('ignores past, cancelled and completed appointments', () => {
        expect(pickNextAppointment([
            appointment(1, '2026-11-01T09:00:00Z'),
            appointment(2, '2026-11-20T09:00:00Z', 'cancelled'),
            appointment(3, '2026-11-21T09:00:00Z', 'completed')
        ], now)).toBeNull();
    });

    it('is case-insensitive on the status and survives invalid data', () => {
        expect(pickNextAppointment([appointment(1, '2026-11-20T09:00:00Z', 'CONFIRMED')], now)?.id).toBe(1);
        expect(pickNextAppointment([appointment(1, 'no-es-una-fecha')], now)).toBeNull();
        expect(pickNextAppointment(null, now)).toBeNull();
        expect(pickNextAppointment(undefined, now)).toBeNull();
    });
});

describe('pickNextTraining (same rule as the Android home)', () => {
    const training = (sessions: TrainingEnvelope['sessions'], enabled = true): TrainingEnvelope => ({ enabled, sessions });
    const session = (id: number, scheduledDate: string, status?: string, outcome?: string) =>
        ({ id, planId: 1, scheduledDate, title: 'S' + id, sessionType: 'strength', status, outcome });

    it('returns today or the nearest future pending session', () => {
        const next = pickNextTraining(training([
            session(1, '2026-11-14'), session(2, '2026-11-11'), session(3, '2026-11-12')
        ]), '2026-11-11');
        expect(next?.id).toBe(2);
    });

    it('skips completed, cancelled, not completed and past sessions', () => {
        expect(pickNextTraining(training([
            session(1, '2026-11-12', 'completed'),
            session(2, '2026-11-12', 'cancelled'),
            session(3, '2026-11-12', 'not_completed'),
            session(4, '2026-11-12', undefined, 'completed'),
            session(5, '2026-11-10')
        ]), '2026-11-11')).toBeNull();
    });

    it('shows nothing when the module is not enabled for the patient', () => {
        expect(pickNextTraining(training([session(1, '2026-11-12')], false), '2026-11-11')).toBeNull();
        expect(pickNextTraining(null)).toBeNull();
    });

    it('accepts dates with time and sorts them by day', () => {
        expect(pickNextTraining(training([session(1, '2026-11-13T00:00:00'), session(2, '2026-11-12T08:00:00')]), '2026-11-11')?.id).toBe(2);
    });
});

describe('date helpers', () => {
    it('builds a local yyyy-MM-dd date and recognizes today', () => {
        const date = new Date(2026, 10, 5, 23, 30);
        expect(localIsoDate(date)).toBe('2026-11-05');
        expect(isToday('2026-11-05T00:00:00', '2026-11-05')).toBe(true);
        expect(isToday('2026-11-06', '2026-11-05')).toBe(false);
    });

    it('formats the day in the interface language and returns invalid values untouched', () => {
        expect(formatDateLabel('2026-11-11', 'es')).toMatch(/miércoles/i);
        expect(formatDateLabel('2026-11-11', 'en')).toMatch(/wednesday/i);
        expect(formatDateLabel('2026-11-11', 'ca')).toMatch(/dimecres/i);
        expect(formatDateLabel('mañana', 'es')).toBe('mañana');
    });

    it('formats an hour or returns empty when the instant is invalid', () => {
        expect(formatTimeLabel('2026-11-11T10:30:00', 'es')).toMatch(/10:30/);
        expect(formatTimeLabel('nada', 'es')).toBe('');
    });
});
