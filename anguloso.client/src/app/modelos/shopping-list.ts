export interface ShoppingListItem {
  foodId: number;
  foodName: string;
  category: string;
  totalGrams: number;
  roundedGrams: number;
  commercialDescription: string;
  allergenTags: string[];
  allergenDataAvailable: boolean;
  requiresAllergenCheck?: boolean;
}

export interface ShoppingListCategory {
  category: string;
  items: ShoppingListItem[];
}
