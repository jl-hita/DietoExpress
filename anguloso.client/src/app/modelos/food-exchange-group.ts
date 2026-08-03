export interface FoodExchangeGroup {
  id: number;
  name: string;
  kcal: number;
  protein: number;
  carbs: number;
  fat: number;
}

export interface FoodInExchangeGroup {
  id: number;
  name: string;
  gramsPerExchange: number;
  kcal: number;
  protein: number;
  carbs: number;
  fat: number;
}
