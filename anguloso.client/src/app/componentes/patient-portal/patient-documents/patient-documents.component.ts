import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule, DatePipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PatientDocument } from '../../../servicios/patient-portal.service';

@Component({
  selector: 'app-patient-documents',
  standalone: true,
  imports: [CommonModule, DatePipe, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './patient-documents.component.html',
  styleUrls: ['./patient-documents.component.css']
})
export class PatientDocumentsComponent {
  @Input() documents: PatientDocument[] = [];
  @Input() loading = false;
  @Input() error: string | null = null;

  @Output() retry = new EventEmitter<void>();
  @Output() download = new EventEmitter<PatientDocument>();
  @Output() accept = new EventEmitter<PatientDocument>();
}
