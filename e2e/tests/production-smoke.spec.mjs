import { test, expect } from '@playwright/test';

// Estas pruebas introducen credenciales reales de GitHub Secrets: no guardar trazas DOM que puedan contenerlas.
test.use({ trace: 'off' });

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;

test.describe('DietoExpress — comprobaciones de navegador no destructivas', () => {
  test('la portada pública se carga', async ({ page }) => {
    const uncaughtErrors = [];
    page.on('pageerror', error => uncaughtErrors.push(error.message));

    const response = await page.goto('/');
    expect(response, 'La navegación debe recibir una respuesta HTTP').not.toBeNull();
    expect(response.status(), 'La portada debe responder correctamente').toBeLessThan(400);
    await expect(page.locator('body')).toContainText(/DietoExpress/i);
    expect(uncaughtErrors, 'No debe haber excepciones JavaScript no controladas').toEqual([]);
  });

  test('el formulario de acceso presenta sus campos principales', async ({ page }) => {
    const response = await page.goto('/login');
    expect(response).not.toBeNull();
    expect(response.status()).toBeLessThan(400);

    await expect(page.getByRole('heading', { name: 'Inicia sesión' })).toBeVisible();
    await expect(page.getByLabel('Email o usuario')).toBeVisible();
    await expect(page.getByLabel('Contraseña')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Iniciar sesión' })).toBeVisible();
  });

  test('una sesión anónima no puede abrir la zona profesional', async ({ page }) => {
    await page.goto('/clients');
    await expect(page).toHaveURL(/\/login(?:[/?#]|$)/);
    await expect(page.getByRole('heading', { name: 'Inicia sesión' })).toBeVisible();
  });

  test('una cuenta de prueba puede iniciar sesión sin modificar datos', async ({ page }) => {
    test.skip(!username || !password, 'Configura E2E_USERNAME y E2E_PASSWORD en GitHub Actions para habilitar esta prueba.');

    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    // Limpiar el campo del DOM antes de que un fallo posterior genere capturas/contexto.
    await page.locator('input[type="password"]').evaluateAll(inputs => inputs.forEach(input => { input.value = ''; }));

    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });
    await expect(page.locator('body')).not.toContainText(/credenciales incorrectas|error al iniciar sesión/i);
    await expect(page.locator('body')).not.toBeEmpty();
  });
});
