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
  @Input() bmi: number | string | null = null;
  @Input() bmiColor = '';
  @Input() bmiCategory = '';
  @Input() weightChangeIcon = '';
  @Input() weightChangeText = '';
  @Input() weightChangeLabel = '';
  @Input() weightChange: number | null = null;
  @Input() isPreview = false;

  formatWeightDate(dateValue: string | Date): string {
    const date = new Date(dateValue);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat('es-ES', { day: '2-digit', month: 'short' }).format(date);
  }

  getWeightBarHeight(weight: number, history: any[]): number {
    if (!history?.length) return 0;
    const min = Math.min(...history.map((h: any) => h.weight));
    const max = Math.max(...history.map((h: any) => h.weight));
    if (max === min) return 60;
    return Math.round(((weight - min) / (max - min)) * 85 + 15);
  }
}
