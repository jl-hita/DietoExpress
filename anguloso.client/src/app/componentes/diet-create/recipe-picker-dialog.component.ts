import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MatDialogRef, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { HttpClient } from '@angular/common/http';
import { environment } from '../../../environments/environments';

@Component({
  selector: 'app-recipe-picker-dialog',
  standalone: true,
  imports: [
    CommonModule,
    ReactiveFormsModule,
    MatDialogModule,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  template: `
    <div class="recipe-picker-dialog">
      <h2 mat-dialog-title style="display: flex; align-items: center; gap: 8px; margin: 0 0 16px;">
        <mat-icon color="primary">menu_book</mat-icon>
        Insertar Receta en la Comida
      </h2>

      <mat-dialog-content>
        <p style="color: #64748b; font-size: 13px; margin-bottom: 12px;">
          Selecciona una receta de tu catálogo. Todos sus ingredientes y macronutrientes se volcarán automáticamente en esta comida.
        </p>

        <mat-form-field appearance="outline" style="width: 100%; margin-bottom: 12px;">
          <mat-label>Filtrar recetas</mat-label>
          <input matInput [formControl]="searchCtrl" placeholder="Escribe el nombre de la receta..." />
          <mat-icon matSuffix>search</mat-icon>
        </mat-form-field>

        <div *ngIf="loading" style="display: flex; justify-content: center; padding: 24px;">
          <mat-spinner diameter="36"></mat-spinner>
        </div>

        <div *ngIf="!loading && filteredRecipes.length === 0" style="text-align: center; color: #94a3b8; padding: 24px;">
          <mat-icon style="font-size: 36px; width: 36px; height: 36px; opacity: 0.5;">search_off</mat-icon>
          <p style="margin: 8px 0 0;">No se encontraron recetas disponibles.</p>
        </div>

        <div class="recipe-list" *ngIf="!loading && filteredRecipes.length > 0">
          <div 
            *ngFor="let r of filteredRecipes" 
            class="recipe-card" 
            [class.selected]="selectedRecipe?.id === r.id"
            (click)="selectRecipe(r)">
            <div class="recipe-card-header">
              <strong style="color: #0f172a; font-size: 14px;">{{ r.name }}</strong>
              <span style="font-size: 12px; color: #6366f1; font-weight: 600;">
                {{ (r.items?.length || 0) }} ingredientes
              </span>
            </div>
            <p *ngIf="r.instructions" style="margin: 4px 0 0; font-size: 12px; color: #64748b; white-space: nowrap; overflow: hidden; text-overflow: ellipsis;">
              {{ r.instructions }}
            </p>
          </div>
        </div>
      </mat-dialog-content>

      <mat-dialog-actions align="end" style="margin-top: 16px; gap: 8px;">
        <button mat-button (click)="dialogRef.close()">Cancelar</button>
        <button mat-raised-button color="primary" [disabled]="!selectedRecipe" (click)="confirmInsert()">
          <mat-icon>playlist_add</mat-icon> Insertar Ingredientes
        </button>
      </mat-dialog-actions>
    </div>
  `,
  styles: [`
    .recipe-picker-dialog {
      min-width: 380px;
      max-width: 520px;
    }
    .recipe-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
      max-height: 280px;
      overflow-y: auto;
    }
    .recipe-card {
      border: 1px solid #e2e8f0;
      border-radius: 8px;
      padding: 10px 12px;
      cursor: pointer;
      transition: all 0.2s;
    }
    .recipe-card:hover {
      border-color: #6366f1;
      background: #f8fafc;
    }
    .recipe-card.selected {
      border-color: #6366f1;
      background: #eef2ff;
    }
    .recipe-card-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
    }
  `]
})
// El selector mantiene separada la búsqueda de recetas de su incorporación al formulario padre para no mezclar estado temporal y estado de la dieta.
export class RecipePickerDialogComponent implements OnInit {
  recipes: any[] = [];
  filteredRecipes: any[] = [];
  selectedRecipe: any | null = null;
  loading = true;
  searchCtrl = new FormControl('');

  constructor(
    private http: HttpClient,
    public dialogRef: MatDialogRef<RecipePickerDialogComponent>
  ) {}

  // Primero se carga el catálogo ligero; el detalle completo de la receta se obtiene únicamente cuando el usuario selecciona una.
  ngOnInit(): void {
    this.http.get<any[]>(`${environment.apiUrl}/recipes`).subscribe({
      next: (data) => {
        this.recipes = data || [];
        this.filteredRecipes = [...this.recipes];
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });

    this.searchCtrl.valueChanges.subscribe((term) => {
      const q = (term || '').toLowerCase().trim();
      this.filteredRecipes = this.recipes.filter(r => (r.name || '').toLowerCase().includes(q));
    });
  }

  // La selección fuerza una segunda consulta porque el listado puede no contener todavía todos los ingredientes necesarios para insertar la receta.
  selectRecipe(recipe: any): void {
    // Si la receta no tiene los items cargados, los pedimos con getRecipe(id)
    this.loading = true;
    this.http.get<any>(`${environment.apiUrl}/recipes/${recipe.id}`).subscribe({
      next: (fullRecipe) => {
        this.selectedRecipe = fullRecipe;
        this.loading = false;
      },
      error: () => {
        this.selectedRecipe = recipe;
        this.loading = false;
      }
    });
  }

  confirmInsert(): void {
    if (this.selectedRecipe) {
      this.dialogRef.close(this.selectedRecipe);
    }
  }
}
