import { test, expect } from '@playwright/test';

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
const dietName = `ZZZ E2E dieta UI ${new Date().toISOString().replace(/[:.]/g, '-')}`;
let dietId = 0;

test.describe('P1 — gestión de dietas desde la interfaz', () => {
  test.beforeEach(async ({ page }) => {
    test.skip(!username || !password, 'Configura E2E_USERNAME y E2E_PASSWORD para probar dietas.');

    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });
  });

  test.afterEach(async ({ page }) => {
    const api = page.context().request;
    if (!dietId) {
      const response = await api.get(new URL('/api/dietas?search=' + encodeURIComponent(dietName) + '&page=1&pageSize=100', baseURL).toString());
      if (response.ok()) {
        const listing = await response.json();
        const diets = Array.isArray(listing) ? listing : (listing.items || listing.Items || []);
        const created = diets.find(diet => (diet.name ?? diet.Name) === dietName);
        dietId = Number(created?.id ?? created?.Id ?? 0);
      }
    }

    if (dietId > 0) {
      const clearResponse = await api.put(new URL('/api/dietas/' + dietId, baseURL).toString(), {
        data: { name: dietName, targetKcal: 1800, days: [] }
      });
      expect([200, 204, 404].includes(clearResponse.status()), 'La dieta E2E debe vaciarse antes de eliminarla').toBe(true);
      const deleteResponse = await api.delete(new URL('/api/dietas/' + dietId, baseURL).toString());
      expect([200, 204, 404].includes(deleteResponse.status()), 'La dieta E2E debe eliminarse al terminar').toBe(true);
      dietId = 0;
    }
  });

  test('crear una dieta, comprobar su persistencia y eliminarla al finalizar', async ({ page }) => {
    await page.goto('/diets/nuevo');
    await expect(page.getByRole('heading', { name: 'Nueva Dieta' })).toBeVisible();
    const nameInput = page.getByPlaceholder('Escribe el nombre de la dieta...');
    await nameInput.fill(dietName);
    await page.getByRole('button', { name: /Crear Dieta/ }).click();

    await expect(page).toHaveURL(/\/diets(?:[/?#]|$)/, { timeout: 20_000 });
    await expect(page.getByText('Dieta creada')).toBeVisible({ timeout: 10_000 });

    const listResponse = await page.context().request.get(new URL('/api/dietas?search=' + encodeURIComponent(dietName) + '&page=1&pageSize=100', baseURL).toString());
    expect(listResponse.ok(), 'La lista de dietas debe responder correctamente').toBeTruthy();
    const listing = await listResponse.json();
    const diets = Array.isArray(listing) ? listing : (listing.items || listing.Items || []);
    const created = diets.find(diet => (diet.name ?? diet.Name) === dietName);
    expect(created, 'La dieta creada desde la interfaz debe persistir').toBeTruthy();
    dietId = Number(created.id ?? created.Id);
    expect(Number.isSafeInteger(dietId) && dietId > 0).toBe(true);

    const detailResponse = await page.context().request.get(new URL('/api/dietas/' + dietId, baseURL).toString());
    expect(detailResponse.ok(), 'La ficha de la dieta debe poder recuperarse').toBeTruthy();
    const detail = await detailResponse.json();
    expect(detail.name ?? detail.Name).toBe(dietName);
  });
});
