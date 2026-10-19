import { test, expect } from '@playwright/test';

// Estas pruebas introducen credenciales reales de GitHub Secrets: no guardar trazas DOM que puedan contenerlas.
test.use({ trace: 'off' });

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;

test.describe('P1 — recorridos principales y fronteras de acceso', () => {
  test('el directorio público permite entrar y cargar sus variantes principales', async ({ page }) => {
    const uncaughtErrors = [];
    page.on('pageerror', error => uncaughtErrors.push(error.message));

    for (const path of ['/nutricionistas', '/nutricionistas/online', '/para-nutricionistas']) {
      const response = await page.goto(path);
      expect(response, `Debe recibirse respuesta al abrir ${path}`).not.toBeNull();
      expect(response.status(), `La ruta pública ${path} no debe devolver error HTTP`).toBeLessThan(400);
      await expect(page.locator('body')).not.toBeEmpty();
      await expect(page.locator('body')).toContainText(/nutricionist|DietoExpress/i);
    }

    expect(uncaughtErrors, 'Las rutas públicas no deben lanzar excepciones JavaScript').toEqual([]);
  });

  test('las rutas profesionales y administrativas no son accesibles sin sesión', async ({ page }) => {
    for (const path of ['/clients', '/diets', '/appointments', '/billing', '/admin']) {
      await page.goto(path);
      await expect(page, `La ruta ${path} debe requerir autenticación`).toHaveURL(/\/login(?:[/?#]|$)/);
      await expect(page.getByRole('heading', { name: 'Inicia sesión' })).toBeVisible();
    }
  });

  test('una cuenta de prueba puede recorrer las pantallas profesionales esenciales', async ({ page }) => {
    test.skip(!username || !password, 'Configura E2E_USERNAME y E2E_PASSWORD para habilitar el recorrido autenticado.');

    const uncaughtErrors = [];
    page.on('pageerror', error => uncaughtErrors.push(error.message));

    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    // Limpiar el campo del DOM antes de que un fallo posterior genere capturas/contexto.
    await page.locator('input[type="password"]').evaluateAll(inputs => inputs.forEach(input => { input.value = ''; }));
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });

    for (const path of ['/dashboard', '/clients', '/diets', '/appointments', '/billing']) {
      const response = await page.goto(path);
      expect(response, `Debe recibirse respuesta al abrir ${path}`).not.toBeNull();
      expect(response.status(), `La ruta ${path} no debe devolver error HTTP`).toBeLessThan(400);
      await expect(page.locator('body')).not.toBeEmpty();
      await expect(page.locator('body')).toContainText(/DietoExpress|paciente|dieta|suscripci[oó]n|cita|agenda/i);

      const timing = await page.evaluate(() => {
        const navigation = performance.getEntriesByType('navigation')[0];
        const paint = performance.getEntriesByType('paint').find(entry => entry.name === 'first-contentful-paint');
        return {
          domContentLoadedMs: navigation?.domContentLoadedEventEnd ?? 0,
          loadEventMs: navigation?.loadEventEnd ?? 0,
          firstContentfulPaintMs: paint?.startTime ?? 0,
        };
      });
      expect(timing.domContentLoadedMs, `${path}: DOM listo en menos de 10 s`).toBeGreaterThan(0);
      expect(timing.domContentLoadedMs, `${path}: DOM listo en menos de 10 s`).toBeLessThan(10_000);
      expect(timing.loadEventMs, `${path}: evento load en menos de 15 s`).toBeLessThan(15_000);
      if (timing.firstContentfulPaintMs > 0) {
        expect(timing.firstContentfulPaintMs, `${path}: primer contenido en menos de 5 s`).toBeLessThan(5_000);
      }
    }

    expect(uncaughtErrors, 'Las pantallas esenciales no deben lanzar excepciones JavaScript').toEqual([]);
  });
});
