import { test, expect } from '@playwright/test';

test.describe('P0 — límites de acceso del portal del paciente', () => {
  test('el portal público presenta un acceso PIN y no revela datos de pacientes sin autenticación', async ({ page }) => {
    const response = await page.goto('/patient');
    expect(response, 'El portal debe responder a la navegación').not.toBeNull();
    expect(response.status()).toBeLessThan(400);

    await expect(page.getByRole('heading', { name: 'Portal del Paciente' })).toBeVisible();
    await expect(page.getByLabel('Email o Teléfono')).toBeVisible();
    await expect(page.getByLabel('PIN de Acceso')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Acceder al Portal' })).toBeVisible();
    await expect(page.locator('.patient-login-screen')).toBeVisible();
    await expect(page.locator('.portal-content')).toHaveCount(0);
  });

  test('un PIN inválido no abre el portal ni expone datos', async ({ page }) => {
    await page.goto('/patient');
    await expect(page.getByRole('heading', { name: 'Portal del Paciente' })).toBeVisible();

    await page.getByLabel('Email o Teléfono').fill('e2e-no-existe@invalid.example');
    await page.getByLabel('PIN de Acceso').fill('000000');
    await page.getByRole('button', { name: 'Acceder al Portal' }).click();

    await expect(page.locator('.auth-error-banner')).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.patient-login-screen')).toBeVisible();
    await expect(page.locator('.portal-content')).toHaveCount(0);
    await expect(page.locator('app-patient-header')).toHaveCount(0);
  });
});

test.describe('P1 — validación local de la consulta online', () => {
  test('una autorización de videollamada fallida muestra un error comprensible y permite reintentar', async ({ page }) => {
    let attempts = 0;
    await page.route('**/api/appointments/77/video-access', async route => {
      attempts++;
      await route.fulfill({
        status: 409,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'La sala estará disponible 15 minutos antes de la cita.' })
      });
    });

    await page.goto('/video-consultation/77');
    await expect(page.getByRole('heading', { name: 'No se puede iniciar o recuperar la consulta' })).toBeVisible();
    await expect(page.getByText('La sala estará disponible 15 minutos antes de la cita.')).toBeVisible();
    await page.getByRole('button', { name: 'Reintentar conexión' }).click();
    await expect.poll(() => attempts).toBe(2);
    await expect(page.getByRole('heading', { name: 'No se puede iniciar o recuperar la consulta' })).toBeVisible();
  });

  test('un identificador de cita inválido no solicita acceso a LiveKit ni abre una sala', async ({ page }) => {
    let videoAccessRequests = 0;
    page.on('request', request => {
      if (/\/api\/.*video.*access|\/api\/appointments\/[^/]+\/video/i.test(request.url())) {
        videoAccessRequests++;
      }
    });

    const response = await page.goto('/video-consultation/0');
    expect(response, 'La ruta de consulta online debe responder').not.toBeNull();
    expect(response.status()).toBeLessThan(400);

    await expect(page.getByRole('heading', { name: 'Consulta online' })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'No se puede iniciar o recuperar la consulta' })).toBeVisible();
    await expect(page.getByText('La cita de consulta online no es válida.')).toBeVisible();
    expect(videoAccessRequests, 'No debe solicitarse token de LiveKit para un ID inválido').toBe(0);
    await expect(page.getByRole('button', { name: 'Reintentar conexión' })).toBeVisible();
  });
});

test.describe('P0 — rutas profesionales y administrativas sin sesión', () => {
  for (const route of ['/appointments', '/clients', '/diets', '/messages', '/documents', '/admin', '/admin/coupons', '/admin/legal']) {
    test(`redirige ${route} al acceso cuando no hay sesión`, async ({ page }) => {
      await page.goto(route);
      await expect(page).toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 10_000 });
      await expect(page.getByRole('heading', { name: 'Inicia sesión' })).toBeVisible();
    });
  }
});
