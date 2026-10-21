import { test, expect } from '@playwright/test';

// El portal y el seguimiento se prueban con respuestas API ficticias.
async function mockPortal(page, { saveError = null, eligible = true } = {}) {
  const saves = [];
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    if (url.pathname.endsWith('/portal/auth') && method === 'POST') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ clientId: 321, fullName: 'Paciente E2E Ficticio' }) });
    } else if (url.pathname.endsWith('/portal/profile') && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ id: 321, fullName: 'Paciente E2E Ficticio', hasActiveDiet: false }) });
    } else if (url.pathname.endsWith('/portal/check-ins/eligibility') && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ eligible, message: eligible ? null : 'La revisión semanal estará disponible después de tu primera consulta.' }) });
    } else if (url.pathname.endsWith('/portal/check-ins/current') && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: 'null' });
    } else if (url.pathname.endsWith('/portal/check-ins') && method === 'POST') {
      const payload = request.postDataJSON();
      saves.push(payload);
      if (saveError) {
        await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: saveError }) });
      } else {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ id: 88, submitted_at: new Date().toISOString(), ...payload }) });
      }
    } else if (url.pathname.endsWith('/commercial-communications/preferences')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ emailEnabled: false }) });
    } else if (url.pathname.endsWith('/portal/push/vapid-public-key')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ publicKey: '' }) });
    } else {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
    }
  });
  return saves;
}

async function loginByPin(page, { eligible = true } = {}) {
  await page.goto('/patient');
  await expect(page.getByRole('heading', { name: 'Portal del Paciente' })).toBeVisible();
  await page.getByLabel('Email o Teléfono').fill('paciente-e2e@invalid.example');
  await page.getByLabel('PIN de Acceso').fill('123456');
  await page.getByRole('button', { name: 'Acceder al Portal' }).click();
  await expect(page.locator('app-patient-header')).toBeVisible({ timeout: 15_000 });
  if (eligible) await expect(page.locator('.weekly-checkin')).toBeVisible({ timeout: 15_000 });
  else await expect(page.locator('.checkin-first-consult')).toBeVisible({ timeout: 15_000 });
}

test.describe('P1 — seguimiento semanal del paciente', () => {
  test('guarda una revisión semanal y envía los valores introducidos', async ({ page }) => {
    const saves = await mockPortal(page);
    await loginByPin(page);
    await page.locator('input[name="checkinWeight"]').fill('82.5');
    await page.locator('input[name="checkinAdherence"]').fill('80');
    await page.locator('input[name="checkinHunger"]').fill('4');
    await page.locator('input[name="checkinEnergy"]').fill('7');
    await page.locator('textarea[name="checkinDifficulties"]').fill('Semana de prueba ficticia');
    await page.locator('input[name="barrier-time"]').check();
    await page.locator('input[name="barrier-social"]').check();
    await page.getByRole('button', { name: 'Guardar revisión' }).click();
    await expect(page.locator('.checkin-saved')).toContainText('Revisión guardada');
    expect(saves).toHaveLength(1);
    await expect(page.getByRole('button', { name: 'Guardar revisión' })).toHaveCount(0);
    await expect(page.locator('.checkin-already-submitted')).toContainText('Podrás rellenarla de nuevo la próxima semana');
    expect(saves[0]).toMatchObject({ weight: 82.5, adherence: 80, hunger: 4, energy: 7, difficulties: 'Semana de prueba ficticia', barriers: ['time', 'social'] });
  });


  test('no solicita revisión semanal antes de la primera consulta realizada', async ({ page }) => {
    await mockPortal(page, { eligible: false });
    await loginByPin(page, { eligible: false });
    await expect(page.locator('.checkin-first-consult')).toContainText('después de tu primera consulta');
    await expect(page.locator('.weekly-checkin')).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Guardar revisión' })).toHaveCount(0);
  });

  test('la validación HTML impide guardar un peso fuera del rango permitido', async ({ page }) => {
    const saves = await mockPortal(page);
    await loginByPin(page);
    const weight = page.locator('input[name="checkinWeight"]');
    await weight.fill('600');
    await page.getByRole('button', { name: 'Guardar revisión' }).click();
    expect(await weight.evaluate(element => element.validity.rangeOverflow)).toBe(true);
    expect(saves).toHaveLength(0);
  });

  test('un fallo al guardar la revisión se comunica sin perder los datos escritos', async ({ page }) => {
    const saves = await mockPortal(page, { saveError: 'El seguimiento no está disponible temporalmente.' });
    await loginByPin(page);
    const difficulties = page.locator('textarea[name="checkinDifficulties"]');
    await difficulties.fill('No perder este comentario durante el error.');
    await page.getByRole('button', { name: 'Guardar revisión' }).click();
    await expect(page.locator('.portal-inline-error')).toContainText('El seguimiento no está disponible temporalmente.');
    await expect(difficulties).toHaveValue('No perder este comentario durante el error.');
    expect(saves).toHaveLength(1);
  });
});
