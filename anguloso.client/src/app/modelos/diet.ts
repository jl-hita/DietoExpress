// ────────────────────────────────────────────────────────────────────────────
// Diet models — coinciden con los DTOs del DietController del backend
// Ruta base: api/dietas
// ────────────────────────────────────────────────────────────────────────────

/** GET /api/dietas  → array de este tipo (equivale a DietListDto) */
export interface DietListItem {
  id?: number;
  name: string;
  targetKcal?: number;
  targetProtein?: number;
  targetCarbs?: number;
  targetFat?: number;
  notes?: string;
  createdAt?: string; // ISO date
}

/** GET /api/dietas/{id}  → incluye árbol de días/comidas (equivale a DietDetailDto) */
export interface DietDetail extends DietListItem {
  days?: DietDay[];
}

/** POST /api/dietas  (equivale a CreateDietDto) */
export interface CreateDietRequest {
  name: string;
  targetKcal?: number;
  targetProtein?: number;
  targetCarbs?: number;
  targetFat?: number;
  notes?: string;
  days?: DietDay[];
}

/** PUT /api/dietas/{id}  (equivale a UpdateDietDto) */
export interface UpdateDietRequest {
  name: string;
  targetKcal?: number;
  targetProtein?: number;
  targetCarbs?: number;
  targetFat?: number;
  notes?: string;
  days?: DietDay[];
}

// ─── Alias de compatibilidad (usado en componentes anteriores) ───────────────
/** @deprecated Usa DietDetail en su lugar */
export type Diet = DietDetail;

// ─── Sub-modelos del árbol ────────────────────────────────────────────────────

/** Equivale a DietDayDto */
export interface DietDay {
  id?: number;
  dayIndex: number;
  meals: Meal[];
}

/** Equivale a MealDto */
export interface Meal {
  id?: number;
  name: string;
  mealIndex: number;
  items: MealItem[];
}

/** Equivale a MealItemDto */
export interface MealItem {
  id?: number;
  foodId?: number;
  grams?: number;

  // Macros precalculados (por los gramos especificados)
  kcal?: number;
  protein?: number;
  carbs?: number;
  fat?: number;

  // Solo en la respuesta del detalle
  foodName?: string;

  // Sistema de Intercambios
  exchangeGroupId?: number;
  exchangeGroupName?: string;
  exchangeCount?: number;
}
