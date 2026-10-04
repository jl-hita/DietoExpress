import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PatientNotification } from '../../../servicios/patient-portal.service';

@Component({
  selector: 'app-patient-notifications',
  standalone: true,
  imports: [CommonModule, DatePipe, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './patient-notifications.component.html',
  styleUrls: ['./patient-notifications.component.css']
})
export class PatientNotificationsComponent {
  @Input() open = false;
  @Input() notifications: PatientNotification[] = [];
  @Input() loading = false;
  @Input() unreadCount = 0;
  @Input() pushSupported = false;
  @Input() pushEnabled = false;
  @Input() pushBusy = false;
  @Input() pushMessage: string | null = null;

  @Output() markRead = new EventEmitter<PatientNotification>();
  @Output() enablePush = new EventEmitter<void>();

  notificationIcon(type: string): string {
    if (type.includes('appointment')) return 'event';
    if (type.includes('document')) return 'description';
    if (type.includes('message')) return 'chat';
    return 'notifications';
  }
}
