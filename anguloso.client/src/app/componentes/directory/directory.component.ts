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
