import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormArray, FormBuilder, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { FoodService } from '../../servicios/food.service';
import { FoodProduct } from '../../modelos/food-product';
import { environment } from '../../../environments/environments';

@Component({
  selector: 'app-crear-receta',
  templateUrl: './crear-receta.component.html',
  styleUrls: ['./crear-receta.component.css'],
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule, MatProgressBarModule],
})
export class CrearRecetaComponent {
  private readonly apiUrl = environment.apiUrl;
  formReceta: FormGroup;
  resultados: FoodProduct[] = [];
  cargando = false;
  buscando = false;
  mensaje: string | null = null;
  detalle: any = null;

  constructor(private fb: FormBuilder, private http: HttpClient, private foodService: FoodService) {
    this.formReceta = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      instructions: ['', Validators.maxLength(10000)],
      servings: [1, [Validators.required, Validators.min(0.1), Validators.max(1000)]],
      yieldGrams: [null, [Validators.min(0.1), Validators.max(100000)]],
      search: [''],
      ingredients: this.fb.array([])
    });
  }

  get ingredients(): FormArray { return this.formReceta.get('ingredients') as FormArray; }

  buscarAlimentos(): void {
    const term = String(this.formReceta.get('search')?.value ?? '').trim();
    if (term.length < 2) { this.resultados = []; return; }
    this.buscando = true;
    this.foodService.searchFoods(term).subscribe({
      next: foods => { this.resultados = foods.slice(0, 12); this.buscando = false; },
      error: () => { this.resultados = []; this.buscando = false; }
    });
  }

  agregarAlimento(food: FoodProduct): void {
    const existing = this.ingredients.controls.find(c => c.value.foodId === food.id);
    if (existing) existing.patchValue({ grams: Number(existing.value.grams || 0) + Number(food.servingSize || 100) });
    else this.ingredients.push(this.fb.group({
      foodId: [food.id, Validators.required], foodName: [food.name],
      grams: [food.servingSize || 100, [Validators.required, Validators.min(0.1), Validators.max(10000)]],
      kcal: [food.nutrients.energyKcal100g], protein: [food.nutrients.protein100g],
      carbs: [food.nutrients.carbs100g], fat: [food.nutrients.fat100g]
    }));
    this.formReceta.get('search')?.setValue('');
    this.resultados = [];
  }

  eliminarIngrediente(i: number): void { this.ingredients.removeAt(i); }
  private scale(v: number | null | undefined, g: number): number { return v == null ? 0 : Number(v) * Number(g || 0) / 100; }
  total(field: 'kcal'|'protein'|'carbs'|'fat'): number { return this.ingredients.controls.reduce((s,c)=>s+this.scale(c.value[field],c.value.grams),0); }
  perServing(field: 'kcal'|'protein'|'carbs'|'fat'): number { return this.total(field)/Number(this.formReceta.get('servings')?.value||1); }

  guardarReceta(): void {
    if (this.formReceta.invalid || !this.ingredients.length) { this.mensaje='Añade al menos un alimento con una cantidad válida.'; return; }
    this.cargando=true; this.mensaje=null;
    const v=this.formReceta.value;
    const body={name:v.name,instructions:v.instructions,servings:Number(v.servings),yieldGrams:v.yieldGrams?Number(v.yieldGrams):null,
      ingredients:v.ingredients.map((i:any)=>({foodId:Number(i.foodId),grams:Number(i.grams)}))};
    this.http.post<any>(this.apiUrl + '/recipes',body).subscribe({
      next: detail=>{this.detalle=detail;this.mensaje='Receta guardada y recalculada correctamente.';this.cargando=false;},
      error: err=>{this.mensaje=err?.error||'No se pudo guardar la receta.';this.cargando=false;}
    });
  }
}
