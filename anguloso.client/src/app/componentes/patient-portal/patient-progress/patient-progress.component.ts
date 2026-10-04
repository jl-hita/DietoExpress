import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { PatientCheckinHistoryComponent } from '../patient-checkin-history/patient-checkin-history.component';

@Component({
  selector: 'app-patient-progress',
  standalone: true,
  imports: [CommonModule, MatIconModule, PatientCheckinHistoryComponent],
  templateUrl: './patient-progress.component.html',
  styleUrls: ['./patient-progress.component.css']
})
export class PatientProgressComponent {
  @Input() profile: any = null;
  @Input() weightHistory: any[] = [];
  @Input() bmi: number | null = null;
  @Input() bmiColor = '';
  @Input() bmiCategory = '';
  @Input() weightChangeIcon = '';
  @Input() weightChangeText = '';
  @Input() weightChangeLabel = '';
  @Input() weightChange: number | null = null;
  @Input() isPreview = false;

  formatWeightDate(dateValue: string | Date): string {
    return new Intl.DateTimeFormat('es-ES', { day: '2-digit', month: 'short' }).format(new Date(dateValue));
  }

  getWeightBarHeight(weight: number, history: any[]): number {
    const values = history.map(item => Number(item.weight)).filter(value => Number.isFinite(value));
    if (!values.length) return 0;
    const min = Math.min(...values);
    const max = Math.max(...values);
    if (max === min) return 60;
    return 20 + ((Number(weight) - min) / (max - min)) * 80;
  }
}
