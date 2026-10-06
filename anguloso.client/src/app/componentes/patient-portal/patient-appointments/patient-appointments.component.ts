import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { AppointmentSlot, PatientAppointment } from '../../../servicios/patient-portal.service';

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
  @Output() cancel = new EventEmitter<PatientAppointment>();
  @Output() joinVideo = new EventEmitter<PatientAppointment>();

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
