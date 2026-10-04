import { Component, Input, OnInit, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { PatientCheckin, PatientPortalService } from '../../../servicios/patient-portal.service';

@Component({
  selector: 'app-patient-checkin-history',
  standalone: true,
  imports: [CommonModule, DatePipe, MatIconModule],
  templateUrl: './patient-checkin-history.component.html',
  styleUrls: ['./patient-checkin-history.component.css']
})
export class PatientCheckinHistoryComponent implements OnInit, OnChanges {
  @Input() enabled = true;
  checkinHistory: PatientCheckin[] = [];

  constructor(private portalService: PatientPortalService) {}

  ngOnInit(): void {
    if (this.enabled) this.loadHistory();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['enabled']?.currentValue === true && changes['enabled'].previousValue === false) {
      this.loadHistory();
    }
  }

  private loadHistory(): void {
    this.portalService.getCheckinHistory().subscribe({
      next: history => this.checkinHistory = history || [],
      error: () => this.checkinHistory = []
    });
  }
}
