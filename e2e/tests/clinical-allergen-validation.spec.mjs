import { test, expect } from '@playwright/test';

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
const testPatientName = 'ZZZ E2E — paciente ficticio alérgenos';
let clientId = 0;
let createdFoodIds = [];

test.describe('P0 — validación clínica de alérgenos (fixtures aislados)', () => {
  test.beforeEach(async ({ page }) => {
    test.skip(!username || !password, 'Configura E2E_USERNAME y E2E_PASSWORD para ejecutar las pruebas clínicas.');

    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await expect(page).not.toHaveURL(new RegExp('/login(?:[/?#]|$)'), { timeout: 20_000 });

    const api = page.context().request;
    const listResponse = await api.get(new URL('/api/clients?search=' + encodeURIComponent(testPatientName) + '&page=1&pageSize=100', baseURL).toString());
    expect(listResponse.ok(), 'Debe poder buscarse la ficha ficticia E2E').toBeTruthy();
    const listing = await listResponse.json();
    const patients = Array.isArray(listing) ? listing : (listing.items || listing.Items || []);
    const existing = patients.find(patient => (patient.fullName ?? patient.FullName) === testPatientName);

    if (existing) {
      clientId = Number(existing.id ?? existing.Id);
      const updateResponse = await api.put(new URL('/api/clients/' + clientId, baseURL).toString(), {
        data: { fullName: testPatientName, foodPreferences: { allergies: 'cacahuete, leche' } }
      });
      expect(updateResponse.ok(), 'Debe poder fijarse la alergia en la ficha ficticia').toBeTruthy();
    } else {
      const createResponse = await api.post(new URL('/api/clients', baseURL).toString(), {
        data: {
          fullName: testPatientName,
          email: 'e2e-allergen-test@invalid.example',
          notes: 'Paciente ficticio persistente para pruebas automatizadas de alérgenos.',
          country: 'España',
          foodPreferences: { allergies: 'cacahuete, leche' }
        }
      });
      expect(createResponse.ok(), 'Debe poder crearse la ficha ficticia mediante el flujo normal de la aplicación').toBeTruthy();
      const created = await createResponse.json();
      clientId = Number(created.id ?? created.Id);
      expect(Number.isSafeInteger(clientId) && clientId > 0, 'La API debe devolver el ID de la ficha ficticia').toBe(true);
    }

    createdFoodIds = [];
    for (const fixture of [
      { name: 'ZZZ E2E alimento temporal A', allergenTags: ['cacahuete'], tracesTags: [], allergenDataStatus: 'reported' },
      { name: 'ZZZ E2E alimento temporal B', allergenTags: [], tracesTags: ['leche'], allergenDataStatus: 'reported' },
      { name: 'ZZZ E2E alimento temporal C', allergenTags: [], tracesTags: [], allergenDataStatus: 'unknown' }
    ]) {
      const response = await api.post(new URL('/api/foods', baseURL).toString(), {
        data: {
          name: fixture.name,
          category: 'E2E — fixture temporal',
          kcal: 100,
          protein: 5,
          carbs: 10,
          fat: 2,
          defaultGrams: 100,
          allergenTags: fixture.allergenTags,
          tracesTags: fixture.tracesTags,
          allergenDataStatus: fixture.allergenDataStatus,
          allergenDataSource: 'e2e-test',
          allergenDataCheckedAt: fixture.allergenDataStatus === 'reported' ? new Date().toISOString() : null
        }
      });
      expect(response.ok(), 'Debe poder crearse un alimento local aislado con metadatos de alérgenos').toBeTruthy();
      const food = await response.json();
      const foodId = Number(food.id ?? food.Id);
      expect(Number.isSafeInteger(foodId) && foodId > 0, 'La API debe devolver el ID del alimento de prueba').toBe(true);
      expect(food.allergen_tags ?? food.allergenTags ?? food.AllergenTags ?? []).toEqual(fixture.allergenTags);
      expect(food.traces_tags ?? food.tracesTags ?? food.TracesTags ?? []).toEqual(fixture.tracesTags);
      expect(food.allergen_data_status ?? food.allergenDataStatus ?? food.AllergenDataStatus).toBe(fixture.allergenDataStatus);
      createdFoodIds.push(foodId);
    }
  });

  test.afterEach(async ({ page }) => {
    const api = page.context().request;
    for (const foodId of [...createdFoodIds].reverse()) {
      const response = await api.delete(new URL('/api/foods/' + foodId, baseURL).toString());
      expect([204, 404].includes(response.status()), 'El alimento temporal E2E debe eliminarse al finalizar la prueba').toBe(true);
    }
    createdFoodIds = [];
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

  test('la validación clínica alerta por alérgenos, trazas y metadatos desconocidos', async ({ page }) => {
    await test.step('alérgeno incompatible', async () => {
      const warnings = await validateDraft(page, createdFoodIds[0]);
      expect(warnings).toEqual(expect.arrayContaining([
        expect.objectContaining({ foodId: createdFoodIds[0], alertType: 'Allergy', message: expect.stringMatching(/\\S/) })
      ]));
      expect(JSON.stringify(warnings)).toMatch(/al[eé]rgia|allerg/i);
    });

    await test.step('trazas incompatibles', async () => {
      const warnings = await validateDraft(page, createdFoodIds[1]);
      expect(warnings).toEqual(expect.arrayContaining([
        expect.objectContaining({ foodId: createdFoodIds[1], alertType: 'Allergy', message: expect.stringMatching(/\\S/) })
      ]));
      expect(JSON.stringify(warnings)).toMatch(/al[eé]rgia|allerg/i);
    });

    await test.step('metadatos desconocidos', async () => {
      const warnings = await validateDraft(page, createdFoodIds[2]);
      expect(warnings).toEqual(expect.arrayContaining([
        expect.objectContaining({ foodId: createdFoodIds[2], alertType: 'AllergenDataUnknown', message: expect.stringMatching(/\\S/) })
      ]));
      expect(JSON.stringify(warnings)).toMatch(/desconocid|incomplet|unknown|allergen|al[eé]rgen/i);
    });
  });
});
