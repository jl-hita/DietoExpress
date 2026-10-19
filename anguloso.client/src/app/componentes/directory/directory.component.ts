import { CommonModule } from '@angular/common';
import { Component, OnInit, ChangeDetectionStrategy, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { DirectoryProfile, PublicAvailabilitySlot, DirectoryReviewSummary } from './directory.models';
import { DirectoryService } from './directory.service';
import { PublicSeoService } from '../../servicios/public-seo.service';
import { PublicFunnelAnalyticsService } from '../../servicios/public-funnel-analytics.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
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
  goal = '';
  availableOnly = false;
  private readonly profilesState = signal<DirectoryProfile[]>([]);
  get profiles(): DirectoryProfile[] { return this.profilesState(); }
  set profiles(value: DirectoryProfile[]) { this.profilesState.set(value); }

  private readonly profileState = signal<DirectoryProfile | null>(null);
  get profile(): DirectoryProfile | null { return this.profileState(); }
  set profile(value: DirectoryProfile | null) { this.profileState.set(value); }

  private readonly loadingState = signal(false);
  get loading(): boolean { return this.loadingState(); }
  set loading(value: boolean) { this.loadingState.set(value); }

  private readonly errorState = signal('');
  get error(): string { return this.errorState(); }
  set error(value: string) { this.errorState.set(value); }

  private readonly availabilityState = signal<PublicAvailabilitySlot[]>([]);
  get availability(): PublicAvailabilitySlot[] { return this.availabilityState(); }
  set availability(value: PublicAvailabilitySlot[]) { this.availabilityState.set(value); }

  private readonly reviewSummaryState = signal<DirectoryReviewSummary | null>(null);
  get reviewSummary(): DirectoryReviewSummary | null { return this.reviewSummaryState(); }
  set reviewSummary(value: DirectoryReviewSummary | null) { this.reviewSummaryState.set(value); }

  private readonly availabilityLoadingState = signal(false);
  get availabilityLoading(): boolean { return this.availabilityLoadingState(); }
  set availabilityLoading(value: boolean) { this.availabilityLoadingState.set(value); }

  private readonly availabilityErrorState = signal('');
  get availabilityError(): string { return this.availabilityErrorState(); }
  set availabilityError(value: string) { this.availabilityErrorState.set(value); }

  private readonly selectedSlotState = signal<PublicAvailabilitySlot | null>(null);
  get selectedSlot(): PublicAvailabilitySlot | null { return this.selectedSlotState(); }
  set selectedSlot(value: PublicAvailabilitySlot | null) { this.selectedSlotState.set(value); }

  private readonly bookingSubmittingState = signal(false);
  get bookingSubmitting(): boolean { return this.bookingSubmittingState(); }
  set bookingSubmitting(value: boolean) { this.bookingSubmittingState.set(value); }

  private readonly bookingErrorState = signal('');
  get bookingError(): string { return this.bookingErrorState(); }
  set bookingError(value: string) { this.bookingErrorState.set(value); }

  private readonly bookingSuccessState = signal<string | null>(null);
  get bookingSuccess(): string | null { return this.bookingSuccessState(); }
  set bookingSuccess(value: string | null) { this.bookingSuccessState.set(value); }
  bookingFullName = '';
  bookingEmail = '';
  bookingPhone = '';
  bookingNotes = '';
  interestConsent = false;
  bookingModality: 'in_person' | 'online' = 'in_person';
  calendarMonth = new Date(new Date().getFullYear(), new Date().getMonth(), 1);
  private readonly selectedAvailabilityDateState = signal<string | null>(null);
  get selectedAvailabilityDate(): string | null { return this.selectedAvailabilityDateState(); }
  set selectedAvailabilityDate(value: string | null) { this.selectedAvailabilityDateState.set(value); }

  constructor(
    private readonly route: ActivatedRoute,
    private readonly directoryService: DirectoryService,
    private readonly publicSeoService: PublicSeoService,
    private readonly publicFunnelAnalytics: PublicFunnelAnalyticsService
  ) {}

  ngOnInit(): void {
    const slug = this.route.snapshot.paramMap.get('slug');
    const city = this.route.snapshot.paramMap.get('city');
    const speciality = this.route.snapshot.paramMap.get('speciality');
    const onlineRoute = this.route.snapshot.routeConfig?.path === 'nutricionistas/online';
    if (slug) this.loadProfile(slug);
    else {
      this.city = city || '';
      this.speciality = speciality ? speciality.replace(/-/g, ' ') : '';
      this.online = onlineRoute;
      this.availableOnly = false;
      this.publicSeoService.setDirectorySeo({ city: this.city, province: this.province, speciality: this.speciality, online: this.online });
      this.publicFunnelAnalytics.track('directory_view');
      this.search();
    }
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
      return { date, dayNumber: date.getDate(), key, slotCount, inCurrentMonth: date.getMonth() === month,
        disabled: date < new Date(new Date().setHours(0, 0, 0, 0)) || slotCount === 0 };
    });
  }

  // El calendario y la lista de horas dependen de este estado. Mantenerlo como signal
  // garantiza que Angular refresque la lista en el mismo clic que selecciona el día.
  private readonly selectedDateSlotsState = signal<PublicAvailabilitySlot[]>([]);
  get selectedDateSlots(): PublicAvailabilitySlot[] { return this.selectedDateSlotsState(); }
  set selectedDateSlots(value: PublicAvailabilitySlot[]) { this.selectedDateSlotsState.set(value); }

  get canGoToPreviousMonth(): boolean {
    return this.calendarMonth > new Date(new Date().getFullYear(), new Date().getMonth(), 1);
  }

  get canGoToNextMonth(): boolean {
    if (!this.availability.length) return false;
    const last = new Date(Math.max(...this.availability.map(slot => new Date(slot.startsAt).getTime())));
    return this.calendarMonth < new Date(last.getFullYear(), last.getMonth(), 1);
  }

  previousMonth(): void {
    if (!this.canGoToPreviousMonth) return;
    this.calendarMonth = new Date(this.calendarMonth.getFullYear(), this.calendarMonth.getMonth() - 1, 1);
    this.selectedAvailabilityDate = null;
    this.selectedDateSlots = [];
    this.selectedSlot = null;
  }

  nextMonth(): void {
    if (!this.canGoToNextMonth) return;
    this.calendarMonth = new Date(this.calendarMonth.getFullYear(), this.calendarMonth.getMonth() + 1, 1);
    this.selectedAvailabilityDate = null;
    this.selectedDateSlots = [];
    this.selectedSlot = null;
  }

  selectAvailabilityDate(key: string): void {
    const slots = this.slotsForDate(key);
    if (!slots.length) return;
    this.selectedAvailabilityDate = key;
    this.selectedDateSlots = slots;
    this.selectedSlot = null;
    this.bookingError = '';
  }

  slotsForDate(key: string): PublicAvailabilitySlot[] {
    return this.availability.filter(slot => this.dateKey(new Date(slot.startsAt)) === key);
  }

  dateKey(date: Date): string {
    return date.getFullYear() + '-' + String(date.getMonth() + 1).padStart(2, '0') + '-' + String(date.getDate()).padStart(2, '0');
  }

  monthLabel(): string {
    return new Intl.DateTimeFormat('es-ES', { month: 'long', year: 'numeric' }).format(this.calendarMonth);
  }

  formatDaySlot(slot: PublicAvailabilitySlot): string {
    return new Intl.DateTimeFormat('es-ES', { hour: '2-digit', minute: '2-digit' }).format(new Date(slot.startsAt));
  }

  trackProfileClick(slug: string): void { this.publicFunnelAnalytics.track('profile_selected', slug); }

  async shareProfile(): Promise<void> {
    if (!this.profile) return;
    const url = window.location.href;
    const text = `Consulta el perfil de ${this.profile.fullName} en DietoExpress`;
    try {
      if (navigator.share) await navigator.share({ title: this.profile.fullName, text, url });
      else await navigator.clipboard.writeText(url);
    } catch { /* El usuario puede cancelar el diálogo nativo sin que sea un error. */ }
  }

  search(): void {
    this.publicSeoService.setDirectorySeo({ city: this.city, province: this.province, speciality: this.speciality, online: this.online });
    this.loading = true; this.error = ''; this.profile = null;
    this.directoryService.search(this.city, this.province, this.speciality, this.online, this.goal, this.availableOnly).subscribe({
      next: profiles => { this.profiles = profiles; this.loading = false; },
      error: () => { this.error = 'No se ha podido cargar el directorio.'; this.loading = false; }
    });
  }

  loadProfile(slug: string): void {
    this.loading = true; this.error = '';
    this.directoryService.getBySlug(slug).subscribe({
      next: profile => {
        this.profile = profile;
        this.publicSeoService.setProfileSeo(profile);
        this.publicFunnelAnalytics.track('profile_view', profile.slug);
        this.loading = false;
        this.loadAvailability(profile.slug);
        this.loadReviews(profile.slug);
      },
      error: () => { this.error = 'No se ha encontrado el profesional solicitado.'; this.loading = false; }
    });
  }

  loadReviews(slug: string): void {
    this.directoryService.getReviews(slug).subscribe({
      next: summary => this.reviewSummary = summary,
      error: () => this.reviewSummary = null
    });
  }

  loadAvailability(slug: string): void {
    this.availabilityLoading = true; this.availabilityError = '';
    this.directoryService.getAvailability(slug).subscribe({
      next: slots => {
        this.availability = slots;
        if (slots.length) {
          const first = new Date(slots[0].startsAt);
          this.calendarMonth = new Date(first.getFullYear(), first.getMonth(), 1);
        }
        this.selectedAvailabilityDate = null;
        this.selectedDateSlots = [];
        this.selectedSlot = null;
        this.availabilityLoading = false;
      },
      error: () => { this.availability = []; this.selectedAvailabilityDate = null; this.selectedDateSlots = []; this.availabilityError = 'No se ha podido consultar la disponibilidad.'; this.availabilityLoading = false; }
    });
  }

  selectSlot(slot: PublicAvailabilitySlot): void {
    this.selectedSlot = slot; this.bookingError = ''; this.bookingSuccess = null; this.bookingModality = 'in_person';
  }

  clearSelectedSlot(): void { this.selectedSlot = null; this.bookingError = ''; }

  requestAppointment(): void {
    if (!this.profile || !this.selectedSlot || this.bookingSubmitting) return;
    const fullName = this.bookingFullName.trim();
    const email = this.bookingEmail.trim().toLowerCase();
    const phone = this.bookingPhone.trim();
    const notes = this.bookingNotes.trim();
    if (!fullName || !email) { this.bookingError = 'El nombre y el email son obligatorios.'; return; }
    if (notes.length > 500) { this.bookingError = 'El comentario no puede superar los 500 caracteres.'; return; }

    this.bookingSubmitting = true; this.bookingError = ''; this.bookingSuccess = null;
    this.publicFunnelAnalytics.track('booking_started', this.profile.slug);
    this.directoryService.requestAppointment(this.profile.slug, {
      startsAt: this.selectedSlot.startsAt,
      durationMinutes: Math.round((new Date(this.selectedSlot.endsAt).getTime() - new Date(this.selectedSlot.startsAt).getTime()) / 60000),
      fullName, email, phone: phone || undefined, patientNotes: notes || undefined,
      modality: this.bookingModality
    }).subscribe({
      next: () => {
        this.bookingSuccess = 'Solicitud enviada para el ' + this.formatSlot(this.selectedSlot!) + '. El profesional deberá confirmarla.';
        this.bookingSubmitting = false; this.selectedSlot = null; this.bookingNotes = ''; this.bookingModality = 'in_person';
        this.loadAvailability(this.profile!.slug);
      },
      error: error => {
        this.bookingSubmitting = false;
        const responseError = error?.error;
        const serverMessage = typeof responseError === 'string'
          ? responseError
          : responseError?.message;
        this.bookingError = typeof serverMessage === 'string' && serverMessage.trim()
          ? serverMessage
          : 'No se ha podido enviar la solicitud. El horario puede haber sido reservado por otra persona.';
        this.loadAvailability(this.profile!.slug);
      }
    });
  }

  submitInterest(): void {
    if (!this.profile || this.profile.canAcceptAppointments || this.bookingSubmitting) return;
    const fullName = this.bookingFullName.trim();
    const email = this.bookingEmail.trim().toLowerCase();
    const phone = this.bookingPhone.trim();
    if (!fullName || !email) { this.bookingError = 'El nombre y el email son obligatorios.'; return; }
    if (!this.interestConsent) { this.bookingError = 'Debes aceptar el tratamiento de tus datos para enviar la solicitud.'; return; }

    this.bookingSubmitting = true;
    this.bookingError = '';
    this.bookingSuccess = null;
    this.directoryService.submitInterest(this.profile.slug, {
      fullName, email, phone: phone || undefined, consentToContact: this.interestConsent
    }).subscribe({
      next: result => {
        this.bookingSuccess = result.message;
        this.bookingSubmitting = false;
        this.bookingFullName = '';
        this.bookingEmail = '';
        this.bookingPhone = '';
        this.interestConsent = false;
      },
      error: error => {
        this.bookingSubmitting = false;
        const responseError = error?.error;
        const serverMessage = typeof responseError === 'string' ? responseError : responseError?.message;
        this.bookingError = typeof serverMessage === 'string' && serverMessage.trim()
          ? serverMessage
          : 'No se ha podido registrar tu interés. Inténtalo de nuevo más tarde.';
      }
    });
  }

  formatSlot(slot: PublicAvailabilitySlot): string {
    return new Intl.DateTimeFormat('es-ES', { weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' }).format(new Date(slot.startsAt));
  }
}
