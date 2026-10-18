import { test, expect } from '@playwright/test';

// Las APIs se simulan: no se consulta ni modifica información clínica real.
async function mockPortalApi(page) {
  const calls = [];
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    calls.push({ path: url.pathname, method: request.method(), body: request.postDataJSON?.() });
    if (url.pathname.endsWith('/portal/auth') && request.method() === 'POST') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ clientId: 321, fullName: 'Paciente E2E Ficticio' }) });
    } else if (url.pathname.endsWith('/portal/profile')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ id: 321, fullName: 'Paciente E2E Ficticio', hasActiveDiet: false, clinicName: 'Clínica E2E', nutritionistName: 'Nutricionista E2E' }) });
    } else if (url.pathname.endsWith('/commercial-communications/preferences')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ emailEnabled: false }) });
    } else if (url.pathname.endsWith('/portal/push/vapid-public-key')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ publicKey: '' }) });
    } else {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
    }
  });
  return calls;
}

async function loginByPin(page) {
  await page.goto('/patient');
  await expect(page.getByRole('heading', { name: 'Portal del Paciente' })).toBeVisible();
  await page.getByLabel('Email o Teléfono').fill('paciente-e2e@invalid.example');
  await page.getByLabel('PIN de Acceso').fill('123456');
  await page.getByRole('button', { name: 'Acceder al Portal' }).click();
  await expect(page.locator('.patient-login-screen')).toHaveCount(0, { timeout: 15_000 });
  await expect(page.locator('app-patient-header')).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('.portal-content').first()).toBeVisible({ timeout: 15_000 });
}

test.describe('P0 — portal autenticado del paciente', () => {
  test('el PIN válido abre el portal y carga el perfil sin exponer datos de otro paciente', async ({ page }) => {
    const calls = await mockPortalApi(page);
    await loginByPin(page);
    await expect(page.locator('app-patient-header')).toContainText('Paciente E2E Ficticio');
    expect(calls.some(call => call.path.endsWith('/portal/auth') && call.method === 'POST' && call.body.emailOrPhone === 'paciente-e2e@invalid.example' && call.body.passcode === '123456')).toBe(true);
    expect(calls.some(call => call.path.endsWith('/portal/profile') && call.method === 'GET')).toBe(true);
    await expect(page.locator('.patient-login-screen')).toHaveCount(0);
  });

  test('las seis secciones principales se pueden abrir desde la navegación del portal', async ({ page }) => {
    await mockPortalApi(page);
    await loginByPin(page);
    const tabs = [
      ['Ver plan de hoy', 'today'],
      ['Ver mis citas', 'appointments'],
      ['Ver lista de la compra', 'shopping'],
      ['Ver mis mensajes', 'messages'],
      ['Ver mi documentación', 'documents'],
      ['Ver mi progreso', 'progress']
    ];
    for (const [label] of tabs) {
      const tab = page.getByRole('button', { name: label, exact: true });
      await tab.click();
      await expect(tab).toHaveAttribute('aria-current', 'page');
    }
  });

  test('cerrar sesión oculta inmediatamente el contenido del portal y vuelve al acceso', async ({ page }) => {
    const calls = await mockPortalApi(page);
    await loginByPin(page);
    await page.locator('.portal-logout-btn').click();
    await expect(page.locator('.patient-login-screen')).toBeVisible({ timeout: 10_000 });
    await expect(page.locator('app-patient-header')).toHaveCount(0);
    await expect(page.locator('.portal-content')).toHaveCount(0);
    expect(calls.some(call => call.path.endsWith('/portal/logout') && call.method === 'POST')).toBe(true);
  });
});
