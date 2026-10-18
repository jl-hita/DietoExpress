import { test, expect } from '@playwright/test';

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
const clientId = Number(process.env.E2E_CLINICAL_CLIENT_ID || 0);
const allergenFoodId = Number(process.env.E2E_ALLERGEN_FOOD_ID || 0);
const traceFoodId = Number(process.env.E2E_TRACE_FOOD_ID || 0);
const unknownFoodId = Number(process.env.E2E_UNKNOWN_ALLERGEN_FOOD_ID || 0);

/*
 * Read-only clinical checks. Configure these IDs to point only to:
 * - a dedicated fictional patient with the relevant allergy in their food preferences;
 * - existing catalogue foods whose allergen/trace/quality metadata has been reviewed.
 *
 * This suite sends draft diets to the validation endpoint; it does not save diets,
 * regenerate meals, alter foods, or modify the patient. Do not point it at a real patient.
 */
test.describe('P0 — validación clínica de alérgenos (solo borradores)', () => {
  test.beforeAll(() => {
    const configuredIds = [clientId, allergenFoodId, traceFoodId, unknownFoodId];
    const ready = Boolean(username && password) && configuredIds.every(id => Number.isSafeInteger(id) && id > 0);
    test.skip(!ready, 'Configura credenciales E2E y los cuatro IDs de la ficha ficticia/alimentos de prueba para habilitar las comprobaciones clínicas.');
    expect(new Set([allergenFoodId, traceFoodId, unknownFoodId]).size,
      'Los tres IDs deben identificar alimentos distintos y revisados, no reutilizar el mismo alimento para escenarios diferentes.'
    ).toBe(3);
  });

  test.beforeEach(async ({ page }) => {
    test.skip(
      !username || !password || !Number.isSafeInteger(clientId) || clientId <= 0 ||
      !Number.isSafeInteger(allergenFoodId) || allergenFoodId <= 0 ||
      !Number.isSafeInteger(traceFoodId) || traceFoodId <= 0 ||
      !Number.isSafeInteger(unknownFoodId) || unknownFoodId <= 0,
      'Configura credenciales E2E y los cuatro IDs de la ficha ficticia/alimentos de prueba para habilitar las comprobaciones clínicas.'
    );

    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });
  });

  async function validateDraft(page, foodId) {
    const response = await page.context().request.post(new URL('/api/dietas/validate', baseURL).toString(), {
      data: {
        clientId,
        diet: {
          name: 'E2E — borrador no persistido',
          targetKcal: 1800,
          days: [{
            dayIndex: 0,
            meals: [{
              name: 'Comida de prueba',
              mealIndex: 0,
              items: [{ foodId, grams: 100 }]
            }]
          }]
        }
      }
    });

    expect(response.status(), 'La validación clínica debe responder correctamente').toBe(200);
    const warnings = await response.json();
    expect(Array.isArray(warnings), 'La API debe devolver una lista de alertas').toBe(true);
    return warnings;
  }

  test('un alimento con alérgeno incompatible genera una alerta vinculada al alimento', async ({ page }) => {
    const warnings = await validateDraft(page, allergenFoodId);
    expect(warnings).toEqual(expect.arrayContaining([
      expect.objectContaining({
        foodId: allergenFoodId,
        alertType: expect.stringMatching(/\S/),
        message: expect.stringMatching(/\S/)
      })
    ]));
    expect(JSON.stringify(warnings)).toMatch(/al[eé]rgen|allergen|alergia/i);
  });

  test('un alimento con trazas incompatibles genera una alerta vinculada al alimento', async ({ page }) => {
    const warnings = await validateDraft(page, traceFoodId);
    expect(warnings).toEqual(expect.arrayContaining([
      expect.objectContaining({
        foodId: traceFoodId,
        alertType: expect.any(String),
        message: expect.any(String)
      })
    ]));
    expect(JSON.stringify(warnings)).toMatch(/traza|trace|al[eé]rgen|allergen/i);
  });

  test('los metadatos desconocidos no se aceptan silenciosamente cuando el paciente declara alergias', async ({ page }) => {
    const warnings = await validateDraft(page, unknownFoodId);
    expect(warnings).toEqual(expect.arrayContaining([
      expect.objectContaining({
        foodId: unknownFoodId,
        alertType: expect.any(String),
        message: expect.any(String)
      })
    ]));
    expect(JSON.stringify(warnings)).toMatch(/desconocid|incomplet|unknown|allergen|al[eé]rgen/i);
  });
});
