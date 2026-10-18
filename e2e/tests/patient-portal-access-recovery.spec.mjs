import { test, expect } from '@playwright/test';

// El endpoint de recuperación se intercepta en el navegador: estas pruebas
// nunca envían correos reales ni revelan si una cuenta existe.
async function openPortal(page) {
  await page.goto('/patient');
  await expect(page.getByRole('heading', { name: 'Portal del Paciente' })).toBeVisible();
  await expect(page.locator('.patient-login-screen')).toBeVisible();
  await expect(page.locator('.access-link-recovery')).toBeVisible();
}

test.describe('P0 — recuperación segura del acceso al portal del paciente', () => {
  test('un email vacío no genera una petición de recuperación', async ({ page }) => {
    let requests = 0;
    await page.route('**/portal/request-access-link', async route => {
      requests++;
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ message: 'Si el email corresponde a un paciente, recibirás un nuevo enlace de acceso.' }) });
    });
    await openPortal(page);
    const email = page.locator('input[name="accessLinkEmail"]');
    await page.getByRole('button', { name: 'Enviar enlace' }).click();
    expect(await email.evaluate(element => element.validity.valueMissing)).toBe(true);
    expect(requests).toBe(0);
  });

  test('un formato de email inválido no genera una petición', async ({ page }) => {
    let requests = 0;
    await page.route('**/portal/request-access-link', async route => {
      requests++;
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ message: 'ok' }) });
    });
    await openPortal(page);
    const email = page.locator('input[name="accessLinkEmail"]');
    await email.fill('esto-no-es-un-email');
    await page.getByRole('button', { name: 'Enviar enlace' }).click();
    expect(await email.evaluate(element => element.validity.typeMismatch)).toBe(true);
    expect(requests).toBe(0);
  });

  test('el email se normaliza a minúsculas y la respuesta no confirma si existe la cuenta', async ({ page }) => {
    let body;
    await page.route('**/portal/request-access-link', async route => {
      body = route.request().postDataJSON();
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ message: 'Si el email corresponde a un paciente, recibirás un nuevo enlace de acceso.' })
      });
    });
    await openPortal(page);
    await page.locator('input[name="accessLinkEmail"]').fill('PACIENTE-E2E@INVALID.EXAMPLE');
    await page.getByRole('button', { name: 'Enviar enlace' }).click();
    await expect(page.locator('.auth-success-banner')).toContainText('Si el email corresponde a un paciente');
    expect(body).toEqual({ email: 'paciente-e2e@invalid.example' });
    await expect(page.locator('.portal-content')).toHaveCount(0);
  });

  test('un error del servidor se muestra de forma comprensible y permite reintentar', async ({ page }) => {
    let requests = 0;
    await page.route('**/portal/request-access-link', async route => {
      requests++;
      if (requests === 1) {
        await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Servicio temporalmente no disponible.' }) });
      } else {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ message: 'Si el email corresponde a un paciente, recibirás un nuevo enlace de acceso.' }) });
      }
    });
    await openPortal(page);
    await page.locator('input[name="accessLinkEmail"]').fill('paciente-e2e@invalid.example');
    await page.getByRole('button', { name: 'Enviar enlace' }).click();
    await expect(page.locator('.access-link-recovery .auth-error-banner')).toContainText('Servicio temporalmente no disponible.');
    await page.getByRole('button', { name: 'Enviar enlace' }).click();
    await expect(page.locator('.auth-success-banner')).toBeVisible();
    expect(requests).toBe(2);
  });

  test('la recuperación no autentica ni abre el contenido clínico', async ({ page }) => {
    await page.route('**/portal/request-access-link', route => route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ message: 'Si el email corresponde a un paciente, recibirás un nuevo enlace de acceso.' })
    }));
    await openPortal(page);
    await page.locator('input[name="accessLinkEmail"]').fill('paciente-e2e@invalid.example');
    await page.getByRole('button', { name: 'Enviar enlace' }).click();
    await expect(page.locator('.auth-success-banner')).toBeVisible();
    await expect(page.locator('.patient-login-screen')).toBeVisible();
    await expect(page.locator('.portal-content')).toHaveCount(0);
    await expect(page.locator('app-patient-header')).toHaveCount(0);
  });
});
