import { Component, Input, OnChanges, OnInit, SimpleChanges } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PatientCheckin, PatientCheckinRequest, PatientPortalService } from '../../../servicios/patient-portal.service';

@Component({
  selector: 'app-patient-checkin',
  standalone: true,
  imports: [CommonModule, FormsModule, DatePipe, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './patient-checkin.component.html',
  styleUrls: ['./patient-checkin.component.css']
})
export class PatientCheckinComponent implements OnInit, OnChanges {
  @Input() enabled = true;

  currentCheckin: PatientCheckin | null = null;
  checkinLoading = false;
  checkinSaving = false;
  checkinError: string | null = null;
  checkinSuccess = false;
  checkinWeight: number | null = null;
  checkinAdherence: number | null = null;
  checkinHunger: number | null = null;
  checkinEnergy: number | null = null;
  checkinSleepQuality: number | null = null;
  checkinSleepHours: number | null = null;
  checkinTraining: number | null = null;
  checkinDifficulties = '';
  checkinNotes = '';

  constructor(private portalService: PatientPortalService) {}

  ngOnInit(): void {
    if (this.enabled) this.loadCurrentCheckin();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['enabled']?.currentValue === true && changes['enabled'].previousValue === false) {
      this.loadCurrentCheckin();
    }
  }

  loadCurrentCheckin(): void {
    this.checkinLoading = true;
    this.checkinError = null;
    this.checkinSuccess = false;
    this.portalService.getCurrentCheckin().subscribe({
      next: checkin => {
        this.currentCheckin = checkin;
        this.checkinLoading = false;
        if (checkin) this.applyCheckin(checkin);
      },
      error: err => {
        this.currentCheckin = null;
        this.checkinLoading = false;
        this.checkinError = err?.error?.message || 'No hemos podido cargar tu revisión semanal.';
      }
    });
  }

  saveCurrentCheckin(): void {
    this.checkinSaving = true;
    this.checkinError = null;
    this.checkinSuccess = false;

    const request: PatientCheckinRequest = {
      weight: this.checkinWeight,
      adherence: this.checkinAdherence,
      hunger: this.checkinHunger,
      energy: this.checkinEnergy,
      sleep_quality: this.checkinSleepQuality,
      sleep_hours: this.checkinSleepHours,
      training: this.checkinTraining,
      difficulties: this.checkinDifficulties.trim() || null,
      notes: this.checkinNotes.trim() || null
    };

    this.portalService.saveCheckin(request).subscribe({
      next: checkin => {
        this.currentCheckin = checkin;
        this.checkinSaving = false;
        this.checkinSuccess = true;
        this.applyCheckin(checkin);
      },
      error: err => {
        this.checkinSaving = false;
        this.checkinError = err?.error?.message || 'No hemos podido guardar tu revisión semanal. Inténtalo de nuevo.';
      }
    });
  }

  private applyCheckin(checkin: PatientCheckin): void {
    this.checkinWeight = checkin.weight ?? null;
    this.checkinAdherence = checkin.adherence ?? null;
    this.checkinHunger = checkin.hunger ?? null;
    this.checkinEnergy = checkin.energy ?? null;
    this.checkinSleepQuality = checkin.sleep_quality ?? null;
    this.checkinSleepHours = checkin.sleep_hours ?? null;
    this.checkinTraining = checkin.training ?? null;
    this.checkinDifficulties = checkin.difficulties ?? '';
    this.checkinNotes = checkin.notes ?? '';
  }
}
