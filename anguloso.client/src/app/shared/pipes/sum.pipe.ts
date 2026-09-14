import { Pipe, PipeTransform } from '@angular/core';

/** Pipe para sumar una propiedad numérica de un array de objetos.
 *  Uso: {{ items | sum:'kcal' }}
 */
@Pipe({
  name: 'sum',
  standalone: true
})
export class SumPipe implements PipeTransform {
  transform(items: any[], field: string): number {
    if (!items?.length) return 0;
    return items.reduce((acc, item) => acc + (parseFloat(item[field]) || 0), 0);
  }
}
