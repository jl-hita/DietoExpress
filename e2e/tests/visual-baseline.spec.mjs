import { test, expect } from '@playwright/test';
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// Baseline only: the workflow enables this file until one successful run has
// uploaded the playwright-visual-baseline-passed marker artifact.
test.skip(process.env.E2E_RUN_VISUAL_BASELINE !== 'true',
  'La revisión visual/documental inicial ya pasó; se repite solo tras fallo o cambio relevante.');

const viewports = [
  { name: 'escritorio', width: 1440, height: 1000 },
  { name: 'móvil', width: 390, height: 844 },
];

for (const viewport of viewports) {
  test(`directorio: skeletons accesibles y responsive en ${viewport.name}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.route('**/api/directory/professionals**', async route => {
      await new Promise(resolveDelay => setTimeout(resolveDelay, 1400));
      await route.fulfill({ status: 200, contentType: 'application/json; charset=utf-8', body: '[]' });
    });

    const response = await page.goto('/nutricionistas');
    expect(response?.status()).toBeLessThan(400);
    const loading = page.getByRole('status', { name: 'Cargando profesionales' });
    await expect(loading).toBeVisible();
    await expect(loading.locator('.profile-card-skeleton')).toHaveCount(3);
    await expect(page.getByRole('button', { name: /Buscar profesionales/ })).toBeDisabled();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth),
      'El directorio no debe producir desplazamiento horizontal').toBe(true);
    await testInfo.attach(`directorio-skeletons-${viewport.name}.png`, {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });
    await expect(page.locator('.empty-state')).toBeVisible({ timeout: 10_000 });
  });

  test(`directorio: estado vacío legible en ${viewport.name}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.route('**/api/directory/professionals**', route =>
      route.fulfill({ status: 200, contentType: 'application/json; charset=utf-8', body: '[]' }));

    const response = await page.goto('/nutricionistas');
    expect(response?.status()).toBeLessThan(400);
    const empty = page.locator('.empty-state');
    await expect(empty).toBeVisible();
    await expect(empty).toContainText('No hemos encontrado profesionales');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await testInfo.attach(`directorio-vacio-${viewport.name}.png`, {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });
  });

  test(`directorio: error de API claro y accesible en ${viewport.name}`, async ({ page }, testInfo) => {
    await page.setViewportSize({ width: viewport.width, height: viewport.height });
    await page.route('**/api/directory/professionals**', route =>
      route.fulfill({ status: 503, contentType: 'application/json; charset=utf-8', body: JSON.stringify({ message: 'Servicio temporalmente no disponible' }) }));

    const response = await page.goto('/nutricionistas');
    expect(response?.status()).toBeLessThan(400);
    const error = page.getByRole('alert');
    await expect(error).toBeVisible();
    await expect(error).toContainText('No se ha podido cargar el directorio');
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
    await testInfo.attach(`directorio-error-${viewport.name}.png`, {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });
  });
}

test('contrato de marca documental: PDFs y emails conservan UTF-8, acentos y teal corporativo', async () => {
  const readRepoFile = relativePath => readFile(resolve(process.cwd(), '..', relativePath), 'utf8');
  const [dietPdf, consultationPdf, email] = await Promise.all([
    readRepoFile('Anguloso.Server/Logica/DietPdfService.cs'),
    readRepoFile('Anguloso.Server/Logica/ConsultationPdfService.cs'),
    readRepoFile('Anguloso.Server/Logica/EmailServ.cs'),
  ]);

  expect(dietPdf).toContain('#0d9488');
  expect(consultationPdf).toContain('#0d9488');
  expect(dietPdf).toContain('Miércoles');
  expect(dietPdf).toContain('Sábado');
  expect(dietPdf).not.toMatch(/MiÃ©rcoles|SÃ¡bado|calorÃ/);
  expect(email).toContain('meta charset=""utf-8""');
  expect(email).toContain('SubjectEncoding = Encoding.UTF8');
  expect(email).toContain('BodyEncoding = Encoding.UTF8');
  expect(email).toContain('background:#0d9488');
  expect(email).toContain('DietoExpress');
});
