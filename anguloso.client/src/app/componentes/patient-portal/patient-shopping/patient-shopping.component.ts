import { Component, EventEmitter, Input, OnChanges, OnInit, Output, SimpleChanges } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ShoppingListCategory, ShoppingListItem } from '../../../modelos/shopping-list';

export type PatientShoppingItem = ShoppingListItem;
export type PatientShoppingCategory = ShoppingListCategory;

@Component({
  selector: 'app-patient-shopping',
  standalone: true,
  imports: [CommonModule, DecimalPipe, MatIconModule, MatProgressSpinnerModule],
  templateUrl: './patient-shopping.component.html',
  styleUrls: ['./patient-shopping.component.css']
})
export class PatientShoppingComponent implements OnInit, OnChanges {
  @Input() shoppingList: PatientShoppingCategory[] = [];
  @Input() shoppingLoading = false;
  @Input() shoppingDataError: string | null = null;
  @Input() hasActiveDiet = false;
  @Input() clientId: number | undefined;

  @Output() retry = new EventEmitter<void>();
  @Output() backToToday = new EventEmitter<void>();
  @Output() countsChange = new EventEmitter<{ total: number; checked: number }>();

  checkedItems: Record<string, boolean> = {};

  ngOnInit(): void {
    this.loadCheckedItems();
    this.emitCounts();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['clientId'] && !changes['clientId'].firstChange) {
      // El componente puede sobrevivir a un cambio de paciente; recargar este estado evita
      // que los checks guardados localmente de un paciente aparezcan en otro.
      this.loadCheckedItems();
      this.emitCounts();
      return;
    }

    if (changes['shoppingList'] && !changes['shoppingList'].firstChange) {
      this.emitCounts();
    }
  }

  get shoppingItemCount(): number {
    return this.shoppingList.reduce(
      (total: number, category: PatientShoppingCategory) => total + (category.items?.length ?? 0), 0);
  }

  get checkedShoppingItemCount(): number {
    return this.shoppingList.reduce(
      (total: number, category: PatientShoppingCategory) => total + (category.items?.filter((item: PatientShoppingItem) =>
        this.isChecked(category.category + '_' + item.foodId)).length ?? 0), 0);
  }

  get shoppingProgressPercent(): number {
    return this.shoppingItemCount
      ? Math.round((this.checkedShoppingItemCount / this.shoppingItemCount) * 100) : 0;
  }

  getShoppingIcon(category: string): string {
    const c = (category ?? '').toLowerCase();
    if (c.includes('fruta') || c.includes('verdura') || c.includes('vegetal')) return 'eco';
    if (c.includes('carne') || c.includes('pescado') || c.includes('proteína')) return 'set_meal';
    if (c.includes('lácteo') || c.includes('lacteo') || c.includes('leche')) return 'local_cafe';
    if (c.includes('cereal') || c.includes('harina') || c.includes('pan')) return 'grain';
    return 'shopping_basket';
  }

  toggleItem(key: string): void {
    this.checkedItems[key] = !this.checkedItems[key];
    this.saveCheckedItems();
    this.emitCounts();
  }

  isChecked(key: string): boolean {
    return !!this.checkedItems[key];
  }

  clearShoppingChecks(): void {
    this.checkedItems = {};
    this.saveCheckedItems();
    this.emitCounts();
  }

  private loadCheckedItems(): void {
    this.checkedItems = {};
    const saved = localStorage.getItem(`shopping_${this.clientId}`);
    if (!saved) return;
    try {
      const parsed = JSON.parse(saved);
      if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
        this.checkedItems = Object.fromEntries(
          Object.entries(parsed).filter(([, value]) => value === true)
        ) as Record<string, boolean>;
      }
    } catch {
      // Un valor corrupto en localStorage no debe impedir el uso de la lista de la compra.
    }
  }

  private emitCounts(): void {
    this.countsChange.emit({ total: this.shoppingItemCount, checked: this.checkedShoppingItemCount });
  }

  private saveCheckedItems(): void {
    localStorage.setItem(`shopping_${this.clientId}`, JSON.stringify(this.checkedItems));
  }
}
