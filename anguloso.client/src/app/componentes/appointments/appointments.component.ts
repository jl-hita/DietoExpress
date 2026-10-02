import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { forkJoin } from 'rxjs';
import { PatientAppointment, PatientPortalService, AvailabilityRule } from '../../servicios/patient-portal.service';

@Component({
  selector: 'app-appointments',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './appointments.component.html',
  styleUrl: './appointments.component.css'
})
export class AppointmentsComponent implements OnInit {
  appointments: PatientAppointment[] = [];
  availability: AvailabilityRule[] = [];
  loading = true;
  saving = false;
  error: string | null = null;
  success: string | null = null;

  readonly days = [
    { value: 1, label: 'Lunes' }, { value: 2, label: 'Martes' },
    { value: 3, label: 'Miércoles' }, { value: 4, label: 'Jueves' },
    { value: 5, label: 'Viernes' }, { value: 6, label: 'Sábado' },
    { value: 0, label: 'Domingo' }
  ];

  newDay = 1;
  newStart = '09:00';
  newEnd = '14:00';
  newSlot = 30;

  constructor(private portalService: PatientPortalService) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading = true;
    this.error = null;
    const from = new Date();
    from.setDate(from.getDate() - 90);
    const to = new Date();
    to.setDate(to.getDate() + 60);
    let pending = 2;
    const done = () => { pending--; if (pending === 0) this.loading = false; };

    this.portalService.getProfessionalAppointments(from.toISOString(), to.toISOString()).subscribe({
      next: value => { this.appointments = value; done(); },
      error: err => { this.error = err?.error?.message || 'No hemos podido cargar las citas.'; done(); }
    });
    this.portalService.getAvailability().subscribe({
      next: value => { this.availability = value; done(); },
      error: err => { this.error = err?.error?.message || 'No hemos podido cargar la disponibilidad.'; done(); }
    });
  }

  get upcoming(): PatientAppointment[] {
    return this.appointments.filter(a => a.startsAt && new Date(a.startsAt).getTime() >= Date.now() && a.status !== 'cancelled')
      .sort((a, b) => new Date(a.startsAt).getTime() - new Date(b.startsAt).getTime());
  }

  get history(): PatientAppointment[] {
    return this.appointments.filter(a => !this.upcoming.some(u => u.id === a.id))
      .sort((a, b) => new Date(b.startsAt).getTime() - new Date(a.startsAt).getTime());
  }

  get pendingCount(): number { return this.appointments.filter(a => a.status === 'requested').length; }

  rulesFor(day: number): AvailabilityRule[] {
    return this.availability.filter(a => a.dayOfWeek === day).sort((a, b) => a.startTime.localeCompare(b.startTime));
  }

  addAvailability(): void {
    this.success = null; this.error = null;
    this.saving = true;

    const daysToSave = this.newDay === -1
      ? [1, 2, 3, 4, 5]
      : [this.newDay];

    const requests = daysToSave.map(day => this.portalService.saveAvailability({
      dayOfWeek: day,
      startTime: this.newStart,
      endTime: this.newEnd,
      slotMinutes: Number(this.newSlot),
      isActive: true
    }));

    forkJoin(requests).subscribe({
      next: rules => {
        for (const rule of rules) {
          const index = this.availability.findIndex(a => a.id === rule.id);
          if (index >= 0) this.availability[index] = rule;
          else this.availability.push(rule);
        }
        this.availability = [...this.availability];
        this.success = this.newDay === -1
          ? 'Disponibilidad de lunes a viernes guardada.'
          : 'Disponibilidad guardada.';
        this.saving = false;
      },
      error: err => {
        this.error = err?.error?.message || 'No hemos podido guardar el horario.';
        this.saving = false;
      }
    });
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

  statusConfirmation(status: string, patient: string): string {
    const action = ({ confirmed: 'confirmar', cancelled: 'cancelar', completed: 'marcar como completada', no_show: 'marcar como no asistida' } as Record<string, string>)[status] || 'actualizar';
    return '¿Quieres ' + action + ' la cita de ' + patient + '?';
  }

  formatDate(value: string): string {
    return new Intl.DateTimeFormat('es-ES', { weekday: 'long', day: 'numeric', month: 'long' }).format(new Date(value));
  }

  formatTime(value: string): string {
    return new Intl.DateTimeFormat('es-ES', { hour: '2-digit', minute: '2-digit' }).format(new Date(value));
  }

  dayLabel(day: number): string {
    return this.days.find(d => d.value === day)?.label || '';
  }

  statusLabel(status: string): string {
    return ({ requested: 'Pendiente', confirmed: 'Confirmada', cancelled: 'Cancelada', completed: 'Completada', no_show: 'No asistió' } as Record<string, string>)[status] || status;
  }
}