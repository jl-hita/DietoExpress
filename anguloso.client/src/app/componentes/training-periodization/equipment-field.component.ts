import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { EquipmentPickerDialogComponent } from './equipment-picker-dialog.component';
import { equipmentSummary } from './training-equipment.catalog';

/** Campo de material: muestra un resumen en español y abre el selector. `csv` conserva el formato histórico (códigos separados por comas). */
@Component({
  selector: 'app-equipment-field',
  standalone: true,
  imports: [CommonModule],
  template: `
    <span class="equipment-field">
      <span class="equipment-summary">{{ summary }}</span>
      <button type="button" class="secondary" (click)="open()" [disabled]="disabled">{{ csv ? 'Cambiar material…' : 'Elegir material…' }}</button>
    </span>
  `,
  styles: [`
    .equipment-field{display:flex;align-items:center;gap:10px;flex-wrap:wrap}
    .equipment-summary{flex:1 1 180px;min-width:0}
  `]
})
export class EquipmentFieldComponent {
  @Input() csv = '';
  @Input() disabled = false;
  @Output() csvChange = new EventEmitter<string>();

  constructor(private readonly dialog: MatDialog) {}

  get summary(): string { return equipmentSummary(this.csv); }

  open(): void {
    this.dialog.open<EquipmentPickerDialogComponent, { csv: string }, string[] | undefined>(EquipmentPickerDialogComponent, {
      width: '520px', maxWidth: '95vw', data: { csv: this.csv }
    }).afterClosed().subscribe(result => { if (result) this.csvChange.emit(result.join(', ')); });
  }
}
