import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-patient-shopping',
  standalone: true,
  imports: [CommonModule, DecimalPipe, MatIconModule],
  templateUrl: './patient-shopping.component.html',
  styleUrls: ['./patient-shopping.component.css']
})
export class PatientShoppingComponent {
  @Input() shoppingList: any[] = [];
  @Input() shoppingLoading = false;
  @Input() shoppingDataError: string | null = null;
  @Input() hasActiveDiet = false;
  @Input() clientId: number | undefined;

  @Output() retry = new EventEmitter<void>();
  @Output() backToToday = new EventEmitter<void>();

  checkedItems: Record<string, boolean> = {};

  ngOnInit(): void {
    this.loadCheckedItems();
  }

  get shoppingItemCount(): number {
    return this.shoppingList.reduce(
      (total: number, category: any) => total + (category.items?.length ?? 0), 0);
  }

  get checkedShoppingItemCount(): number {
    return this.shoppingList.reduce(
      (total: number, category: any) => total + (category.items?.filter((item: any) =>
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
  }

  isChecked(key: string): boolean {
    return !!this.checkedItems[key];
  }

  clearShoppingChecks(): void {
    this.checkedItems = {};
    this.saveCheckedItems();
  }

  private loadCheckedItems(): void {
    const saved = localStorage.getItem(`shopping_${this.clientId}`);
    if (!saved) return;
    try { this.checkedItems = JSON.parse(saved); } catch { this.checkedItems = {}; }
  }

  private saveCheckedItems(): void {
    localStorage.setItem(`shopping_${this.clientId}`, JSON.stringify(this.checkedItems));
  }
}
