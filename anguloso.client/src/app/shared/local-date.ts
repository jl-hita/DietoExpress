/**
 * Fecha de calendario (yyyy-MM-dd) en la zona del navegador. toISOString() usa UTC y, cerca de medianoche, devuelve el día
 * anterior o el siguiente.
 */
export function localDateInput(date: Date = new Date()): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}
