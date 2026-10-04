import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { PatientNotificationsComponent } from '../patient-notifications/patient-notifications.component';
import { PatientNotification } from '../../../servicios/patient-portal.service';

@Component({
  selector: 'app-patient-header',
  standalone: true,
  imports: [CommonModule, MatIconModule, PatientNotificationsComponent],
  templateUrl: './patient-header.component.html',
  styleUrls: ['./patient-header.component.css']
})
export class PatientHeaderComponent {
  @Input() profile: any = null;
  @Input() notificationsOpen = false;
  @Input() notifications: PatientNotification[] = [];
  @Input() notificationsLoading = false;
  @Input() unreadNotificationCount = 0;
  @Input() pushSupported = false;
  @Input() pushEnabled = false;
  @Input() pushBusy = false;
  @Input() pushMessage: string | null = null;

  @Output() toggleNotifications = new EventEmitter<void>();
  @Output() logout = new EventEmitter<void>();
  @Output() markNotificationRead = new EventEmitter<PatientNotification>();
  @Output() enablePush = new EventEmitter<void>();
}
