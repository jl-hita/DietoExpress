import { Component, Input, OnInit, OnChanges, SimpleChanges, ChangeDetectionStrategy, signal} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { PatientCheckin, PatientPortalService } from '../../../servicios/patient-portal.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-patient-checkin-history',
  standalone: true,
  imports: [CommonModule, DatePipe, MatIconModule],
  templateUrl: './patient-checkin-history.component.html',
  styleUrls: ['./patient-checkin-history.component.css']
})
export class PatientCheckinHistoryComponent implements OnInit, OnChanges {
  @Input() enabled = true;
  private readonly checkinHistoryState = signal<PatientCheckin[]>([]);
  get checkinHistory(): PatientCheckin[] { return this.checkinHistoryState(); }
  set checkinHistory(value: PatientCheckin[]) { this.checkinHistoryState.set(value); }

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
