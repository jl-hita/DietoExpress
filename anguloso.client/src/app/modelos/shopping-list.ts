export interface ShoppingListItem {
  foodId: number;
  foodName: string;
  category: string;
  totalGrams: number;
  roundedGrams: number;
  commercialDescription: string;
}

export interface ShoppingListCategory {
  category: string;
  items: ShoppingListItem[];
}
