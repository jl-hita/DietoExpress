import { FoodProduct } from './food-product';

describe('FoodProduct model', () => {
  it('should allow a valid food product object', () => {
    const product: FoodProduct = {
      id: 1,
      name: 'Test',
      nutrients: {}
    };

    expect(product).toBeTruthy();
    expect(product.name).toBe('Test');
  });
});
