import { TestBed } from '@angular/core/testing';
import { TimeZonePreferenceService } from './time-zone-preference.service';

describe('TimeZonePreferenceService', () => {
  let service: TimeZonePreferenceService;
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [TimeZonePreferenceService] });
    service = TestBed.inject(TimeZonePreferenceService);
  });
  it('formats instants using the selected IANA time zone, not the host zone', () => {
    service.setTimeZoneId('Atlantic/Canary');
    const canary = service.formatInPreferredZone('2026-01-15T12:00:00.000Z', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
    expect(canary).toContain('12:00');
    service.setTimeZoneId('Europe/Madrid');
    const mainland = service.formatInPreferredZone('2026-01-15T12:00:00.000Z', { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' });
    expect(mainland).toContain('13:00');
  });
  it('returns an empty string for invalid date values', () => {
    expect(service.formatInPreferredZone('not-a-date', { dateStyle: 'medium' })).toBe('');
  });
});
