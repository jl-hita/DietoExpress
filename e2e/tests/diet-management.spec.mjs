import { test, expect } from '@playwright/test';

// Estas pruebas introducen credenciales reales de GitHub Secrets: no guardar trazas DOM que puedan contenerlas.
test.use({ trace: 'off' });

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
    // No borrar la contraseña hasta que termine la autenticación: vaciarla inmediatamente
    // después del click puede competir con la serialización del formulario.
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });
    await page.locator('input[type="password"]').evaluateAll(inputs => inputs.forEach(input => { input.value = ''; }));
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

    // Comprobar ambos caminos del diálogo nativo sin afectar a datos preexistentes:
    // la dieta creada por esta prueba se elimina al confirmar; cancelar no muta el backend.
    await page.goto('/diets');
    const search = page.getByPlaceholder('Buscar por nombre de dieta...');
    await search.fill(dietName);
    const row = page.locator('tr').filter({ hasText: dietName });
    await expect(row).toBeVisible({ timeout: 15_000 });
    let deleteRequests = 0;
    page.on('request', request => {
      if (request.method() === 'DELETE' && new URL(request.url()).pathname.endsWith(`/api/dietas/${dietId}`)) deleteRequests++;
    });

    page.once('dialog', dialog => dialog.dismiss());
    await row.locator('button.delete-btn').click();
    await expect(row).toBeVisible();
    await page.waitForTimeout(300);
    expect(deleteRequests, 'Cancelar debe conservar la dieta sin enviar DELETE').toBe(0);

    page.once('dialog', dialog => dialog.accept());
    const deleteResponsePromise = page.waitForResponse(response =>
      response.request().method() === 'DELETE' &&
      new URL(response.url()).pathname.endsWith(`/api/dietas/${dietId}`));
    await row.locator('button.delete-btn').click();
    const deleteResponse = await deleteResponsePromise;
    expect([200, 204].includes(deleteResponse.status()), 'Confirmar debe eliminar la dieta de prueba').toBe(true);
    dietId = 0;
  });
});
