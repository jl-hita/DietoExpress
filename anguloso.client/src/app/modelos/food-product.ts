// ────────────────────────────────────────────────────────────────────────────
// FoodProduct — formato enriquecido devuelto por:
//   GET /api/food/search/{query}
//   GET /api/food/barcode/{code}
//
// Para crear/editar alimentos en la BBDD local usa CustomFoodRequest en food.ts
// Para leer un alimento por ID usa Food en food.ts
// ────────────────────────────────────────────────────────────────────────────

export interface FoodProduct {
  id: number;

  name: string;
  brands?: string;
  category?: string;

  nutriscore?: string;
  source?: 'local' | 'openfoodfacts' | 'usda' | 'user';

  servingSize?: number;          // 30
  servingSizeUnit?: string;      // g | ml
  servingSizeText?: string;      // "1 slice (30 g)"

  nutrients: Nutrients;
  micronutrients?: Micronutrients;

  createdAt?: string;            // ISO
  lastSyncedAt?: string;         // ISO
}

export interface Nutrients {
  energyKcal100g?: number;

  protein100g?: number;
  carbs100g?: number;
  fat100g?: number;

  saturatedFat100g?: number;
  sugar100g?: number;
  fiber100g?: number;
  salt100g?: number;
}

export interface Micronutrients {
  vitaminA_ug?: number;
  vitaminC_mg?: number;
  vitaminD_ug?: number;
  vitaminE_mg?: number;
  vitaminB12_ug?: number;
  folate_ug?: number;

  calcium_mg?: number;
  iron_mg?: number;
  magnesium_mg?: number;
  potassium_mg?: number;
  zinc_mg?: number;
}
