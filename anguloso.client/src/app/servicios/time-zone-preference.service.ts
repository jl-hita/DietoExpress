import { Injectable, signal } from '@angular/core';

export interface TimeZoneOption { id: string; label: string; }
export const TIME_ZONE_OPTIONS: TimeZoneOption[] = [
  { id: 'Europe/Madrid', label: 'España peninsular (Madrid)' },
  { id: 'Atlantic/Canary', label: 'Islas Canarias' },
  { id: 'Europe/Lisbon', label: 'Portugal continental' },
  { id: 'UTC', label: 'UTC (referencia técnica)' },
  { id: 'America/Mexico_City', label: 'México — Ciudad de México' },
  { id: 'America/Cancun', label: 'México — Cancún' },
  { id: 'America/Tijuana', label: 'México — Tijuana' },
  { id: 'America/Bogota', label: 'Colombia' },
  { id: 'America/Lima', label: 'Perú' },
  { id: 'America/Guayaquil', label: 'Ecuador continental' },
  { id: 'America/Caracas', label: 'Venezuela' },
  { id: 'America/Santiago', label: 'Chile continental' },
  { id: 'America/Argentina/Buenos_Aires', label: 'Argentina — Buenos Aires' },
  { id: 'America/Montevideo', label: 'Uruguay' },
  { id: 'America/Sao_Paulo', label: 'Brasil — São Paulo' },
  { id: 'America/Puerto_Rico', label: 'Puerto Rico' },
  { id: 'America/Havana', label: 'Cuba' },
  { id: 'America/New_York', label: 'Estados Unidos/Canadá — Este' },
  { id: 'America/Chicago', label: 'Estados Unidos — Centro' },
  { id: 'America/Denver', label: 'Estados Unidos — Montaña' },
  { id: 'America/Los_Angeles', label: 'Estados Unidos/Canadá — Pacífico' }
];

@Injectable({ providedIn: 'root' })
export class TimeZonePreferenceService {
  private readonly zoneState = signal(this.detectBrowserTimeZone());
  readonly timeZoneId = this.zoneState.asReadonly();
  detectBrowserTimeZone(): string {
    try { const zone = Intl.DateTimeFormat().resolvedOptions().timeZone; return zone || 'Europe/Madrid'; }
    catch { return 'Europe/Madrid'; }
  }
  setTimeZoneId(value: string | null | undefined): void {
    const candidate = value?.trim();
    if (!candidate) { this.zoneState.set(this.detectBrowserTimeZone()); return; }
    try { new Intl.DateTimeFormat('en', { timeZone: candidate }); this.zoneState.set(candidate); }
    catch { this.zoneState.set(this.detectBrowserTimeZone()); }
  }
  formatInPreferredZone(value: string | Date, options: Intl.DateTimeFormatOptions, locale = 'es-ES'): string {
    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat(locale, { ...options, timeZone: this.zoneState() }).format(date);
  }

  formatLocalTimestamp(date: Date): string {
    const zone = this.zoneState();
    const parts = new Intl.DateTimeFormat('en-CA', { timeZone: zone, year: 'numeric', month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit', second: '2-digit', hourCycle: 'h23' }).formatToParts(date);
    const part = (type: string) => parts.find(item => item.type === type)?.value ?? '00';
    const local = `${part('year')}-${part('month')}-${part('day')}T${part('hour')}:${part('minute')}:${part('second')}.${date.getUTCMilliseconds().toString().padStart(3, '0')}`;
    const label = new Intl.DateTimeFormat('en', { timeZone: zone, timeZoneName: 'longOffset' }).formatToParts(date).find(item => item.type === 'timeZoneName')?.value ?? 'GMT';
    return `${local}${label === 'GMT' ? '+00:00' : label.replace('GMT', '')} (${zone})`;
  }
}
