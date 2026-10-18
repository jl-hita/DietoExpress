import { test, expect } from '@playwright/test';

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
let createdInterval = null;

test.describe('P1 — agenda y disponibilidad semanal', () => {
  test.beforeEach(async ({ page }) => {
    test.skip(!username || !password, 'Configura E2E_USERNAME y E2E_PASSWORD para probar la agenda.');

    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });
    await page.goto('/appointments');
    await expect(page.locator('body')).toContainText(/Citas|SuperAdmin/);
    if (await page.getByText(/SuperAdmin no tiene un tenant propio/).count()) {
      test.skip(true, 'La cuenta E2E es SuperAdmin y no dispone de agenda propia.');
    }
    await expect(page.getByRole('heading', { name: 'Disponibilidad semanal' })).toBeVisible({ timeout: 20_000 });
  });

  test.afterEach(async ({ page }) => {
    if (!createdInterval) return;
    await page.goto('/appointments');
    const rule = page.locator('.rule').filter({ hasText: createdInterval });
    if (await rule.count()) {
      page.once('dialog', dialog => dialog.accept());
      await rule.getByRole('button', { name: 'Eliminar' }).click();
      await expect(rule).toHaveCount(0, { timeout: 10_000 });
    }
    createdInterval = null;
  });

  test('añadir y eliminar un horario disponible desde el calendario', async ({ page }) => {
    const saturday = page.locator('.day-card').filter({ has: page.getByText('Sábado', { exact: true }) });
    await expect(saturday).toBeVisible();

    const occupied = new Set();
    const existingRules = await saturday.locator('.rule').allTextContents();
    for (const text of existingRules) {
      const match = text.match(/(\d{2}:\d{2})\s*[–-]\s*(\d{2}:\d{2})/);
      if (match) {
        const start = Number(match[1].slice(0, 2)) * 60 + Number(match[1].slice(3, 5));
        const end = Number(match[2].slice(0, 2)) * 60 + Number(match[2].slice(3, 5));
        for (let minute = start; minute < end; minute += 15) occupied.add(minute);
      }
    }

    let startMinute = -1;
    for (let minute = 22 * 60; minute <= 23 * 60 + 30; minute += 15) {
      if (!occupied.has(minute) && !occupied.has(minute + 15)) {
        startMinute = minute;
        break;
      }
    }
    test.skip(startMinute < 0, 'No hay un intervalo libre de 15 minutos entre las 22:00 y las 23:45 del sábado.');

    const format = minute => `${String(Math.floor(minute / 60)).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}`;
    const start = format(startMinute);
    const end = format(startMinute + 15);
    createdInterval = `${start}–${end}`;

    await page.getByLabel('Día', { exact: true }).selectOption({ label: 'Sábado' });
    await page.getByLabel('Hora de inicio').fill(start);
    await page.getByLabel('Hora de fin').fill(end);
    await page.getByLabel('Duración').selectOption({ label: '15 min' });
    await page.getByRole('button', { name: 'Añadir', exact: true }).click();

    await expect(page.getByText('Disponibilidad guardada.')).toBeVisible({ timeout: 15_000 });
    const createdRule = page.locator('.rule').filter({ hasText: createdInterval });
    await expect(createdRule).toBeVisible();

    page.once('dialog', dialog => dialog.accept());
    await createdRule.getByRole('button', { name: 'Eliminar' }).click();
    await expect(createdRule).toHaveCount(0, { timeout: 10_000 });
    createdInterval = null;
  });
});
