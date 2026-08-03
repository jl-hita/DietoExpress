// ────────────────────────────────────────────────────────────────────────────
// Food models — coinciden con los DTOs del FoodController del backend
// ────────────────────────────────────────────────────────────────────────────

/**
 * Alimento tal como lo devuelve el backend en:
 *   GET /api/food/{id}
 *   GET /api/food/search/{query}
 *   GET /api/food/barcode/{code}
 *   POST /api/food  (respuesta)
 *
 * Equivale al modelo `foods` de la base de datos.
 */
export interface Food {
  id: number;
  name: string;
  brands?: string;
  category?: string;
  nutriscore?: string;

  kcal?: number;
  protein?: number;
  carbs?: number;
  fat?: number;
  saturatedFat?: number;
  fiber?: number;
  sugar?: number;
  salt?: number;

  servingSize?: number;
  servingSizeUnit?: string;   // g | ml
  servingSizeText?: string;   // "1 slice (30 g)"

  defaultGrams?: number;
  source?: string;            // local | openfoodfacts | usda | user

  createdAt?: string;         // ISO
  lastSyncedAt?: string;      // ISO
}

// ─── Peticiones de escritura ─────────────────────────────────────────────────

/**
 * POST /api/food      → crear alimento personalizado
 * PUT  /api/food/{id} → actualizar alimento personalizado
 *
 * Equivale a CustomFoodDto.cs
 */
export interface CustomFoodRequest {
  name: string;
  brands?: string;
  category?: string;
  nutriscore?: string;

  kcal?: number;
  protein?: number;
  carbs?: number;
  fat?: number;
  saturatedFat?: number;
  fiber?: number;
  sugar?: number;
  salt?: number;

  servingSize?: number;
  servingSizeUnit?: string;
  servingSizeText?: string;

  defaultGrams?: number;
  source?: string;
}
