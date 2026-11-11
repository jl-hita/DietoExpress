import { CommonModule } from '@angular/common';
import { Component, Inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { EQUIPMENT_GROUPS, EQUIPMENT_OPTIONS, EquipmentOption, parseEquipmentCsv } from './training-equipment.catalog';

/** Diálogo para elegir el material disponible con nombres en español; devuelve los códigos que usa el servidor. */
@Component({
  selector: 'app-equipment-picker-dialog',
  standalone: true,
  imports: [CommonModule, FormsModule, MatDialogModule],
  template: `
    <h2 mat-dialog-title>Material disponible</h2>
    <mat-dialog-content>
      <p class="hint">Marca solo el material que tiene el cliente. <strong>Sin ninguna selección se asume que dispone de todo el material.</strong></p>
      <input class="search" type="search" [(ngModel)]="query" name="equipmentSearch" placeholder="Buscar material…" aria-label="Buscar material">
      <fieldset *ngFor="let group of groups" [hidden]="!visible(group).length">
        <legend>{{ group }}</legend>
        <label *ngFor="let option of visible(group)" class="row">
          <input type="checkbox" [checked]="selected.has(option.code)" (change)="toggle(option.code, $any($event.target).checked)">
          <span>{{ option.label }}<small *ngIf="option.hint"> · {{ option.hint }}</small></span>
        </label>
      </fieldset>
      <fieldset *ngIf="unknown.length">
        <legend>Otros (valores personalizados)</legend>
        <label *ngFor="let code of unknown" class="row">
          <input type="checkbox" [checked]="selected.has(code)" (change)="toggle(code, $any($event.target).checked)">
          <span>{{ code }}</span>
        </label>
      </fieldset>
      <p class="empty" *ngIf="query && !anyVisible">Ningún material coincide con la búsqueda.</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button type="button" class="link" (click)="clear()" [disabled]="!selected.size">Quitar selección</button>
      <button type="button" (click)="cancel()">Cancelar</button>
      <button type="button" class="primary" (click)="apply()">Aplicar ({{ selected.size || 'todo' }})</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .hint{margin:0 0 12px;color:#475569;font-size:.9rem}
    .search{width:100%;box-sizing:border-box;padding:9px 12px;margin-bottom:10px;border:1px solid #cbd5e1;border-radius:8px;font:inherit}
    fieldset{border:0;border-top:1px solid #e2e8f0;margin:0 0 6px;padding:8px 0}
    legend{padding:0 8px 0 0;font-weight:600;font-size:.82rem;letter-spacing:.03em;text-transform:uppercase;color:#64748b}
    fieldset[hidden]{display:none}
    .row{display:flex;align-items:center;gap:10px;padding:6px 2px;cursor:pointer}
    .row input{width:18px;height:18px;flex:none}
    small{color:#64748b}
    .empty{color:#64748b}
    button{padding:8px 16px;border-radius:8px;border:1px solid #cbd5e1;background:#fff;font:inherit;cursor:pointer}
    button.primary{background:#0d9488;border-color:#0d9488;color:#fff}
    button.link{border:0;background:none;color:#0f766e;text-decoration:underline}
    button:disabled{opacity:.5;cursor:default}
  `]
})
export class EquipmentPickerDialogComponent {
  readonly groups = EQUIPMENT_GROUPS;
  query = '';
  selected: Set<string>;
  /** Códigos que ya venían guardados pero no están en el catálogo: se conservan para no borrar datos sin avisar. */
  readonly unknown: string[];

  constructor(private readonly ref: MatDialogRef<EquipmentPickerDialogComponent, string[] | undefined>,
              @Inject(MAT_DIALOG_DATA) data: { csv: string }) {
    const codes = parseEquipmentCsv(data?.csv);
    this.selected = new Set(codes);
    this.unknown = codes.filter(code => !EQUIPMENT_OPTIONS.some(option => option.code === code));
  }

  visible(group: string): EquipmentOption[] {
    const q = this.query.trim().toLowerCase();
    return EQUIPMENT_OPTIONS.filter(option => option.group === group &&
      (!q || option.label.toLowerCase().includes(q) || option.code.includes(q)));
  }
  get anyVisible(): boolean { return this.groups.some(group => this.visible(group).length > 0); }

  toggle(code: string, checked: boolean): void {
    const next = new Set(this.selected);
    if (checked) next.add(code); else next.delete(code);
    this.selected = next;
  }
  clear(): void { this.selected = new Set(); }
  cancel(): void { this.ref.close(undefined); }
  apply(): void {
    const ordered = [...EQUIPMENT_OPTIONS.map(option => option.code), ...this.unknown].filter(code => this.selected.has(code));
    this.ref.close(ordered);
  }
}
