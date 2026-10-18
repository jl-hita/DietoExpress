import { test, expect } from '@playwright/test';

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
let clientId = 0;
const testPatientName = 'ZZZ E2E — paciente ficticio alérgenos';
const allergenFoodId = Number(process.env.E2E_ALLERGEN_FOOD_ID || 0);
const traceFoodId = Number(process.env.E2E_TRACE_FOOD_ID || 0);
const unknownFoodId = Number(process.env.E2E_UNKNOWN_ALLERGEN_FOOD_ID || 0);

/*
 * The suite automatically creates or reuses one dedicated fictional patient and sets
 * their allergy profile. It sends draft diets to the validation endpoint and does not
 * save diets or change catalogue foods. Food IDs must refer to reviewed test fixtures.
 */
test.describe('P0 — validación clínica de alérgenos (solo borradores)', () => {
  test.beforeAll(() => {
    const configuredIds = [allergenFoodId, traceFoodId, unknownFoodId];
    const ready = Boolean(username && password) && configuredIds.every(id => Number.isSafeInteger(id) && id > 0);
    test.skip(!ready, 'Configura credenciales E2E y tres IDs de alimentos de prueba revisados para habilitar las comprobaciones clínicas.');
    expect(new Set([allergenFoodId, traceFoodId, unknownFoodId]).size,
      'Los tres IDs deben identificar alimentos distintos y revisados, no reutilizar el mismo alimento para escenarios diferentes.'
    ).toBe(3);
  });

  test.beforeEach(async ({ page }) => {
    test.skip(
      !username || !password ||
      !Number.isSafeInteger(allergenFoodId) || allergenFoodId <= 0 ||
      !Number.isSafeInteger(traceFoodId) || traceFoodId <= 0 ||
      !Number.isSafeInteger(unknownFoodId) || unknownFoodId <= 0,
      'Configura credenciales E2E y tres IDs de alimentos de prueba revisados para habilitar las comprobaciones clínicas.'
    );

    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });
  });

    // Reutiliza una ficha ficticia dedicada para no crear un paciente nuevo en cada ejecución.
    const api = page.context().request;
    const listResponse = await api.get(new URL('/api/clients?search=' + encodeURIComponent(testPatientName) + '&page=1&pageSize=100', baseURL).toString());
    expect(listResponse.ok(), 'Debe poder consultarse la lista de pacientes para preparar el fixture E2E').toBeTruthy();
    const listing = await listResponse.json();
    const patients = Array.isArray(listing) ? listing : (listing.items || []);
    const existing = patients.find(patient => (patient.fullName ?? patient.FullName) === testPatientName);

    if (existing) {
      clientId = Number(existing.id ?? existing.Id);
      const updateResponse = await api.put(new URL('/api/clients/' + clientId, baseURL).toString(), { data: { fullName: testPatientName, foodPreferences: { allergies: 'cacahuete, leche' } } });
      expect(updateResponse.ok(), 'Debe poder prepararse la alergia de la ficha ficticia E2E').toBeTruthy();
    } else {
      const createResponse = await api.post(new URL('/api/clients', baseURL).toString(), { data: { fullName: testPatientName, email: 'e2e-allergen-test@invalid.example', notes: 'Ficha ficticia reservada para pruebas automatizadas de alérgenos. No usar como paciente real.', country: 'España', foodPreferences: { allergies: 'cacahuete, leche' } } });
      expect(createResponse.ok(), 'Debe poder crearse la ficha ficticia E2E mediante el endpoint normal de la aplicación').toBeTruthy();
      const created = await createResponse.json();
      clientId = Number(created.id ?? created.Id);
      expect(Number.isSafeInteger(clientId) && clientId > 0, 'La API debe devolver el ID de la ficha ficticia creada').toBe(true);
    }

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
