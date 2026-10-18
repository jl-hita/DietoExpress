import { test, expect } from '@playwright/test';

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;

test.describe('P1 — facturación sin iniciar cargos', () => {
  test('consultar la suscripción y cambiar el periodo sin iniciar checkout', async ({ page }) => {
    test.skip(!username || !password, 'Configura E2E_USERNAME y E2E_PASSWORD para probar la facturación.');

    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });

    await page.goto('/billing');
    await expect(page.getByRole('heading', { name: 'Suscripción' })).toBeVisible();
    await expect(page.locator('body')).toContainText(/Stripe/i);

    if (await page.locator('body').getByText('La cuenta SuperAdmin no tiene una suscripción de organización').count()) {
      test.skip(true, 'La cuenta E2E es SuperAdmin; la contratación corresponde a cuentas profesionales.');
    }

    const monthly = page.getByRole('button', { name: /Mensual/ });
    const yearly = page.getByRole('button', { name: /Anual/ });
    await expect(monthly).toBeVisible();
    await expect(yearly).toBeVisible();

    await yearly.click();
    await expect(yearly).toHaveClass(/selected/);
    await expect(page.locator('.equivalent')).toBeVisible();

    await monthly.click();
    await expect(monthly).toHaveClass(/selected/);

    // Intencionadamente no pulsa botones de compra ni crea sesiones de Stripe.
    await expect(page.getByRole('button', { name: /Continuar con el pago/ }).first()).toBeVisible();
  });
});
