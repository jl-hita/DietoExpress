import { test, expect } from '@playwright/test';

// Estas pruebas introducen credenciales reales de GitHub Secrets: no guardar trazas DOM que puedan contenerlas.
test.use({ trace: 'off' });

const professionalUsername = process.env.E2E_USERNAME;
const professionalPassword = process.env.E2E_PASSWORD;
const adminUsername = process.env.E2E_SUPERADMIN_USERNAME;
const adminPassword = process.env.E2E_SUPERADMIN_PASSWORD;

const suffix = Date.now().toString(36).toUpperCase();
const coupon25 = `E2E25-${suffix}`;
const coupon100 = `E2E100-${suffix}`;

async function login(page, username, password) {
  await page.goto('/login');
  await page.getByLabel('Email o usuario').fill(username);
  await page.getByLabel('Contraseña').fill(password);
  try {
      await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    } finally {
      // Limpiar incluso si el click falla; las trazas están desactivadas en estos tests.
      await page.locator('input[type="password"]').evaluateAll(inputs => inputs.forEach(input => { input.value = ''; }));
    }
  await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });
}

test.describe.serial('P0 — cupones promocionales (sin completar Checkout)', () => {
  test('SuperAdmin crea cupones de 25 % y 100 % para nutri_full mensual', async ({ page }) => {
    test.skip(!adminUsername || !adminPassword, 'Configura E2E_SUPERADMIN_USERNAME y E2E_SUPERADMIN_PASSWORD.');
    await login(page, adminUsername, adminPassword);
    await page.goto('/admin/coupons');
    await expect(page.getByRole('heading', { name: /cupones/i }).first()).toBeVisible();

    for (const [code, discount] of [[coupon25, 25], [coupon100, 100]]) {
      await page.getByLabel('Código', { exact: true }).fill(code);
      await page.getByLabel(/Descuento/).first().fill(String(discount));
      await page.locator('.coupon-admin .panel').first().locator('select').nth(0).selectOption('nutri_full');
      await page.locator('.coupon-admin .panel').first().locator('select').nth(1).selectOption('monthly');
      await page.getByRole('button', { name: 'Crear código' }).click();
      await expect(page.getByText('Cupón creado.', { exact: true })).toBeVisible({ timeout: 10_000 });
      await expect(page.getByText(code, { exact: true })).toBeVisible();
    }
  });

  test('la cuenta profesional obtiene la previsualización correcta y no se redirige a Stripe', async ({ page }) => {
    test.skip(!professionalUsername || !professionalPassword, 'Configura E2E_USERNAME y E2E_PASSWORD.');
    test.skip(!adminUsername || !adminPassword, 'Esta prueba requiere los secretos de SuperAdmin para crear cupones nuevos.');

    await login(page, professionalUsername, professionalPassword);
    await page.goto('/billing');
    const couponInput = page.locator('#billing-coupon');
    await expect(couponInput).toBeVisible();

    const terms = page.getByRole('checkbox', { name: /Acepto las condiciones de contratación/ });
    test.skip(await terms.count() === 0, 'No hay condiciones de contratación visibles; se evita iniciar el flujo de checkout.');
    await terms.check();

    for (const [code, discount] of [[coupon25, 25], [coupon100, 100]]) {
      await couponInput.fill(code);
      let dialogMessage = '';
      page.once('dialog', async dialog => {
        dialogMessage = dialog.message();
        await dialog.dismiss();
      });
      await page.getByRole('button', { name: /Continuar con el pago/ }).first().click();
      await expect.poll(() => dialogMessage, { timeout: 15_000 }).toContain(`descuento del ${discount} %`);
      await expect.poll(() => dialogMessage, { timeout: 15_000 }).toContain('Importe inicial:');
      await expect(page).toHaveURL(/\/billing(?:[?#]|$)/);
      await expect(couponInput).toHaveValue(code);
    }
  });
});
