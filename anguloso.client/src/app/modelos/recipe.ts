// ────────────────────────────────────────────────────────────────────────────
// Recipe models — coinciden con los DTOs del RecipesController del backend
// ────────────────────────────────────────────────────────────────────────────

/** GET /api/recipes  → array de este tipo */
export interface RecipeListItem {
  id: number;
  name: string;
  instructions?: string;
  createdAt?: string; // ISO date
}

/** Ingrediente calculado tal como lo devuelve el backend en GET /api/recipes/{id} */
export interface RecipeIngredient {
  foodId: number;
  foodName: string;
  brands?: string;
  grams: number;

  // Macros ya calculados para los gramos especificados
  kcal?: number;
  protein?: number;
  carbs?: number;
  fat?: number;
}

/** GET /api/recipes/{id} */
export interface RecipeDetail extends RecipeListItem {
  ingredients: RecipeIngredient[];
}

// ─── Peticiones de escritura ─────────────────────────────────────────────────

/** Ingrediente que se envía al backend al crear/actualizar una receta */
export interface CreateRecipeIngredientRequest {
  foodId: number;
  grams: number;
}

/** POST /api/recipes  |  PUT /api/recipes/{id} */
export interface CreateRecipeRequest {
  name: string;
  instructions?: string;
  ingredients: CreateRecipeIngredientRequest[];
}
