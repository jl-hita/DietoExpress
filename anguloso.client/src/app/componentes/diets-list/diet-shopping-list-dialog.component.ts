import { Component, Inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTabsModule } from '@angular/material/tabs';
import { DietService } from '../../servicios/diet.service';
import { ShoppingListCategory } from '../../modelos/shopping-list';

export interface DietShoppingListDialogData {
  dietId: number;
  dietName: string;
}

@Component({
  selector: 'app-diet-shopping-list-dialog',
  standalone: true,
  imports: [
    CommonModule,
    MatDialogModule,
    MatButtonModule,
    MatIconModule,
    MatProgressSpinnerModule,
    MatTabsModule
  ],
  template: `
    <div class="shopping-dialog">
      <div mat-dialog-title class="dialog-header">
        <div style="display: flex; align-items: center; gap: 8px;">
          <mat-icon color="primary">shopping_cart</mat-icon>
          <div>
            <h2 style="margin: 0; font-size: 18px; line-height: 1.2;">Lista de la Compra Semanal</h2>
            <span style="font-size: 12px; color: #64748b; font-weight: normal;">
              Plan: <strong>{{ data.dietName }}</strong>
            </span>
          </div>
        </div>
        <button mat-icon-button (click)="dialogRef.close()">
          <mat-icon>close</mat-icon>
        </button>
      </div>

      <mat-dialog-content class="dialog-content">
        <div *ngIf="loading" class="loading-state">
          <mat-spinner diameter="40"></mat-spinner>
          <p>Agregando y normalizando ingredientes de la semana...</p>
        </div>

        <div *ngIf="!loading && (!categories || categories.length === 0)" class="empty-state">
          <mat-icon style="font-size: 48px; width: 48px; height: 48px; color: #94a3b8;">shopping_basket</mat-icon>
          <p>No se encontraron ingredientes en esta dieta.</p>
        </div>

        <div *ngIf="!loading && categories && categories.length > 0" class="categories-list">
          <div *ngFor="let cat of categories" class="category-block">
            <div class="category-header">
              <mat-icon class="cat-icon">{{ getCategoryIcon(cat.category) }}</mat-icon>
              <span class="cat-title">{{ cat.category }}</span>
              <span class="cat-count">({{ cat.items?.length || 0 }})</span>
            </div>

            <div class="items-table">
              <div *ngFor="let item of cat.items" class="item-row">
                <div class="item-info">
                  <span class="item-name">{{ item.foodName }}</span>
                  <span *ngIf="item.commercialDescription" class="commercial-hint">
                    {{ item.commercialDescription }}
                  </span>
                </div>
                <div class="item-amounts">
                  <span class="rounded-pill" *ngIf="item.roundedGrams">
                    {{ item.roundedGrams >= 1000 ? (item.roundedGrams / 1000 | number:'1.1-2') + ' kg' : (item.roundedGrams | number:'1.0-0') + ' g' }}
                  </span>
                  <span class="exact-amount">
                    {{ item.commercialDescription || ('dieta: ' + (item.totalGrams | number:'1.0-1') + ' g') }}
                  </span>
                </div>
              </div>
            </div>
          </div>
        </div>
      </mat-dialog-content>

      <mat-dialog-actions align="end" class="dialog-actions">
        <button mat-button (click)="dialogRef.close()">Cerrar</button>
        <button mat-raised-button color="primary" (click)="printList()" [disabled]="loading || !categories.length">
          <mat-icon>print</mat-icon> Imprimir Lista
        </button>
      </mat-dialog-actions>
    </div>
  `,
  styles: [`
    .shopping-dialog {
      min-width: 380px;
      max-width: 650px;
      display: flex;
      flex-direction: column;
      max-height: 85vh;
    }
    .dialog-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 16px 20px 8px;
      margin: 0;
      border-bottom: 1px solid #f1f5f9;
    }
    .dialog-content {
      padding: 16px 20px !important;
      overflow-y: auto;
    }
    .loading-state, .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      padding: 40px 20px;
      gap: 12px;
      color: #64748b;
      font-size: 13px;
    }
    .categories-list {
      display: flex;
      flex-direction: column;
      gap: 16px;
    }
    .category-block {
      background: #f8fafc;
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      overflow: hidden;
    }
    .category-header {
      background: #eef2ff;
      border-bottom: 1px solid #e0e7ff;
      padding: 8px 12px;
      display: flex;
      align-items: center;
      gap: 8px;
      font-weight: 600;
      font-size: 13px;
      color: #3730a3;
    }
    .cat-icon {
      font-size: 18px;
      width: 18px;
      height: 18px;
      color: #4f46e5;
    }
    .cat-count {
      font-size: 11px;
      color: #6b7280;
      font-weight: normal;
    }
    .items-table {
      display: flex;
      flex-direction: column;
    }
    .item-row {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 9px 14px;
      border-bottom: 1px solid #f1f5f9;
      background: white;
    }
    .item-row:last-child {
      border-bottom: none;
    }
    .item-info {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }
    .item-name {
      font-size: 13px;
      font-weight: 500;
      color: #1e293b;
    }
    .commercial-hint {
      font-size: 11px;
      color: #2563eb;
      font-weight: 500;
    }
    .item-amounts {
      display: flex;
      align-items: center;
      gap: 8px;
      text-align: right;
    }
    .rounded-pill {
      background: #ecfdf5;
      color: #047857;
      border: 1px solid #a7f3d0;
      font-weight: 700;
      font-size: 12px;
      padding: 2px 8px;
      border-radius: 9999px;
      white-space: nowrap;
    }
    .exact-amount {
      font-size: 11px;
      color: #94a3b8;
      white-space: nowrap;
    }
    .dialog-actions {
      padding: 12px 20px !important;
      border-top: 1px solid #f1f5f9;
      margin: 0;
    }
  `]
})
// Documentación: este componente coordina estado local, validación y llamadas asíncronas; la vista solo refleja ese estado.
export class DietShoppingListDialogComponent implements OnInit {
  loading = true;
  categories: ShoppingListCategory[] = [];

  constructor(
    private dietService: DietService,
    public dialogRef: MatDialogRef<DietShoppingListDialogComponent>,
    @Inject(MAT_DIALOG_DATA) public data: DietShoppingListDialogData
  ) {}

  // La agregación y normalización de ingredientes se realiza en el backend para trabajar con las cantidades consolidadas de toda la dieta.
  ngOnInit(): void {
    if (this.data?.dietId) {
      this.dietService.getDietShoppingList(this.data.dietId).subscribe({
        next: (res) => {
          this.categories = res || [];
          this.loading = false;
        },
        error: (err) => {
          console.error('Error al cargar lista de la compra', err);
          this.loading = false;
        }
      });
    } else {
      this.loading = false;
    }
  }

  // Las categorías proceden del backend y pueden variar en idioma o acentuación; se normalizan antes de elegir el icono visual.
  getCategoryIcon(cat: string): string {
    const c = (cat || '').toLowerCase();
    if (c.includes('verdura') || c.includes('hortaliz')) return 'eco';
    if (c.includes('fruta')) return 'apple';
    if (c.includes('carne') || c.includes('ave')) return 'restaurant';
    if (c.includes('pescado') || c.includes('marisco')) return 'set_meal';
    if (c.includes('huevo')) return 'egg';
    if (c.includes('lácteo') || c.includes('lacteo')) return 'local_drink';
    if (c.includes('cereal') || c.includes('pasta') || c.includes('pan')) return 'bakery_dining';
    if (c.includes('legumbre')) return 'grain';
    if (c.includes('aceite') || c.includes('grasa')) return 'opacity';
    if (c.includes('fruto seco')) return 'spa';
    return 'category';
  }

  printList(): void {
    window.print();
  }
}
