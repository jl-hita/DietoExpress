import { Component, EventEmitter, Input, Output, ChangeDetectionStrategy, signal, OnDestroy } from '@angular/core';
import { Subscription, interval } from 'rxjs';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { FormsModule } from '@angular/forms';
import { AppointmentSlot, PatientAppointment, PatientPortalService } from '../../../servicios/patient-portal.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-patient-appointments',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './patient-appointments.component.html',
  styleUrls: ['./patient-appointments.component.css']
})
export class PatientAppointmentsComponent implements OnDestroy {
  private readonly nowMsState = signal(Date.now());
  private readonly clockSubscription: Subscription;
  get nowMs(): number { return this.nowMsState(); }

  @Input() slots: AppointmentSlot[] = [];
  @Input() upcomingAppointments: PatientAppointment[] = [];
  @Input() pastAppointments: PatientAppointment[] = [];
  @Input() loading = false;
  @Input() booking = false;
  @Input() error: string | null = null;
  @Input() success: string | null = null;

  @Output() refresh = new EventEmitter<void>();
  @Output() request = new EventEmitter<{ slot: AppointmentSlot; modality: 'in_person' | 'online' }>();
  private readonly selectedModalityState = signal<'in_person' | 'online'>('in_person');
  get selectedModality(): 'in_person' | 'online' { return this.selectedModalityState(); }
  set selectedModality(value: 'in_person' | 'online') { this.selectedModalityState.set(value); }

  // Los mapas de entrada se mantienen mutables por compatibilidad con ngModel.
  reviewRatings: Record<number, number> = {};
  reviewComments: Record<number, string> = {};

  private readonly reviewSubmittingState = signal<Record<number, boolean>>({});
  get reviewSubmitting(): Record<number, boolean> { return this.reviewSubmittingState(); }
  set reviewSubmitting(value: Record<number, boolean>) { this.reviewSubmittingState.set(value); }

  private readonly reviewMessagesState = signal<Record<number, string>>({});
  get reviewMessages(): Record<number, string> { return this.reviewMessagesState(); }
  set reviewMessages(value: Record<number, string>) { this.reviewMessagesState.set(value); }

  private readonly reviewErrorsState = signal<Record<number, string>>({});
  get reviewErrors(): Record<number, string> { return this.reviewErrorsState(); }
  set reviewErrors(value: Record<number, string>) { this.reviewErrorsState.set(value); }

  constructor(private readonly portalService: PatientPortalService) {
    this.clockSubscription = interval(15_000).subscribe(() => this.nowMsState.set(Date.now()));
  }

  ngOnDestroy(): void { this.clockSubscription.unsubscribe(); }

  videoAccessState(appointment: PatientAppointment): 'waiting' | 'available' | 'expired' {
    const now = this.nowMs;
    const availableFrom = new Date(appointment.startsAt).getTime() - 15 * 60_000;
    const expiresAt = appointment.videoExpiresAt
      ? new Date(appointment.videoExpiresAt).getTime()
      : new Date(appointment.endsAt).getTime() + 15 * 60_000;
    if (now < availableFrom) return 'waiting';
    if (!Number.isFinite(expiresAt) || now >= expiresAt) return 'expired';
    return 'available';
  }

  videoAccessCountdown(appointment: PatientAppointment): string {
    const availableFrom = new Date(appointment.startsAt).getTime() - 15 * 60_000;
    let totalMinutes = Math.max(0, Math.ceil((availableFrom - this.nowMs) / 60_000));
    const days = Math.floor(totalMinutes / 1440);
    totalMinutes -= days * 1440;
    const hours = Math.floor(totalMinutes / 60);
    const minutes = totalMinutes - hours * 60;
    const parts: string[] = [];
    if (days) parts.push(`${days} ${days === 1 ? 'día' : 'días'}`);
    if (hours) parts.push(`${hours} ${hours === 1 ? 'hora' : 'horas'}`);
    if (minutes || parts.length === 0) parts.push(`${minutes} ${minutes === 1 ? 'minuto' : 'minutos'}`);
    return parts.join(', ').replace(/, ([^,]*)$/, ' y $1');
  }
  @Output() cancel = new EventEmitter<PatientAppointment>();
  @Output() joinVideo = new EventEmitter<PatientAppointment>();

  private readonly calendarMonthState = signal(new Date(new Date().getFullYear(), new Date().getMonth(), 1));
  get calendarMonth(): Date { return this.calendarMonthState(); }
  set calendarMonth(value: Date) { this.calendarMonthState.set(value); }

  private readonly selectedAvailabilityDateState = signal<string | null>(null);
  get selectedAvailabilityDate(): string | null { return this.selectedAvailabilityDateState(); }
  set selectedAvailabilityDate(value: string | null) { this.selectedAvailabilityDateState.set(value); }

  submitReview(appointment: PatientAppointment): void {
    const rating = this.reviewRatings[appointment.id] || 0;
    if (rating < 1 || rating > 5 || this.reviewSubmitting[appointment.id]) return;
    this.reviewSubmitting = { ...this.reviewSubmitting, [appointment.id]: true };
    this.reviewErrors = { ...this.reviewErrors, [appointment.id]: '' };
    this.portalService.submitDirectoryReview(appointment.id, rating, this.reviewComments[appointment.id] || '').subscribe({
      next: result => {
        this.reviewSubmitting = { ...this.reviewSubmitting, [appointment.id]: false };
        this.reviewMessages = { ...this.reviewMessages, [appointment.id]: result.message || 'Gracias por tu valoración.' };
      },
      error: err => {
        this.reviewSubmitting = { ...this.reviewSubmitting, [appointment.id]: false };
        this.reviewErrors = { ...this.reviewErrors, [appointment.id]: err?.error?.message || 'No hemos podido guardar la valoración.' };
      }
    });
  }

  get calendarDays(): Array<{ date: Date; dayNumber: number; key: string; slotCount: number; inCurrentMonth: boolean; disabled: boolean }> {
    const year = this.calendarMonth.getFullYear();
    const month = this.calendarMonth.getMonth();
    const firstDay = new Date(year, month, 1);
    const startOffset = (firstDay.getDay() + 6) % 7;
    const start = new Date(year, month, 1 - startOffset);
    return Array.from({ length: 42 }, (_, index) => {
      const date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + index);
      const key = this.dateKey(date);
      const slotCount = this.slotsForDate(key).length;
      return { date, dayNumber: date.getDate(), key, slotCount, inCurrentMonth: date.getMonth() === month, disabled: date < new Date(new Date().setHours(0, 0, 0, 0)) || slotCount === 0 };
    });
  }

  get selectedDateSlots(): AppointmentSlot[] {
    return this.selectedAvailabilityDate ? this.slotsForDate(this.selectedAvailabilityDate) : [];
  }

  get canGoToPreviousMonth(): boolean {
    const current = new Date(new Date().getFullYear(), new Date().getMonth(), 1);
    return this.calendarMonth > current;
  }

  get canGoToNextMonth(): boolean {
    if (!this.slots.length) return false;
    const last = new Date(Math.max(...this.slots.map(slot => new Date(slot.startsAt).getTime())));
    return this.calendarMonth < new Date(last.getFullYear(), last.getMonth(), 1);
  }

  previousMonth(): void {
    if (this.canGoToPreviousMonth) {
      this.calendarMonth = new Date(this.calendarMonth.getFullYear(), this.calendarMonth.getMonth() - 1, 1);
      this.selectedAvailabilityDate = null;
    }
  }

  nextMonth(): void {
    if (this.canGoToNextMonth) {
      this.calendarMonth = new Date(this.calendarMonth.getFullYear(), this.calendarMonth.getMonth() + 1, 1);
      this.selectedAvailabilityDate = null;
    }
  }

  selectAvailabilityDate(key: string): void {
    if (!this.slotsForDate(key).length) return;
    this.selectedAvailabilityDate = key;
  }

  slotsForDate(key: string): AppointmentSlot[] {
    return this.slots.filter(slot => this.dateKey(new Date(slot.startsAt)) === key);
  }

  dateKey(date: Date): string {
    return date.getFullYear() + '-' + String(date.getMonth() + 1).padStart(2, '0') + '-' + String(date.getDate()).padStart(2, '0');
  }

  monthLabel(): string {
    return new Intl.DateTimeFormat('es-ES', { month: 'long', year: 'numeric' }).format(this.calendarMonth);
  }

  formatDaySlot(value: string): string {
    return new Intl.DateTimeFormat('es-ES', { hour: '2-digit', minute: '2-digit' }).format(new Date(value));
  }

  formatDate(value: string): string {
    return new Intl.DateTimeFormat('es-ES', {
      weekday: 'long',
      day: 'numeric',
      month: 'long'
    }).format(new Date(value));
  }

  formatTime(value: string): string {
    return new Intl.DateTimeFormat('es-ES', {
      hour: '2-digit',
      minute: '2-digit'
    }).format(new Date(value));
  }
}
