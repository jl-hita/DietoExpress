import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AppointmentSlot, PatientAppointment, PatientPortalService } from '../../../servicios/patient-portal.service';

@Component({
  selector: 'app-patient-appointments',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './patient-appointments.component.html',
  styleUrls: ['./patient-appointments.component.css']
})
export class PatientAppointmentsComponent {
  @Input() slots: AppointmentSlot[] = [];
  @Input() upcomingAppointments: PatientAppointment[] = [];
  @Input() pastAppointments: PatientAppointment[] = [];
  @Input() loading = false;
  @Input() booking = false;
  @Input() error: string | null = null;
  @Input() success: string | null = null;

  @Output() refresh = new EventEmitter<void>();
  @Output() request = new EventEmitter<{ slot: AppointmentSlot; modality: 'in_person' | 'online' }>();
  selectedModality: 'in_person' | 'online' = 'in_person';
  reviewRatings: Record<number, number> = {};
  reviewComments: Record<number, string> = {};
  reviewSubmitting: Record<number, boolean> = {};
  reviewMessages: Record<number, string> = {};
  reviewErrors: Record<number, string> = {};

  constructor(private readonly portalService: PatientPortalService) {}
  @Output() cancel = new EventEmitter<PatientAppointment>();
  @Output() joinVideo = new EventEmitter<PatientAppointment>();

  calendarMonth = new Date(new Date().getFullYear(), new Date().getMonth(), 1);
  selectedAvailabilityDate: string | null = null;

  submitReview(appointment: PatientAppointment): void {
    const rating = this.reviewRatings[appointment.id] || 0;
    if (rating < 1 || rating > 5 || this.reviewSubmitting[appointment.id]) return;
    this.reviewSubmitting[appointment.id] = true;
    this.reviewErrors[appointment.id] = '';
    this.portalService.submitDirectoryReview(appointment.id, rating, this.reviewComments[appointment.id] || '').subscribe({
      next: result => {
        this.reviewSubmitting[appointment.id] = false;
        this.reviewMessages[appointment.id] = result.message || 'Gracias por tu valoración.';
      },
      error: err => {
        this.reviewSubmitting[appointment.id] = false;
        this.reviewErrors[appointment.id] = err?.error?.message || 'No hemos podido guardar la valoración.';
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
