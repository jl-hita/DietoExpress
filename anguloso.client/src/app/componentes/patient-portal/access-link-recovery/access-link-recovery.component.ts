import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

@Component({
  selector: 'app-access-link-recovery',
  standalone: true,
  imports: [CommonModule, FormsModule, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './access-link-recovery.component.html',
  styleUrls: ['./access-link-recovery.component.css']
})
export class AccessLinkRecoveryComponent {
  @Input() error: string | null = null;
  @Input() message: string | null = null;
  @Input() loading = false;
  @Output() request = new EventEmitter<string>();

  email = '';

  submit(): void {
    if (this.email.trim()) this.request.emit(this.email.trim());
  }
}
