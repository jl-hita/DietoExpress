import { test, expect } from '@playwright/test';

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
const patientName = `ZZZ E2E paciente UI ${new Date().toISOString().replace(/[:.]/g, '-')}`;
let patientId = 0;

test.describe('P1 — gestión de pacientes desde la interfaz', () => {
  test.beforeEach(async ({ page }) => {
    test.skip(!username || !password, 'Configura E2E_USERNAME y E2E_PASSWORD para ejecutar las pruebas de pacientes.');

    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });
  });

  test.afterEach(async ({ page }) => {
    const api = page.context().request;
    if (!patientId) {
      const response = await api.get(new URL('/api/clients?search=' + encodeURIComponent(patientName) + '&page=1&pageSize=100', baseURL).toString());
      if (response.ok()) {
        const listing = await response.json();
        const patients = Array.isArray(listing) ? listing : (listing.items || listing.Items || []);
        const created = patients.find(patient => (patient.fullName ?? patient.FullName) === patientName);
        patientId = Number(created?.id ?? created?.Id ?? 0);
      }
    }

    if (patientId > 0) {
      const response = await api.delete(new URL('/api/clients/' + patientId, baseURL).toString());
      expect([200, 204, 404].includes(response.status()), 'La ficha ficticia E2E debe archivarse al terminar').toBe(true);
      patientId = 0;
    }
  });

  test('crear un paciente, abrir su ficha y verificar los datos guardados', async ({ page }) => {
    await page.goto('/clients/nuevo');
    await expect(page.getByRole('heading', { name: 'Nuevo Cliente' })).toBeVisible();
    await expect(page.getByLabel('Nombre completo')).toBeVisible();

    await page.getByLabel('Nombre completo').fill(patientName);
    await page.getByLabel('Email').fill(`e2e-${Date.now()}@invalid.example`);
    await page.getByLabel('Notas').fill('Registro ficticio creado automáticamente por Playwright; archivar al finalizar.');
    await page.getByRole('button', { name: 'Crear cliente' }).click();

    await expect(page).toHaveURL(/\/clients\/\d+(?:[/?#]|$)/, { timeout: 20_000 });
    const match = page.url().match(/\/clients\/(\d+)/);
    expect(match, 'La aplicación debe navegar a la ficha creada').not.toBeNull();
    patientId = Number(match[1]);

    await expect(page.locator('body')).toContainText(patientName);
    const apiResponse = await page.context().request.get(new URL('/api/clients/' + patientId, baseURL).toString());
    expect(apiResponse.ok(), 'La ficha creada debe persistir en la API').toBeTruthy();
    const patient = await apiResponse.json();
    expect(patient.fullName ?? patient.FullName).toBe(patientName);
    expect(patient.notes ?? patient.Notes).toContain('Playwright');
  });
});
