import { Component, ChangeDetectionStrategy, DestroyRef, inject, signal} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, FormGroup, ReactiveFormsModule, FormsModule } from '@angular/forms';
import { MatDialogRef } from '@angular/material/dialog';
import { debounceTime, distinctUntilChanged, switchMap, finalize } from 'rxjs/operators';
import { of } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MATERIAL_IMPORTS } from '../../shared/material.imports';
import { FoodService } from '../../servicios/food.service';
import { FoodProduct } from '../../modelos/food-product';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-food-search-dialog',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormsModule, MATERIAL_IMPORTS],
  template: `
    <h2 mat-dialog-title>Añadir alimento</h2>
    <mat-dialog-content>
      <mat-form-field appearance="outline" class="full-width">
        <mat-label>Buscar alimento...</mat-label>
        <input matInput [formControl]="searchCtrl" placeholder="Pollo, arroz...">
      </mat-form-field>
      <p class="food-count" *ngIf="foodCount !== null" role="status">
        {{ foodCount | number }} alimentos disponibles en la base de datos
      </p>
      <p class="food-count-error" *ngIf="foodCountError" role="alert">
        No se ha podido cargar el número de alimentos. Puedes seguir buscando con normalidad.
      </p>
      
      <div *ngIf="loading" class="loading-box">
        <mat-progress-spinner mode="indeterminate" diameter="30"></mat-progress-spinner>
      </div>

      <mat-list *ngIf="results.length > 0 && !selectedFood">
        <mat-list-item *ngFor="let f of results" (click)="selectFood(f)" class="food-item">
          <span matListItemTitle>{{ f.name }}</span>
          <span matListItemLine>{{ f.kcal || 0 }} kcal | P: {{ f.protein || 0 }}g | HC: {{ f.carbs || 0 }}g | G: {{ f.fat || 0 }}g</span>
        </mat-list-item>
      </mat-list>

      <div *ngIf="selectedFood" class="selected-food-box">
        <h3>{{ selectedFood.name }}</h3>
        <p>Introduce la cantidad (gramos/ml):</p>
        <mat-form-field appearance="outline">
          <mat-label>Cantidad en gramos</mat-label>
          <input matInput type="number" [(ngModel)]="grams" min="1">
        </mat-form-field>
        <button mat-button color="warn" (click)="selectedFood = null; grams = 100">Cambiar alimento</button>
      </div>

    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancelar</button>
      <button mat-flat-button color="primary" [disabled]="!selectedFood || grams <= 0" (click)="confirm()">Confirmar</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .full-width { width: 100%; margin-top: 10px; }
    .food-count { margin: -4px 0 12px; color: #64748b; font-size: 12px; }
    .food-count-error { margin: -4px 0 12px; color: #b45309; font-size: 12px; }
    .loading-box { display: flex; justify-content: center; margin: 10px 0; }
    .food-item { cursor: pointer; border-bottom: 1px solid #eee; }
    .food-item:hover { background: #f9f9f9; }
    .selected-food-box { padding: 15px; border: 1px solid #ccc; border-radius: 8px; margin-top: 10px; background: #fdfdfd; }
  `]
})
// La búsqueda combina el catálogo local con el servicio externo cuando procede, manteniendo el resultado adaptado al formulario de dieta.
export class FoodSearchDialogComponent {
  private readonly destroyRef = inject(DestroyRef);
  searchCtrl: any;

  private readonly resultsState = signal<any[]>([]);
  get results(): any[] { return this.resultsState(); }
  set results(value: any[]) { this.resultsState.set(value); }

  private readonly loadingState = signal(false);
  get loading(): boolean { return this.loadingState(); }
  set loading(value: boolean) { this.loadingState.set(value); }

  private readonly selectedFoodState = signal<any | null>(null);
  get selectedFood(): any | null { return this.selectedFoodState(); }
  set selectedFood(value: any | null) { this.selectedFoodState.set(value); }
  grams: number = 100;
  foodCount: number | null = null;
  foodCountError = false;

  constructor(
    private dialogRef: MatDialogRef<FoodSearchDialogComponent>,
    private fb: FormBuilder,
    private foodSvc: FoodService
  ) {
    this.searchCtrl = this.fb.control<string>('');
    this.foodSvc.getFoodCount().subscribe({
      next: result => {
        this.foodCount = result?.count ?? 0;
        this.foodCountError = false;
      },
      error: () => {
        this.foodCount = null;
        this.foodCountError = true;
      }
    });

    // Debounce + distinctUntilChanged limitan las consultas mientras se escribe y switchMap descarta resultados de búsquedas anteriores.
    this.searchCtrl.valueChanges.pipe(
      debounceTime(400),
      distinctUntilChanged(),
      switchMap((term: string | null) => {
        if (!term || term.length < 2) {
          this.results = [];
          return of([]);
        }
        this.loading = true;
        return this.foodSvc.searchFoods(term).pipe(
          finalize(() => this.loading = false)
        );
      }),
      takeUntilDestroyed(this.destroyRef)
    ).subscribe((res: any) => {
      this.results = res || [];
    });
  }

  selectFood(food: any) {
    this.selectedFood = food;
    this.grams = 100; // Por defecto
  }

  // El componente padre recibe tanto el alimento seleccionado como la cantidad para incorporarlos a la dieta.
  confirm() {
    if (this.selectedFood && this.grams > 0) {
      this.dialogRef.close({ food: this.selectedFood, grams: this.grams });
    }
  }
}
