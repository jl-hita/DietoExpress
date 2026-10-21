import { Injectable } from '@angular/core';
import { NativeDateAdapter } from '@angular/material/core';
import { parseDateDisplay } from './date-input.directive';

/** Native DateAdapter that always displays and parses datepicker input as DD/MM/YYYY. */
@Injectable()
export class SpanishDateAdapter extends NativeDateAdapter {
  override parse(value: unknown): Date | null {
    if (typeof value !== 'string') return super.parse(value);
    const input = value.trim();
    if (!input) return null;
    const iso = /^(\d{4})-(\d{2})-(\d{2})$/.exec(input);
    const canonical = iso ? input : parseDateDisplay(input);
    if (!canonical) return null;
    const [year, month, day] = canonical.split('-').map(Number);
    const date = this.createDate(year, month - 1, day);
    return this.isValid(date) ? date : null;
  }

  override format(date: Date, displayFormat: Object): string {
    if (!this.isValid(date)) return '';
    const format = displayFormat as Intl.DateTimeFormatOptions;
    if (format.year === 'numeric' && format.month === 'numeric' && format.day === 'numeric') {
      return `${String(date.getDate()).padStart(2, '0')}/${String(date.getMonth() + 1).padStart(2, '0')}/${String(date.getFullYear()).padStart(4, '0')}`;
    }
    return super.format(date, displayFormat);
  }
}
