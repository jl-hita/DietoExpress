import { CommonModule } from '@angular/common';
import { Component, OnInit, ChangeDetectionStrategy, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { forkJoin } from 'rxjs';
import { PatientAppointment, PatientPortalService, AvailabilityRule } from '../../servicios/patient-portal.service';
import { AuthService } from '../../servicios/auth.service';
import { TimeZonePreferenceService } from '../../servicios/time-zone-preference.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-appointments',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './appointments.component.html',
  styleUrl: './appointments.component.css'
})
export class AppointmentsComponent implements OnInit {
  private readonly appointmentsState = signal<PatientAppointment[]>([]);
  get appointments(): PatientAppointment[] { return this.appointmentsState(); }
  set appointments(value: PatientAppointment[]) { this.appointmentsState.set(value); }

  private readonly availabilityState = signal<AvailabilityRule[]>([]);
  get availability(): AvailabilityRule[] { return this.availabilityState(); }
  set availability(value: AvailabilityRule[]) { this.availabilityState.set(value); }

  readonly loading = signal(true);

  private readonly savingState = signal(false);
  get saving(): boolean { return this.savingState(); }
  set saving(value: boolean) { this.savingState.set(value); }

  private readonly errorState = signal<string | null>(null);
  get error(): string | null { return this.errorState(); }
  set error(value: string | null) { this.errorState.set(value); }

  private readonly successState = signal<string | null>(null);
  get success(): string | null { return this.successState(); }
  set success(value: string | null) { this.successState.set(value); }

  private readonly googleCalendarState = signal<{ connected: boolean; email: string; calendarId: string; lastSyncedAt?: string | null; reauthorizationRequired?: boolean } | null>(null);
  get googleCalendar(): { connected: boolean; email: string; calendarId: string; lastSyncedAt?: string | null; reauthorizationRequired?: boolean } | null { return this.googleCalendarState(); }
  set googleCalendar(value: { connected: boolean; email: string; calendarId: string; lastSyncedAt?: string | null; reauthorizationRequired?: boolean } | null) { this.googleCalendarState.set(value); }

  private readonly calendarBusyState = signal(false);
  get calendarBusy(): boolean { return this.calendarBusyState(); }
  set calendarBusy(value: boolean) { this.calendarBusyState.set(value); }
  isSuperAdmin = false;
  resendingAppointmentId: number | null = null;

  readonly days = [
    { value: 1, label: 'Lunes' }, { value: 2, label: 'Martes' },
    { value: 3, label: 'Miércoles' }, { value: 4, label: 'Jueves' },
    { value: 5, label: 'Viernes' }, { value: 6, label: 'Sábado' },
    { value: 0, label: 'Domingo' }
  ];

  manualClients: any[] = [];
  manualClientSearch = '';
  manualClientId: number | null = null;
  manualStartsAt = '';
  manualDuration = 30;
  manualModality = 'in_person';
  manualNotes = '';
  newDay = 1;
  newStart = '09:00';
  newEnd = '14:00';
  newSlot = 30;
  editingRuleId: number | null = null;

  constructor(private portalService: PatientPortalService, private router: Router, private route: ActivatedRoute, private authService: AuthService, private timeZones: TimeZonePreferenceService) {}

  ngOnInit(): void {
    this.isSuperAdmin = this.authService.isSuperAdmin();
    if (this.isSuperAdmin) { this.loading.set(false); return; }
    this.handleGoogleCalendarResult();
    this.load();
    this.loadGoogleCalendar();
  }

  private handleGoogleCalendarResult(): void {
    const result = this.route.snapshot.queryParamMap.get('calendar');
    if (result === 'connected') {
      this.success = 'Google Calendar conectado correctamente.';
      this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    } else if (result === 'error') {
      this.error = 'No hemos podido completar la conexión con Google Calendar. Revisa el registro del servidor para obtener más detalles.';
      this.router.navigate([], { relativeTo: this.route, queryParams: {}, replaceUrl: true });
    }
  }

  loadGoogleCalendar(): void {
    this.portalService.getGoogleCalendarStatus().subscribe({ next: value => this.googleCalendar = value, error: () => this.googleCalendar = null });
  }

  connectGoogleCalendar(): void { this.portalService.connectGoogleCalendar(); }

  // La sincronización invalida el estado visual local y vuelve a cargar citas/disponibilidad después de que el backend termine el proceso.
  syncGoogleCalendar(): void {
    this.calendarBusy = true; this.error = null;
    this.portalService.syncGoogleCalendar().subscribe({ next: () => { this.calendarBusy = false; this.loadGoogleCalendar(); this.load(); this.success = 'Google Calendar sincronizado.'; }, error: err => { this.calendarBusy = false; this.error = err?.error?.detail || err?.error?.message || 'No hemos podido sincronizar Google Calendar.'; this.loadGoogleCalendar(); } });
  }

  disconnectGoogleCalendar(): void {
    if (!confirm('¿Desconectar Google Calendar? Los eventos externos dejarán de bloquear nuevas citas.')) return;
    this.calendarBusy = true;
    this.portalService.disconnectGoogleCalendar().subscribe({ next: () => { this.calendarBusy = false; this.googleCalendar = { connected: false, email: '', calendarId: 'primary' }; this.success = 'Google Calendar desconectado.'; }, error: err => { this.calendarBusy = false; this.error = err?.error?.message || 'No hemos podido desconectar Google Calendar.'; } });
  }

  // Se cargan en paralelo citas y disponibilidad para que el calendario profesional quede operativo aunque una colección tarde más que la otra.
  // Ambas colecciones se solicitan en paralelo; el indicador global solo desaparece cuando han terminado las dos peticiones.
  load(): void {
    this.loading.set(true);
    this.error = null;
    const from = new Date();
    from.setDate(from.getDate() - 90);
    const to = new Date();
    to.setDate(to.getDate() + 60);
    let pending = 2;
    const done = () => { pending--; if (pending === 0) this.loading.set(false); };

    this.portalService.getProfessionalAppointments(from.toISOString(), to.toISOString()).subscribe({
      next: value => { this.appointments = value; done(); },
      error: err => { this.error = err?.error?.message || 'No hemos podido cargar las citas.'; done(); }
    });
    this.portalService.getAvailability().subscribe({
      next: value => { this.availability = value; done(); },
      error: err => { this.error = err?.error?.message || 'No hemos podido cargar la disponibilidad.'; done(); }
    });
    this.loadManualClients();
  }

  loadManualClients(): void {
    this.portalService.getClients(this.manualClientSearch.trim(), 1, 100).subscribe({
      next: value => { this.manualClients = Array.isArray(value) ? value : (value?.items || value?.Items || []); },
      error: () => { this.manualClients = []; }
    });
  }

  createManualAppointment(): void {
    this.error = null; this.success = null;
    if (!this.manualClientId || !this.manualStartsAt) {
      this.error = 'Selecciona un paciente y una fecha y hora.';
      return;
    }
    const startsAt = new Date(this.manualStartsAt);
    if (!Number.isFinite(startsAt.getTime()) || startsAt.getTime() <= Date.now() + 5 * 60_000) {
      this.error = 'La cita debe ser posterior a los próximos cinco minutos.';
      return;
    }
    this.saving = true;
    this.portalService.createConsultationAppointment({
      clientId: Number(this.manualClientId),
      startsAt: startsAt.toISOString(),
      durationMinutes: Number(this.manualDuration),
      notes: this.manualNotes.trim() || null,
      modality: this.manualModality
    }).subscribe({
      next: appointment => {
        const selectedClient = this.manualClients.find(client => Number(client.id ?? client.Id) === Number(this.manualClientId));
        this.appointments = [...this.appointments, { ...appointment, clientName: selectedClient?.fullName ?? selectedClient?.FullName ?? null }].sort((a, b) => new Date(a.startsAt).getTime() - new Date(b.startsAt).getTime());
        this.success = 'Cita creada y confirmada. La disponibilidad semanal solo afecta a las reservas públicas.';
        this.manualNotes = '';
        this.saving = false;
      },
      error: err => { this.error = err?.error?.message || 'No hemos podido crear la cita.'; this.saving = false; }
    });
  }

  get upcoming(): PatientAppointment[] {
    const now = Date.now();
    // Mantener en "Próximas citas" las citas cuyo intervalo todavía no ha terminado.
    // Comparar solo startsAt mandaba una cita en curso al historial y ocultaba sus acciones.
    return this.appointments.filter(a =>
      a.startsAt &&
      a.endsAt &&
      new Date(a.endsAt).getTime() > now &&
      (a.status === 'requested' || a.status === 'confirmed')
    ).sort((a, b) => new Date(a.startsAt).getTime() - new Date(b.startsAt).getTime());
  }

  get history(): PatientAppointment[] {
    return this.appointments.filter(a => !this.upcoming.some(u => u.id === a.id))
      .sort((a, b) => new Date(b.startsAt).getTime() - new Date(a.startsAt).getTime());
  }

  get pendingCount(): number { return this.appointments.filter(a => a.status === 'requested').length; }

  rulesFor(day: number): AvailabilityRule[] {
    return this.availability.filter(a => a.dayOfWeek === day).sort((a, b) => a.startTime.localeCompare(b.startTime));
  }

  // Una edición actualiza una sola regla; una alta con "lunes a viernes" crea las cinco reglas mediante forkJoin.
  // En modo edición se actualiza una regla; para la opción laboral de lunes a viernes se crean las cinco reglas de forma concurrente.
  addAvailability(): void {
    this.success = null; this.error = null; this.saving = true;
    const request = {
      dayOfWeek: this.newDay,
      startTime: this.newStart,
      endTime: this.newEnd,
      slotMinutes: Number(this.newSlot),
      isActive: true
    };

    if (this.editingRuleId !== null) {
      const id = this.editingRuleId;
      this.portalService.updateAvailability(id, request).subscribe({
        next: rule => {
          const index = this.availability.findIndex(a => a.id === id);
          if (index >= 0) this.availability[index] = rule;
          this.availability = [...this.availability];
          this.success = 'Disponibilidad actualizada.';
          this.resetAvailabilityEditor();
          this.saving = false;
        },
        error: err => { this.error = err?.error?.message || 'No hemos podido actualizar el horario.'; this.saving = false; }
      });
      return;
    }

    const daysToSave = this.newDay === -1 ? [1, 2, 3, 4, 5] : [this.newDay];
    forkJoin(daysToSave.map(day => this.portalService.saveAvailability({ ...request, dayOfWeek: day }))).subscribe({
      next: rules => {
        for (const rule of rules) {
          const index = this.availability.findIndex(a => a.id === rule.id);
          if (index >= 0) this.availability[index] = rule; else this.availability.push(rule);
        }
        this.availability = [...this.availability];
        this.success = this.newDay === -1 ? 'Disponibilidad de lunes a viernes guardada.' : 'Disponibilidad guardada.';
        this.saving = false;
      },
      error: err => { this.error = err?.error?.message || 'No hemos podido guardar el horario.'; this.saving = false; }
    });
  }

  editAvailability(rule: AvailabilityRule): void {
    this.editingRuleId = rule.id;
    this.newDay = rule.dayOfWeek;
    this.newStart = rule.startTime.slice(0, 5);
    this.newEnd = rule.endTime.slice(0, 5);
    this.newSlot = rule.slotMinutes;
    this.success = null; this.error = null;
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  cancelAvailabilityEdit(): void {
    this.resetAvailabilityEditor();
  }

  deleteAvailability(rule: AvailabilityRule): void {
    if (!confirm('¿Eliminar este horario? Solo se puede eliminar si no tiene citas futuras reservadas.')) return;
    this.saving = true; this.error = null; this.success = null;
    this.portalService.deleteAvailability(rule.id).subscribe({
      next: () => {
        this.availability = this.availability.filter(a => a.id !== rule.id);
        this.success = 'Disponibilidad eliminada.';
        this.saving = false;
      },
      error: err => { this.error = err?.error?.message || 'No hemos podido eliminar el horario.'; this.saving = false; }
    });
  }

  private resetAvailabilityEditor(): void {
    this.editingRuleId = null;
    this.newDay = 1;
    this.newStart = '09:00';
    this.newEnd = '14:00';
    this.newSlot = 30;
  }

  toggleAvailability(rule: AvailabilityRule): void {
    this.saving = true; this.error = null; this.success = null;
    this.portalService.saveAvailability({
      dayOfWeek: rule.dayOfWeek, startTime: rule.startTime, endTime: rule.endTime,
      slotMinutes: rule.slotMinutes, isActive: !rule.isActive
    }).subscribe({
      next: saved => {
        const index = this.availability.findIndex(a => a.id === saved.id);
        if (index >= 0) this.availability[index] = saved;
        this.availability = [...this.availability];
        this.saving = false;
      },
      error: err => { this.error = err?.error?.message || 'No hemos podido actualizar el horario.'; this.saving = false; }
    });
  }

  // El cambio de estado se confirma antes de enviarlo y, tras recibir la respuesta canónica del servidor, sustituye el elemento local.
  changeStatus(appointment: PatientAppointment, status: string): void {
    if (!confirm(this.statusConfirmation(status, appointment.clientName || 'el paciente'))) return;
    this.error = null; this.success = null;
    this.portalService.updateAppointmentStatus(appointment.id, status).subscribe({
      next: updated => {
        const index = this.appointments.findIndex(a => a.id === updated.id);
        if (index >= 0) this.appointments[index] = updated;
        this.appointments = [...this.appointments];
        this.success = 'Cita actualizada.';
      },
      error: err => this.error = err?.error?.message || 'No hemos podido actualizar la cita.'
    });
  }

  resendAppointmentEmail(appointment: PatientAppointment): void {
    if (this.resendingAppointmentId !== null) return;
    this.error = null;
    this.success = null;
    this.resendingAppointmentId = appointment.id;
    this.portalService.resendAppointmentEmail(appointment.id).subscribe({
      next: result => {
        this.success = result?.message || 'Email reenviado al paciente.';
        this.resendingAppointmentId = null;
      },
      error: err => {
        this.error = err?.error?.message || 'No se ha podido reenviar el email al paciente.';
        this.resendingAppointmentId = null;
      }
    });
  }

  joinVideoAppointment(appointment: PatientAppointment): void {
    this.portalService.openVideoAccess(appointment.id, err => {
      this.error = err?.error?.message || 'No hemos podido abrir la consulta online. Comprueba que la cita esté dentro de su ventana de acceso.';
    });
  }

  startConsultation(appointment: PatientAppointment): void {
    this.router.navigate(['/appointments', appointment.id, 'consultation']);
  }

  statusConfirmation(status: string, patient: string): string {
    const action = ({ confirmed: 'confirmar', cancelled: 'cancelar', completed: 'marcar como completada', no_show: 'marcar como no asistida' } as Record<string, string>)[status] || 'actualizar';
    return '¿Quieres ' + action + ' la cita de ' + patient + '?';
  }

  formatDate(value: string): string {
    return this.timeZones.formatInPreferredZone(value, { weekday: 'long', day: 'numeric', month: 'long' });
  }

  formatTime(value: string): string {
    return this.timeZones.formatInPreferredZone(value, { hour: '2-digit', minute: '2-digit' });
  }

  dayLabel(day: number): string {
    return this.days.find(d => d.value === day)?.label || '';
  }

  statusLabel(status: string, endsAt?: string): string {
    // Una solicitud sin confirmar que ya ha pasado no debe quedarse eternamente como pendiente.
    // No inferimos una ausencia: no_show solo se muestra cuando el profesional la registra.
    if (status === 'requested' && endsAt && Number.isFinite(new Date(endsAt).getTime()) && new Date(endsAt).getTime() <= Date.now()) {
      return 'Caducada';
    }
    return ({ requested: 'Pendiente', confirmed: 'Confirmada', cancelled: 'Cancelada', completed: 'Completada', no_show: 'No asistió' } as Record<string, string>)[status] || status;
  }
}