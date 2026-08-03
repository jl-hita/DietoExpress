import { Component } from '@angular/core';
import { FoodService } from '../../servicios/food.service';
import { FoodProduct } from '../../modelos/food-product';
import { finalize } from 'rxjs';

@Component({
  selector: 'app-food-tester',
  standalone: false,
  templateUrl: './food-tester.component.html',
  styleUrl: './food-tester.component.css'
})
export class FoodTesterComponent {
  /*
  term: string = '';
  barcode: string = '';
  resultados: FoodProduct[] = [];
  seleccionado: FoodProduct | null = null;
  loading = false;
  error: string | null = null;
  objectKeys = Object.keys;

  constructor(private foodService: FoodService) { }

  buscar() {
    if (!this.term || this.term.trim().length < 2) {
      this.error = 'Introduce al menos 2 caracteres para buscar';
      return;
    }
    this.loading = true;
    this.error = null;
    this.resultados = [];
    this.seleccionado = null;

    this.foodService.searchFoods(this.term.trim()).pipe(
      finalize(() => this.loading = false)
    ).subscribe({
      next: (res) => {
        // Algunos backends devuelven { products: [...] } o la lista directa
        if (!res) {
          this.error = 'No se han encontrado resultados';
          return;
        }

        // Normalizar a array
        const arr = Array.isArray(res) ? res : ((res as any).products ?? [res]);
        this.resultados = arr.map((r: any) => this.normalizeProduct(r));
        if (this.resultados.length === 1) this.seleccionado = this.resultados[0];
      },
      error: (err) => {
        console.error(err);
        this.error = 'Error al buscar alimentos';
      }
    });
  }

  buscarBarcode() {
    const code = this.barcode.trim();
    if (!code) { this.error = 'Introduce un código de barras'; return; }
    this.loading = true;
    this.error = null;
    this.seleccionado = null;
    this.resultados = [];

    this.foodService.getProduct(code).pipe(
      finalize(() => this.loading = false)
    ).subscribe({
      next: (res) => {
        this.seleccionado = this.normalizeProduct(res);
      },
      error: (err) => {
        console.error(err);
        this.error = 'No se ha encontrado el producto por código';
      }
    });
  }

  // Normaliza el producto: garantiza estructura conocida y extrae nutrimentos clave
  normalizeProduct(raw: any): FoodProduct {
    if (!raw) return { product: null };

    // Si el back devuelve la envoltura OFF: { code, product: {...} }
    const prodRaw: any = raw.product ?? raw;

    const nutriments: Record<string, number | string | undefined> = prodRaw.nutriments ?? raw.nutriments ?? {};

    // normalizar campos principales
    const normalized: FoodProduct = {
      code: raw.code ?? prodRaw.code ?? raw.code,
      product: {
        product_name: prodRaw.product_name ?? prodRaw.name ?? raw.product_name,
        brands: prodRaw.brands,
        quantity: prodRaw.quantity,
        categories: prodRaw.categories,
        image_url: prodRaw.image_url ?? prodRaw.image_small_url ?? prodRaw.image_thumb_url,
        nutriscore_grade: prodRaw.nutriscore_grade,
        nova_group: prodRaw.nova_group,
        ingredients_text: prodRaw.ingredients_text,
        nutriments: nutriments
      },
      nutriments: nutriments
    };

    // Calculamos kcal/100g si es posible
    const kcal = this.extractKcal(nutriments);
    if (kcal != null) {
      // guardamos también en nutriments una clave común
      normalized.product!.nutriments!['energy_kcal_100g'] = kcal;
    }

    return normalized;
  }

  // Extrae kilocalorías por 100g buscando varias claves posibles
  extractKcal(n: Record<string, any>): number | null {
    if (!n) return null;

    const candidates = [
      'energy-kcal_100g',
      'energy_kcal_100g',
      'energy-kcal',
      'energy_100g',
      'energy'
    ];

    for (const k of candidates) {
      const v = n[k];
      if (v == null) continue;

      const num = typeof v === 'string' ? parseFloat(v) : Number(v);
      if (!isNaN(num)) return num;
    }

    // A veces OFF da energy (kJ). Intentamos convertir kJ -> kcal si encontramos energy-kj_100g
    const kjCandidates = ['energy-kj_100g', 'energy_kj_100g', 'energy-kj'];
    for (const k of kjCandidates) {
      const v = n[k];
      if (v == null) continue;
      const numkJ = typeof v === 'string' ? parseFloat(v) : Number(v);
      if (!isNaN(numkJ)) {
        // 1 kcal = 4.184 kJ
        return Math.round((numkJ / 4.184) * 100) / 100;
      }
    }

    return null;
  }

  // Mostrar porción formateada
  getKcalDisplay(p: FoodProduct | null) {
    const kcal = p?.product?.nutriments?.['energy_kcal_100g'] ?? p?.nutriments?.['energy_kcal_100g'];
    return kcal ? `${kcal} kcal/100g` : 'kcal desconocidas';
  }
  */
}
