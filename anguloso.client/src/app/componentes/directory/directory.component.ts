import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { DirectoryProfile, PublicAvailabilitySlot } from './directory.models';
import { DirectoryService } from './directory.service';

@Component({
  selector: 'app-directory',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatButtonModule],
  templateUrl: './directory.component.html',
  styleUrl: './directory.component.css'
})
export class DirectoryComponent implements OnInit {
  city = '';
  province = '';
  speciality = '';
  online = false;

  profiles: DirectoryProfile[] = [];
  profile: DirectoryProfile | null = null;
  loading = false;
  error = '';
  availability: PublicAvailabilitySlot[] = [];
  availabilityLoading = false;
  availabilityError = '';
  selectedSlot: PublicAvailabilitySlot | null = null;
  bookingSubmitting = false;
  bookingError = '';
  bookingSuccess: string | null = null;
  bookingFullName = '';
  bookingEmail = '';
  bookingPhone = '';
  bookingNotes = '';
  calendarMonth = new Date(new Date().getFullYear(), new Date().getMonth(), 1);
  selectedAvailabilityDate: string | null = null;

  get calendarDays(): Array<{ date: Date; dayNumber: number; key: string; slotCount: number; inCurrentMonth: boolean; disabled: boolean }> {
    const year = this.calendarMonth.getFullYear();
    const month = this.calendarMonth.getMonth();
    const firstDay = new Date(year, month, 1);
    const startOffset = firstDay.getDay();
    const start = new Date(year, month, 1 - startOffset);
    return Array.from({ length: 42 }, (_, index) => {
      const date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + index);
      const key = this.dateKey(date);
      const slotCount = this.slotsForDate(key).length;
      return {
        date,
        dayNumber: date.getDate(),
        key,
        slotCount,
        inCurrentMonth: date.getMonth() === month,
        disabled: date < new Date(new Date().setHours(0, 0, 0, 0)) || slotCount === 0
      };
    });
  }

  get selectedDateSlots(): PublicAvailabilitySlot[] {
    return this.selectedAvailabilityDate ? this.slotsForDate(this.selectedAvailabilityDate) : [];
  }

  get canGoToPreviousMonth(): boolean {
    const current = new Date(new Date().getFullYear(), new Date().getMonth(), 1);
    return this.calendarMonth > current;
  }

  get canGoToNextMonth(): boolean {
    const lastSlotDate = this.availability.length
      ? new Date(Math.max(...this.availability.map(slot => new Date(slot.startsAt).getTime())))
      : new Date();
    const loadedLastMonth = new Date(lastSlotDate.getFullYear(), lastSlotDate.getMonth(), 1);
    return this.calendarMonth < loadedLastMonth;
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
    const slots = this.slotsForDate(key);
    if (!slots.length) return;
    this.selectedAvailabilityDate = key;
    this.selectedSlot = null;
    this.bookingError = '';
  }

  slotsForDate(key: string): PublicAvailabilitySlot[] {
    return this.availability.filter(slot => this.dateKey(new Date(slot.startsAt)) === key);
  }

  dateKey(date: Date): string {
    const year = date.getFullYear();
    const month = String(date.getMonth() + 1).padStart(2, '0');
    const day = String(date.getDate()).padStart(2, '0');
    return year + '-' + month + '-' + day;
  }

  monthLabel(): string {
    return new Intl.DateTimeFormat('es-ES', { month: 'long', year: 'numeric' }).format(this.calendarMonth);
  }

  formatDaySlot(slot: PublicAvailabilitySlot): string {
    return new Intl.DateTimeFormat('es-ES', { hour: '2-digit', minute: '2-digit' }).format(new Date(slot.startsAt));
  }

  selectSlot(slot: PublicAvailabilitySlot): void {

  constructor(
    private readonly route: ActivatedRoute,
    private readonly directoryService: DirectoryService
  ) {}

  ngOnInit(): void {
    const slug = this.route.snapshot.paramMap.get('slug');

    if (slug) {
      this.loadProfile(slug);
    } else {
      this.search();
    }
  }

  search(): void {
    this.loading = true;
    this.error = '';
    this.profile = null;

    this.directoryService.search(this.city, this.province, this.speciality, this.online).subscribe({
      next: profiles => {
        this.profiles = profiles;
        this.loading = false;
      },
      error: () => {
        this.error = 'No se ha podido cargar el directorio.';
        this.loading = false;
      }
    });
  }

  loadProfile(slug: string): void {
    this.loading = true;
    this.error = '';

    this.directoryService.getBySlug(slug).subscribe({
      next: profile => {
        this.profile = profile;
        this.loading = false;
        this.loadAvailability(profile.slug);
      },
      error: () => {
        this.error = 'No se ha encontrado el profesional solicitado.';
        this.loading = false;
      }
    });
  }

  loadAvailability(slug: string): void {
    this.availabilityLoading = true;
    this.availabilityError = '';

    this.directoryService.getAvailability(slug).subscribe({
      next: slots => {
        this.availability = slots;
        const firstAvailable = slots[0];
        if (firstAvailable) {
          const firstDate = new Date(firstAvailable.startsAt);
          this.calendarMonth = new Date(firstDate.getFullYear(), firstDate.getMonth(), 1);
        }
        this.selectedAvailabilityDate = null;
        this.availabilityLoading = false;
      },
      error: () => {
        this.availability = [];
        this.availabilityError = 'No se ha podido consultar la disponibilidad.';
        this.availabilityLoading = false;
      }
    });
  }

  selectSlot(slot: PublicAvailabilitySlot): void {
    this.selectedSlot = slot;
    this.bookingError = '';
    this.bookingSuccess = null;
  }

  clearSelectedSlot(): void {
    this.selectedSlot = null;
    this.bookingError = '';
  }

  requestAppointment(): void {
    if (!this.profile || !this.selectedSlot || this.bookingSubmitting) return;

    const fullName = this.bookingFullName.trim();
    const email = this.bookingEmail.trim().toLowerCase();
    const phone = this.bookingPhone.trim();
    const notes = this.bookingNotes.trim();

    if (!fullName || !email) {
      this.bookingError = 'El nombre y el email son obligatorios.';
      return;
    }
    if (notes.length > 500) {
      this.bookingError = 'El comentario no puede superar los 500 caracteres.';
      return;
    }

    // El slot se muestra como disponible en una consulta anterior; el backend lo
    // vuelve a comprobar dentro de una transacción antes de aceptar la solicitud.
    this.bookingSubmitting = true;
    this.bookingError = '';
    this.bookingSuccess = null;

    this.directoryService.requestAppointment(this.profile.slug, {
      startsAt: this.selectedSlot.startsAt,
      durationMinutes: Math.round(
        (new Date(this.selectedSlot.endsAt).getTime() - new Date(this.selectedSlot.startsAt).getTime()) / 60000
      ),
      fullName,
      email,
      phone: phone || undefined,
      patientNotes: notes || undefined
    }).subscribe({
      next: () => {
        this.bookingSuccess = 'Solicitud enviada para el ' + this.formatSlot(this.selectedSlot!) + '. El profesional deberá confirmarla.';
        this.bookingSubmitting = false;
        this.selectedSlot = null;
        this.bookingNotes = '';
        this.loadAvailability(this.profile!.slug);
      },
      error: error => {
        this.bookingSubmitting = false;
        this.bookingError = error?.error?.message || 'No se ha podido enviar la solicitud. El horario puede haber sido reservado por otra persona.';
        this.loadAvailability(this.profile!.slug);
      }
    });
  }

  formatSlot(slot: PublicAvailabilitySlot): string {
    const date = new Date(slot.startsAt);
    return new Intl.DateTimeFormat('es-ES', {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit'
    }).format(date);
  }
}
