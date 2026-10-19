import { test, expect } from '@playwright/test';

// Estas pruebas introducen credenciales reales de GitHub Secrets: no guardar trazas DOM que puedan contenerlas.
test.use({ trace: 'off' });

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
const testPatientName = 'ZZZ E2E — paciente ficticio alérgenos';
let clientId = 0;
let createdFoodIds = [];
let createdDietIds = [];
let createdRecipeIds = [];

test.describe('P0 — validación clínica de alérgenos (fixtures aislados)', () => {
  test.beforeEach(async ({ page }) => {
    test.skip(!username || !password, 'Configura E2E_USERNAME y E2E_PASSWORD para ejecutar las pruebas clínicas.');

    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    try {
      await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    } finally {
      // Limpiar incluso si el click falla; las trazas están desactivadas en estos tests.
      await page.locator('input[type="password"]').evaluateAll(inputs => inputs.forEach(input => { input.value = ''; }));
    }
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
    createdDietIds = [];
    createdRecipeIds = [];
    for (const fixture of [
      { name: 'Pollo E2E temporal A', category: 'Carnes', kcal: 100, protein: 20, carbs: 0, fat: 2, allergenTags: ['cacahuete'], tracesTags: [], allergenDataStatus: 'reported' },
      { name: 'Arroz E2E temporal B', category: 'Cereales', kcal: 130, protein: 3, carbs: 28, fat: 1, allergenTags: [], tracesTags: ['leche'], allergenDataStatus: 'reported' },
      { name: 'Patata E2E temporal C', category: 'Tubérculos', kcal: 80, protein: 2, carbs: 18, fat: 0, allergenTags: [], tracesTags: [], allergenDataStatus: 'unknown' }
    ]) {
      const response = await api.post(new URL('/api/foods', baseURL).toString(), {
        data: {
          name: fixture.name,
          category: fixture.category,
          kcal: fixture.kcal,
          protein: fixture.protein,
          carbs: fixture.carbs,
          fat: fixture.fat,
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
    for (const dietId of [...createdDietIds].reverse()) {
      const clearResponse = await api.put(new URL('/api/dietas/' + dietId, baseURL).toString(), {
        data: { name: 'ZZZ E2E dieta temporal', targetKcal: 1800, days: [] }
      });
      expect([204, 404].includes(clearResponse.status()), 'La dieta temporal debe vaciarse antes de archivarse').toBe(true);
      const deleteResponse = await api.delete(new URL('/api/dietas/' + dietId, baseURL).toString());
      expect([204, 404].includes(deleteResponse.status()), 'La dieta temporal E2E debe archivarse al finalizar').toBe(true);
    }
    for (const recipeId of [...createdRecipeIds].reverse()) {
      const response = await api.delete(new URL('/api/recipes/' + recipeId, baseURL).toString());
      expect([204, 404].includes(response.status()), 'La receta temporal E2E debe eliminarse al finalizar la prueba').toBe(true);
    }
    for (const foodId of [...createdFoodIds].reverse()) {
      const response = await api.delete(new URL('/api/foods/' + foodId, baseURL).toString());
      expect([204, 404].includes(response.status()), 'El alimento temporal E2E debe eliminarse al finalizar la prueba').toBe(true);
    }
    if (clientId > 0) {
      const archiveResponse = await api.delete(new URL('/api/clients/' + clientId, baseURL).toString());
      expect([204, 404].includes(archiveResponse.status()), 'La ficha ficticia E2E debe archivarse al finalizar').toBe(true);
      clientId = 0;
    }
    createdDietIds = [];
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
        expect.objectContaining({ foodId: createdFoodIds[0], alertType: 'Allergy', message: expect.stringMatching(/\S/) })
      ]));
      expect(JSON.stringify(warnings)).toMatch(/al[eé]rgia|allerg/i);
    });

    await test.step('trazas incompatibles', async () => {
      const warnings = await validateDraft(page, createdFoodIds[1]);
      expect(warnings).toEqual(expect.arrayContaining([
        expect.objectContaining({ foodId: createdFoodIds[1], alertType: 'Allergy', message: expect.stringMatching(/\S/) })
      ]));
      expect(JSON.stringify(warnings)).toMatch(/al[eé]rgia|allerg/i);
    });

    await test.step('receta y lista de compra propagan alérgenos y trazas; asignación incompatible se bloquea', async () => {
      const recipeResponse = await page.context().request.post(new URL('/api/recipes', baseURL).toString(), {
        data: {
          name: 'ZZZ E2E receta temporal alérgenos',
          instructions: 'Fixture automatizado; no usar clínicamente.',
          servings: 1,
          ingredients: [
            { foodId: createdFoodIds[0], grams: 100 },
            { foodId: createdFoodIds[1], grams: 100 }
          ]
        }
      });
      expect(recipeResponse.status(), 'Debe crearse la receta temporal para probar propagación clínica').toBe(201);
      const recipe = await recipeResponse.json();
      const recipeId = Number(recipe.id ?? recipe.Id);
      expect(Number.isSafeInteger(recipeId) && recipeId > 0).toBe(true);
      createdRecipeIds.push(recipeId);

      const recipeDetailResponse = await page.context().request.get(new URL('/api/recipes/' + recipeId, baseURL).toString());
      expect(recipeDetailResponse.status()).toBe(200);
      const recipeDetail = await recipeDetailResponse.json();
      expect(recipeDetail.allergenTags ?? recipeDetail.AllergenTags ?? []).toContain('cacahuete');
      expect(recipeDetail.traceTags ?? recipeDetail.TraceTags ?? []).toContain('leche');
      const ingredients = recipeDetail.ingredients ?? recipeDetail.Ingredients ?? [];
      expect(ingredients.map(item => Number(item.foodId ?? item.FoodId))).toEqual(
        expect.arrayContaining([createdFoodIds[0], createdFoodIds[1]])
      );

      const dietResponse = await page.context().request.post(new URL('/api/dietas', baseURL).toString(), {
        data: {
          name: 'ZZZ E2E dieta temporal con receta',
          targetKcal: 1800,
          days: [{
            dayIndex: 0,
            meals: [{
              name: 'Comida principal',
              mealIndex: 0,
              items: [{ recipeId, recipeServings: 1, grams: 100 }]
            }]
          }]
        }
      });
      expect(dietResponse.status(), 'Debe poder crearse una dieta temporal no asignada que use la receta').toBe(201);
      const diet = await dietResponse.json();
      const dietId = Number(diet.id ?? diet.Id);
      expect(Number.isSafeInteger(dietId) && dietId > 0).toBe(true);
      createdDietIds.push(dietId);

      const shoppingResponse = await page.context().request.get(new URL('/api/dietas/' + dietId + '/shopping-list', baseURL).toString());
      expect(shoppingResponse.status()).toBe(200);
      const categories = await shoppingResponse.json();
      const shoppingItems = (Array.isArray(categories) ? categories : []).flatMap(category =>
        category.items ?? category.Items ?? []);
      const peanutItem = shoppingItems.find(item => Number(item.foodId ?? item.FoodId) === createdFoodIds[0]);
      const traceItem = shoppingItems.find(item => Number(item.foodId ?? item.FoodId) === createdFoodIds[1]);
      expect(peanutItem, 'La lista debe expandir la receta y propagar el alimento con alérgeno').toBeTruthy();
      expect(traceItem, 'La lista debe expandir la receta y propagar el alimento con trazas').toBeTruthy();
      expect(peanutItem.allergenTags ?? peanutItem.AllergenTags ?? []).toContain('cacahuete');
      expect(traceItem.traceTags ?? traceItem.TraceTags ?? []).toContain('leche');

      const assignmentResponse = await page.context().request.post(new URL('/api/clients/' + clientId + '/diets', baseURL).toString(), {
        data: { dietId, startDate: new Date().toISOString(), notes: 'Prueba automatizada de incompatibilidad; no es una asignación clínica real.' }
      });
      expect(assignmentResponse.status(), 'No debe asignarse una dieta cuya receta contiene alérgeno y trazas incompatibles').toBe(409);
      const assignmentBody = await assignmentResponse.json();
      expect(JSON.stringify(assignmentBody)).toMatch(/incompatibil|al[eé]rg|allergen|cacahuete|leche/i);
    });

    await test.step('generación excluye alérgenos y trazas conocidos; datos desconocidos generan avisos', async () => {
      const response = await page.context().request.post(new URL('/api/dietas/generate', baseURL).toString(), {
        data: {
          clientId,
          targetKcal: 2000,
          numberOfDays: 1,
          mealsPerDay: 5,
          dietType: 'Equilibrada'
          // El resto de opciones usa los valores de producción para que la prueba
          // compruebe el flujo real de generación, incluido el optimizador y las recetas.
        }
      });
      const generationBody = await response.text();
      expect(response.status(), 'La generación clínica debe completar una dieta válida. Respuesta API: ' + generationBody).toBe(200);
      const generated = JSON.parse(generationBody);
      const generatedFoodIds = (generated.days ?? generated.Days ?? []).flatMap(day =>
        (day.meals ?? day.Meals ?? []).flatMap(meal =>
          (meal.items ?? meal.Items ?? []).map(item => Number(item.foodId ?? item.FoodId))
            .filter(id => Number.isSafeInteger(id) && id > 0)));
      for (const forbiddenFoodId of createdFoodIds.slice(0, 2)) {
        expect(generatedFoodIds, 'La dieta generada no debe incorporar alimentos con alérgenos o trazas incompatibles').not.toContain(forbiddenFoodId);
      }
    });

    await test.step('sugerencias de sustitución respetan restricciones del paciente', async () => {
      for (const sourceId of [createdFoodIds[0], createdFoodIds[1]]) {
        const url = new URL('/api/foods/' + sourceId + '/substitutions', baseURL);
        url.searchParams.set('target', 'balanced');
        url.searchParams.set('limit', '50');
        url.searchParams.set('clientId', String(clientId));
        const response = await page.context().request.get(url.toString());
        expect(response.status(), 'Las sugerencias deben respetar el contexto clínico').toBe(200);
        const suggestions = await response.json();
        expect(Array.isArray(suggestions)).toBe(true);
        for (const forbiddenFoodId of createdFoodIds.slice(0, 2)) {
          if (forbiddenFoodId !== sourceId) {
            expect(suggestions.map(item => Number(item.id ?? item.Id))).not.toContain(forbiddenFoodId);
          }
        }
      }
    });

    await test.step('regeneración no selecciona alimentos incompatibles ni datos desconocidos', async () => {
      const createResponse = await page.context().request.post(new URL('/api/dietas', baseURL).toString(), {
        data: {
          name: 'ZZZ E2E dieta temporal',
          targetKcal: 1800,
          days: [{
            dayIndex: 0,
            meals: [{
              name: 'Comida principal',
              mealIndex: 0,
              items: [{ foodId: null, grams: 100, kcal: 600, protein: 30, carbs: 60, fat: 12 }]
            }]
          }]
        }
      });
      expect(createResponse.status(), 'Debe poder crearse una dieta temporal no asignada').toBe(201);
      const createdDiet = await createResponse.json();
      const dietId = Number(createdDiet.id ?? createdDiet.Id);
      expect(Number.isSafeInteger(dietId) && dietId > 0).toBe(true);
      createdDietIds.push(dietId);

      const detailResponse = await page.context().request.get(new URL('/api/dietas/' + dietId, baseURL).toString());
      expect(detailResponse.status()).toBe(200);
      const detail = await detailResponse.json();
      const days = detail.days ?? detail.Days ?? [];
      const meals = days[0]?.meals ?? days[0]?.Meals ?? [];
      const mealId = Number(meals[0]?.id ?? meals[0]?.Id);
      expect(Number.isSafeInteger(mealId) && mealId > 0, 'La dieta temporal debe devolver el ID de la comida').toBe(true);

      const regenerateResponse = await page.context().request.post(new URL('/api/dietas/' + dietId + '/regenerate', baseURL).toString(), {
        data: { operation: 'regenerate-meal', mealId, clientId, preserveNutritionTargets: false, preserveWeeklyVariety: false }
      });
      expect(regenerateResponse.status(), 'La regeneración clínica debe completarse').toBe(200);
      const regenerated = await regenerateResponse.json();
      expect(regenerated.success ?? regenerated.Success).toBe(true);

      const afterResponse = await page.context().request.get(new URL('/api/dietas/' + dietId, baseURL).toString());
      expect(afterResponse.status()).toBe(200);
      const after = await afterResponse.json();
      const afterDays = after.days ?? after.Days ?? [];
      const regeneratedFoodIds = afterDays.flatMap(day =>
        (day.meals ?? day.Meals ?? []).flatMap(meal =>
          (meal.items ?? meal.Items ?? []).map(item => Number(item.foodId ?? item.FoodId))
            .filter(id => Number.isSafeInteger(id) && id > 0)));
      for (const forbiddenFoodId of createdFoodIds.slice(0, 2)) {
        expect(regeneratedFoodIds, 'La comida regenerada no debe incorporar alimentos con alérgenos o trazas incompatibles').not.toContain(forbiddenFoodId);
      }
      expect(regeneratedFoodIds.length, 'La regeneración debe elegir al menos un alimento compatible').toBeGreaterThan(0);
    });

    await test.step('metadatos desconocidos', async () => {
      const warnings = await validateDraft(page, createdFoodIds[2]);
      expect(warnings).toEqual(expect.arrayContaining([
        expect.objectContaining({ foodId: createdFoodIds[2], alertType: 'AllergenDataUnknown', message: expect.stringMatching(/\S/) })
      ]));
      expect(JSON.stringify(warnings)).toMatch(/desconocid|incomplet|unknown|allergen|al[eé]rgen/i);
    });
  });
});
