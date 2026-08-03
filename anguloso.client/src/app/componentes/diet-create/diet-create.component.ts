import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, FormArray, Validators, AbstractControl, FormControl } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MATERIAL_IMPORTS } from '../../shared/material.imports';
import { DietService } from '../../servicios/diet.service';
import { FoodService } from '../../servicios/food.service';
import { Diet, DietDay, Meal, MealItem } from '../../modelos/diet';
import { FoodExchangeGroup } from '../../modelos/food-exchange-group';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ExchangeSearchDialogComponent } from './exchange-search-dialog.component';
import { debounceTime, distinctUntilChanged, switchMap, finalize } from 'rxjs/operators';
import { of } from 'rxjs';

@Component({
  selector: 'app-diet-create',
  standalone: true,
  imports: [MATERIAL_IMPORTS],
  templateUrl: './diet-create.component.html',
  styleUrls: ['./diet-create.component.css']
})
export class DietCreateComponent implements OnInit {
  form: FormGroup;
  isEdit = false;
  dietId: number | null = null;
  loading = false;
  exchangeGroups: FoodExchangeGroup[] = [];

  // Visual Editor Properties
  activeDayIndex = 0;
  searchCtrl: FormControl;
  searchResults: any[] = [];
  searchLoading = false;
  selectedFoodForAdd: any | null = null;
  gramsToAdd = 100;
  targetMealForAdd: { dIndex: number, mIndex: number } | null = null;

  constructor(
    private fb: FormBuilder,
    private dietService: DietService,
    private foodService: FoodService,
    private route: ActivatedRoute,
    private router: Router,
    private snackBar: MatSnackBar,
    private dialog: MatDialog
  ) {
    this.searchCtrl = this.fb.control<string>('');
    this.form = this.fb.group({
      name: ['', Validators.required],
      targetKcal: [null],
      targetProtein: [null],
      targetCarbs: [null],
      targetFat: [null],
      notes: [''],
      days: this.fb.array([])
    });
  }

  get days(): FormArray {
    return this.form.get('days') as FormArray;
  }

  getMeals(dayIndex: number): FormArray {
    return this.days.at(dayIndex).get('meals') as FormArray;
  }

  getItems(dayIndex: number, mealIndex: number): FormArray {
    return this.getMeals(dayIndex).at(mealIndex).get('items') as FormArray;
  }

  ngOnInit(): void {
    // Cargar grupos de intercambio
    this.foodService.getExchangeGroups().subscribe({
      next: (groups) => this.exchangeGroups = groups,
      error: () => console.warn('No se pudieron cargar los grupos de intercambio')
    });

    // Configurar búsqueda reactiva inline
    this.searchCtrl.valueChanges.pipe(
      debounceTime(300),
      distinctUntilChanged(),
      switchMap((term: string | null) => {
        if (!term || term.trim().length < 2) {
          this.searchResults = [];
          return of([]);
        }
        this.searchLoading = true;
        return this.foodService.searchFoods(term).pipe(
          finalize(() => this.searchLoading = false)
        );
      })
    ).subscribe({
      next: (res: any) => {
        this.searchResults = res || [];
      },
      error: (err) => {
        console.error('Error al buscar alimentos:', err);
        this.searchLoading = false;
      }
    });

    const id = this.route.snapshot.paramMap.get('id');
    if (id) {
      this.dietId = +id;
      this.isEdit = true;
      this.loading = true;
      this.dietService.getDiet(this.dietId).subscribe({
        next: (d) => {
          this.form.patchValue({
            name: d.name,
            targetKcal: d.targetKcal ?? null,
            targetProtein: d.targetProtein ?? null,
            targetCarbs: d.targetCarbs ?? null,
            targetFat: d.targetFat ?? null,
            notes: d.notes || ''
          });

          // Reconstruir árbol temporal en FormArray
          if (d.days) {
            d.days.forEach(day => {
              const dayGroup = this.createDayGroup(day.dayIndex);
              const mealsArray = dayGroup.get('meals') as FormArray;
              day.meals.forEach(meal => {
                const mealGroup = this.createMealGroup(meal.name, meal.mealIndex);
                const itemsArray = mealGroup.get('items') as FormArray;
                meal.items.forEach(item => {
                  itemsArray.push(this.createItemGroup(item));
                });
                mealsArray.push(mealGroup);
              });
              this.days.push(dayGroup);
            });
          }

          this.loading = false;
          // Seleccionar primer día tras cargar
          if (this.days.length > 0) {
            this.selectDay(0);
          }
        },
        error: () => {
          this.loading = false;
          this.snackBar.open('No se pudo cargar la dieta', 'Cerrar', { duration: 4000 });
          this.router.navigate(['/diets']);
        }
      });
    } else {
      // Nueva dieta: Agregamos el Día 1 por defecto vacio, y seleccionamos primer dia.
      this.addDay();
      this.selectDay(0);
    }
  }

  createDayGroup(index: number = 0): FormGroup {
    return this.fb.group({
      dayIndex: [index],
      meals: this.fb.array([])
    });
  }

  createMealGroup(name: string = 'Comida', index: number = 0): FormGroup {
    return this.fb.group({
      name: [name, Validators.required],
      mealIndex: [index],
      items: this.fb.array([])
    });
  }

  createItemGroup(item: any): FormGroup {
    const isExchange = !!item.exchangeGroupId;
    return this.fb.group({
      foodId: [item.foodId ?? null],
      foodName: [item.foodName || ''],
      grams: [item.grams ?? null, isExchange ? null : [Validators.required, Validators.min(1)]],
      // Base macros per 100g para poder recalcular:
      baseKcal: [item.baseKcal ?? (item.kcal && item.grams ? item.kcal / (item.grams / 100) : 0)],
      baseProtein: [item.baseProtein ?? (item.protein && item.grams ? item.protein / (item.grams / 100) : 0)],
      baseCarbs: [item.baseCarbs ?? (item.carbs && item.grams ? item.carbs / (item.grams / 100) : 0)],
      baseFat: [item.baseFat ?? (item.fat && item.grams ? item.fat / (item.grams / 100) : 0)],
      // Current calculated macros:
      kcal: [item.kcal || 0],
      protein: [item.protein || 0],
      carbs: [item.carbs || 0],
      fat: [item.fat || 0],
      // Intercambios:
      exchangeGroupId: [item.exchangeGroupId ?? null],
      exchangeGroupName: [item.exchangeGroupName ?? null],
      exchangeCount: [item.exchangeCount ?? null]
    });
  }

  createExchangeItemGroup(group: FoodExchangeGroup, count: number): FormGroup {
    return this.fb.group({
      foodId: [null],
      foodName: [null],
      grams: [null],
      baseKcal: [0],
      baseProtein: [0],
      baseCarbs: [0],
      baseFat: [0],
      kcal: [+(group.kcal * count).toFixed(1)],
      protein: [+(group.protein * count).toFixed(1)],
      carbs: [+(group.carbs * count).toFixed(1)],
      fat: [+(group.fat * count).toFixed(1)],
      exchangeGroupId: [group.id],
      exchangeGroupName: [group.name],
      exchangeCount: [count]
    });
  }

  // Visual Selection Helpers
  selectDay(index: number): void {
    this.activeDayIndex = index;
    // Set default target meal to the first meal of the selected day
    const meals = this.getMeals(index);
    if (meals && meals.length > 0) {
      this.targetMealForAdd = { dIndex: index, mIndex: 0 };
    } else {
      this.targetMealForAdd = null;
    }
    this.selectedFoodForAdd = null;
  }

  selectSearchMeal(dIndex: number, mIndex: number): void {
    this.targetMealForAdd = { dIndex, mIndex };
    this.selectedFoodForAdd = null;
    this.searchCtrl.setValue('');
    this.searchResults = [];
    
    // Focus search input
    setTimeout(() => {
      const searchInput = document.getElementById('food-search-input');
      if (searchInput) {
        searchInput.focus();
      }
    }, 100);
  }

  getTargetMealName(): string {
    if (!this.targetMealForAdd) return '';
    const { dIndex, mIndex } = this.targetMealForAdd;
    const dayName = `Día ${dIndex + 1}`;
    const mealCtrl = this.getMeals(dIndex).at(mIndex);
    const mealName = mealCtrl ? mealCtrl.get('name')?.value : '';
    return `${dayName} - ${mealName}`;
  }

  getFoodName(food: any): string {
    if (!food) return '';
    return food.name || food.product_name || food.productName || '[Sin nombre]';
  }

  getFoodKcal(food: any): number {
    if (!food) return 0;
    return food.kcal ?? food.nutriments?.energyKcal100g ?? food.nutriments?.['energy-kcal_100g'] ?? 0;
  }

  getFoodProtein(food: any): number {
    if (!food) return 0;
    return food.protein ?? food.nutriments?.proteins100g ?? food.nutriments?.['proteins_100g'] ?? 0;
  }

  getFoodCarbs(food: any): number {
    if (!food) return 0;
    return food.carbs ?? food.nutriments?.carbohydrates100g ?? food.nutriments?.['carbohydrates_100g'] ?? 0;
  }

  getFoodFat(food: any): number {
    if (!food) return 0;
    return food.fat ?? food.nutriments?.fat100g ?? food.nutriments?.['fat_100g'] ?? 0;
  }

  selectFoodForAdd(food: any): void {
    this.selectedFoodForAdd = food;
    this.gramsToAdd = food.defaultGrams || 100;
  }

  confirmAddFood(): void {
    if (!this.selectedFoodForAdd || !this.targetMealForAdd) return;
    const { dIndex, mIndex } = this.targetMealForAdd;
    const food = this.selectedFoodForAdd;
    const grams = this.gramsToAdd;

    const kcalVal = this.getFoodKcal(food);
    const proteinVal = this.getFoodProtein(food);
    const carbsVal = this.getFoodCarbs(food);
    const fatVal = this.getFoodFat(food);
    const foodIdVal = food.id || food.Id || null;
    const nameVal = this.getFoodName(food);

    const item = {
      foodId: foodIdVal,
      foodName: nameVal,
      grams: grams,
      baseKcal: kcalVal,
      baseProtein: proteinVal,
      baseCarbs: carbsVal,
      baseFat: fatVal,
      kcal: (kcalVal || 0) * (grams / 100),
      protein: (proteinVal || 0) * (grams / 100),
      carbs: (carbsVal || 0) * (grams / 100),
      fat: (fatVal || 0) * (grams / 100)
    };

    this.getItems(dIndex, mIndex).push(this.createItemGroup(item));
    this.selectedFoodForAdd = null;
    this.searchCtrl.setValue('');
    this.searchResults = [];
    this.snackBar.open(`${nameVal} añadido a ${this.getMeals(dIndex).at(mIndex).get('name')?.value}`, 'Cerrar', { duration: 2000 });
  }

  addDay(): void {
    const idx = this.days.length;
    const g = this.createDayGroup(idx);
    // Preañadir Desayuno, Comida, Cena
    const meals = g.get('meals') as FormArray;
    meals.push(this.createMealGroup('Desayuno', 0));
    meals.push(this.createMealGroup('Comida', 1));
    meals.push(this.createMealGroup('Cena', 2));

    this.days.push(g);
    
    // Select this day if it's the first one
    if (this.days.length === 1) {
      this.selectDay(0);
    }
  }

  removeDay(dIndex: number): void {
    this.days.removeAt(dIndex);
    // Renumerar los demas
    for(let i = 0; i < this.days.length; i++) {
       this.days.at(i).get('dayIndex')?.setValue(i);
    }
    
    // Adjust activeDayIndex
    if (this.activeDayIndex >= this.days.length) {
      this.activeDayIndex = Math.max(0, this.days.length - 1);
    }
    if (this.days.length > 0) {
      this.selectDay(this.activeDayIndex);
    } else {
      this.targetMealForAdd = null;
    }
  }

  addMeal(dIndex: number): void {
    const meals = this.getMeals(dIndex);
    meals.push(this.createMealGroup(`Comida ${meals.length + 1}`, meals.length));
    if (meals.length === 1) {
      this.targetMealForAdd = { dIndex, mIndex: 0 };
    }
  }

  removeMeal(dIndex: number, mIndex: number): void {
    this.getMeals(dIndex).removeAt(mIndex);
    // Adjust targetMealForAdd if it was pointing to this meal
    if (this.targetMealForAdd && this.targetMealForAdd.dIndex === dIndex && this.targetMealForAdd.mIndex === mIndex) {
      this.selectDay(dIndex);
    }
  }

  isExchangeItem(itemCtrl: AbstractControl): boolean {
    return !!itemCtrl.get('exchangeGroupId')?.value;
  }

  openExchangeSearch(dIndex: number, mIndex: number): void {
    const ref = this.dialog.open(ExchangeSearchDialogComponent, {
      width: '480px',
      data: { exchangeGroups: this.exchangeGroups }
    });
    ref.afterClosed().subscribe(res => {
      if (res && res.group && res.count) {
        this.getItems(dIndex, mIndex).push(this.createExchangeItemGroup(res.group, res.count));
      }
    });
  }

  removeItem(dIndex: number, mIndex: number, iIndex: number): void {
    this.getItems(dIndex, mIndex).removeAt(iIndex);
  }

  recalcItem(itemCtrl: AbstractControl): void {
    if(!itemCtrl || this.isExchangeItem(itemCtrl)) return;
    const grams = itemCtrl.get('grams')?.value || 0;
    const bKcal = itemCtrl.get('baseKcal')?.value || 0;
    const bProt = itemCtrl.get('baseProtein')?.value || 0;
    const bCarb = itemCtrl.get('baseCarbs')?.value || 0;
    const bFat = itemCtrl.get('baseFat')?.value || 0;

    itemCtrl.patchValue({
      kcal: bKcal * (grams / 100),
      protein: bProt * (grams / 100),
      carbs: bCarb * (grams / 100),
      fat: bFat * (grams / 100)
    }, { emitEvent: false });
  }

  recalcExchangeItem(itemCtrl: AbstractControl): void {
    if(!itemCtrl || !this.isExchangeItem(itemCtrl)) return;
    const count = itemCtrl.get('exchangeCount')?.value || 0;
    const groupId = itemCtrl.get('exchangeGroupId')?.value;
    const group = this.exchangeGroups.find(g => g.id === groupId);
    if (!group) return;

    itemCtrl.patchValue({
      kcal: +(group.kcal * count).toFixed(1),
      protein: +(group.protein * count).toFixed(1),
      carbs: +(group.carbs * count).toFixed(1),
      fat: +(group.fat * count).toFixed(1)
    }, { emitEvent: false });
  }

  // CALCS
  getMealSum(dIndex: number, mIndex: number, prop: string): number {
    const items = this.getItems(dIndex, mIndex).controls;
    let sum = 0;
    for(let i of items) sum += (i.get(prop)?.value || 0);
    return sum;
  }
  getMealKcal(d: number, m: number) { return this.getMealSum(d,m,'kcal'); }
  getMealProtein(d: number, m: number) { return this.getMealSum(d,m,'protein'); }
  getMealCarbs(d: number, m: number) { return this.getMealSum(d,m,'carbs'); }
  getMealFat(d: number, m: number) { return this.getMealSum(d,m,'fat'); }

  getDaySum(dIndex: number, prop: string): number {
    const meals = this.getMeals(dIndex).controls;
    let sum = 0;
    for (let m = 0; m < meals.length; m++) sum += this.getMealSum(dIndex, m, prop);
    return sum;
  }
  getDayKcal(d: number) { return this.getDaySum(d,'kcal'); }
  getDayProtein(d: number) { return this.getDaySum(d,'protein'); }
  getDayCarbs(d: number) { return this.getDaySum(d,'carbs'); }
  getDayFat(d: number) { return this.getDaySum(d,'fat'); }

  getPercent(current: number, target: number | null | undefined): number {
    if (!target || target <= 0) return 0;
    return Math.min(100, Math.round((current / target) * 100));
  }

  get targetKcal(): number { return this.form.get('targetKcal')?.value || 0; }
  get targetProtein(): number { return this.form.get('targetProtein')?.value || 0; }
  get targetCarbs(): number { return this.form.get('targetCarbs')?.value || 0; }
  get targetFat(): number { return this.form.get('targetFat')?.value || 0; }

  submit(): void {
    if (this.form.invalid) return;
    const raw = this.form.getRawValue();
    const dto: Diet = {
      name: raw.name,
      targetKcal: raw.targetKcal ?? undefined,
      targetProtein: raw.targetProtein ?? undefined,
      targetCarbs: raw.targetCarbs ?? undefined,
      targetFat: raw.targetFat ?? undefined,
      notes: raw.notes || undefined,
      days: raw.days.map((d: any) => ({
        dayIndex: d.dayIndex,
        meals: d.meals.map((m: any, mIdx: number) => ({
          name: m.name,
          mealIndex: m.mealIndex,
          items: m.items.map((i: any) => ({
             foodId: i.foodId ?? undefined,
             grams: i.grams ?? undefined,
             kcal: i.kcal,
             protein: i.protein,
             carbs: i.carbs,
             fat: i.fat,
             exchangeGroupId: i.exchangeGroupId ?? undefined,
             exchangeCount: i.exchangeCount ?? undefined
          }))
        }))
      }))
    };

    if (this.isEdit && this.dietId != null) {
      this.dietService.updateDiet(this.dietId, dto).subscribe({
        next: () => {
          this.snackBar.open('Dieta actualizada', 'Cerrar', { duration: 3000 });
          this.router.navigate(['/diets']);
        },
        error: () => this.snackBar.open('Error al actualizar', 'Cerrar', { duration: 4000 })
      });
    } else {
      this.dietService.createDiet(dto).subscribe({
        next: () => {
          this.snackBar.open('Dieta creada', 'Cerrar', { duration: 3000 });
          this.router.navigate(['/diets']);
        },
        error: () => this.snackBar.open('Error al crear la dieta', 'Cerrar', { duration: 4000 })
      });
    }
  }

  cancel(): void {
    this.router.navigate(['/diets']);
  }
}
