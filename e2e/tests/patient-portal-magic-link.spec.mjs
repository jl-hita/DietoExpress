import { test, expect } from '@playwright/test';

// Todas las APIs se simulan; no se accede a datos reales de pacientes.
test.describe('P0 — acceso al portal mediante enlace mágico', () => {
  test('un token válido mantiene la sesión al retirar el token de la URL', async ({ page }) => {
    const calls = [];
    await page.route('**/api/**', async route => {
      const request = route.request();
      const url = new URL(request.url());
      calls.push({ path: url.pathname, method: request.method() });
      if (url.pathname.endsWith('/portal/auth')) {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ clientId: 321, fullName: 'Paciente E2E Ficticio' }) });
      } else if (url.pathname.endsWith('/portal/profile')) {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ id: 321, fullName: 'Paciente E2E Ficticio', hasActiveDiet: false, clinicName: 'Clínica E2E' }) });
      } else if (url.pathname.endsWith('/commercial-communications/preferences')) {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ emailEnabled: false }) });
      } else if (url.pathname.endsWith('/portal/push/vapid-public-key')) {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ publicKey: '' }) });
      } else {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
      }
    });

    await page.goto('/patient?token=e2e-valid-magic-token');

    await expect(page).toHaveURL(/\/patient(?:[?#]|$)/, { timeout: 15_000 });
    await expect(page.locator('.patient-login-screen')).toHaveCount(0, { timeout: 15_000 });
    await expect(page.locator('.portal-content').first()).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('app-patient-header')).toBeVisible();
    expect(calls.some(call => call.path.endsWith('/portal/auth') && call.method === 'POST')).toBe(true);
    expect(calls.some(call => call.path.endsWith('/portal/profile') && call.method === 'GET')).toBe(true);
  });

  test('un token inválido no abre la sesión ni muestra datos clínicos', async ({ page }) => {
    await page.route('**/api/portal/auth', route => route.fulfill({
      status: 401,
      contentType: 'application/json',
      body: JSON.stringify({ message: 'El enlace de acceso ha expirado o no es válido.' })
    }));
    await page.goto('/patient?token=e2e-invalid-magic-token');
    await expect(page.locator('.patient-login-screen')).toBeVisible();
    await expect(page.locator('.auth-error-banner')).toContainText('El enlace de acceso ha expirado o no es válido.');
    await expect(page.locator('.portal-content')).toHaveCount(0);
    await expect(page.locator('app-patient-header')).toHaveCount(0);
  });
});
