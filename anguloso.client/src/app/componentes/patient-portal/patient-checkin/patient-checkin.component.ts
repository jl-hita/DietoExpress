import { Component, Input, OnChanges, OnInit, SimpleChanges, ChangeDetectionStrategy, signal} from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PatientCheckin, PatientCheckinRequest, PatientPortalService } from '../../../servicios/patient-portal.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-patient-checkin',
  standalone: true,
  imports: [CommonModule, FormsModule, DatePipe, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './patient-checkin.component.html',
  styleUrls: ['./patient-checkin.component.css']
})
export class PatientCheckinComponent implements OnInit, OnChanges {
  @Input() enabled = true;

  private readonly currentCheckinState = signal<PatientCheckin | null>(null);
  get currentCheckin(): PatientCheckin | null { return this.currentCheckinState(); }
  set currentCheckin(value: PatientCheckin | null) { this.currentCheckinState.set(value); }

  private readonly checkinLoadingState = signal(false);
  get checkinLoading(): boolean { return this.checkinLoadingState(); }
  set checkinLoading(value: boolean) { this.checkinLoadingState.set(value); }

  private readonly checkinSavingState = signal(false);
  get checkinSaving(): boolean { return this.checkinSavingState(); }
  set checkinSaving(value: boolean) { this.checkinSavingState.set(value); }

  private readonly checkinErrorState = signal<string | null>(null);
  get checkinError(): string | null { return this.checkinErrorState(); }
  set checkinError(value: string | null) { this.checkinErrorState.set(value); }

  private readonly checkinSuccessState = signal(false);
  get checkinSuccess(): boolean { return this.checkinSuccessState(); }
  set checkinSuccess(value: boolean) { this.checkinSuccessState.set(value); }

  private readonly checkinWeightState = signal<number | null>(null);
  get checkinWeight(): number | null { return this.checkinWeightState(); }
  set checkinWeight(value: number | null) { this.checkinWeightState.set(value); }

  private readonly checkinAdherenceState = signal<number | null>(null);
  get checkinAdherence(): number | null { return this.checkinAdherenceState(); }
  set checkinAdherence(value: number | null) { this.checkinAdherenceState.set(value); }

  private readonly checkinHungerState = signal<number | null>(null);
  get checkinHunger(): number | null { return this.checkinHungerState(); }
  set checkinHunger(value: number | null) { this.checkinHungerState.set(value); }

  private readonly checkinEnergyState = signal<number | null>(null);
  get checkinEnergy(): number | null { return this.checkinEnergyState(); }
  set checkinEnergy(value: number | null) { this.checkinEnergyState.set(value); }

  private readonly checkinSleepQualityState = signal<number | null>(null);
  get checkinSleepQuality(): number | null { return this.checkinSleepQualityState(); }
  set checkinSleepQuality(value: number | null) { this.checkinSleepQualityState.set(value); }

  private readonly checkinSleepHoursState = signal<number | null>(null);
  get checkinSleepHours(): number | null { return this.checkinSleepHoursState(); }
  set checkinSleepHours(value: number | null) { this.checkinSleepHoursState.set(value); }

  private readonly checkinTrainingState = signal<number | null>(null);
  get checkinTraining(): number | null { return this.checkinTrainingState(); }
  set checkinTraining(value: number | null) { this.checkinTrainingState.set(value); }

  private readonly checkinDifficultiesState = signal('');
  get checkinDifficulties(): string { return this.checkinDifficultiesState(); }
  set checkinDifficulties(value: string) { this.checkinDifficultiesState.set(value); }

  private readonly checkinNotesState = signal('');
  get checkinNotes(): string { return this.checkinNotesState(); }
  set checkinNotes(value: string) { this.checkinNotesState.set(value); }

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

  saveCurrentCheckin(form: NgForm): void {
    // FormsModule desactiva la validación nativa del navegador (novalidate).
    // Validar antes de llamar a la API para no guardar pesos/valores fuera de rango.
    if (form.invalid) {
      form.form.markAllAsTouched();
      return;
    }

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
