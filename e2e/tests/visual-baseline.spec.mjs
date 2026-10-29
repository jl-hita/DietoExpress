import { test, expect } from '@playwright/test';

// Estas pruebas introducen credenciales reales de GitHub Secrets: no guardar trazas DOM que puedan contenerlas.
test.use({ trace: 'off' });
import { readFile } from 'node:fs/promises';
import { resolve } from 'node:path';

// Baseline only: the workflow enables this file until one successful run has
// recorded the E2E_VISUAL_BASELINE_PASSED marker in roadmap issue #4.
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
  expect(consultationPdf).toContain('PLANIFICACIÓN DEPORTIVA');
  expect(consultationPdf).toContain('MACROCICLO · FASES Y OBJETIVOS');
  expect(consultationPdf).toContain('RUTINAS Y SESIONES');
  expect(consultationPdf).toContain('EVOLUCIÓN Y PROGRESO');
  expect(consultationPdf).toContain('Progresión');
  expect(consultationPdf).toContain('progressionRule');
  expect(consultationPdf).toContain('container.Page(page =>');
  expect(consultationPdf).toContain('training_mesocycles');
  expect(consultationPdf).toContain('training_sessions');
  expect(dietPdf).toContain('Miércoles');
  expect(dietPdf).toContain('Sábado');
  expect(dietPdf).not.toMatch(/MiÃ©rcoles|SÃ¡bado|calorÃ/);
  expect(email).toContain('meta charset=""utf-8""');
  expect(email).toContain('SubjectEncoding = Encoding.UTF8');
  expect(email).toContain('BodyEncoding = Encoding.UTF8');
  expect(email).toContain('background:#0d9488');
  expect(email).toContain('DietoExpress');
});

test('la plantilla HTML real de email se renderiza con acentos y teal corporativo', async ({ page }, testInfo) => {
  const emailSource = await readFile(resolve(process.cwd(), '..', 'Anguloso.Server/Logica/EmailServ.cs'), 'utf8');
  const templateStart = emailSource.indexOf('return $@"');
  const templateEnd = emailSource.indexOf('";', templateStart);
  expect(templateStart).toBeGreaterThanOrEqual(0);
  expect(templateEnd).toBeGreaterThan(templateStart);
  const sampleBody = '<h1>Prueba de DietoExpress</h1><p>Miércoles, sábado, calorías, proteínas, nutrición y piñón.</p>';
  const html = emailSource.slice(templateStart + 'return $@"'.length, templateEnd).replaceAll('""', '"').replace('{htmlBody}', sampleBody);
  expect(html).toContain('charset="utf-8"');
  expect(html).toContain('#0d9488');
  expect(html).toContain('Miércoles');
  expect(html).not.toMatch(/MiÃ©rcoles|calorÃ|nutriciÃ/);

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.setContent(html, { waitUntil: 'domcontentloaded' });
  await expect(page.locator('body')).toContainText('Miércoles, sábado, calorías, proteínas, nutrición y piñón.');
  const header = page.locator('table table tr').first().locator('td').first();
  await expect(header).toContainText('DietoExpress');
  const background = await header.evaluate(element => getComputedStyle(element).backgroundColor);
  expect(background).toBe('rgb(13, 148, 136)');
  await testInfo.attach('email-template-preview.html', { body: html, contentType: 'text/html; charset=utf-8' });
  await testInfo.attach('email-template-preview.png', { body: await page.screenshot({ fullPage: true }), contentType: 'image/png' });
});

test('PDF de dieta generado por la API: MIME, cabecera y evidencia para revisión visual', async ({ page }, testInfo) => {
  expect(process.env.E2E_USERNAME, 'Falta E2E_USERNAME para la prueba real de PDF').toBeTruthy();
  expect(process.env.E2E_PASSWORD, 'Falta E2E_PASSWORD para la prueba real de PDF').toBeTruthy();
  test.setTimeout(120_000);

  const clientName = `ZZZ E2E PDF ${new Date().toISOString().replace(/[:.]/g, '-')}`;
  const dietName = `ZZZ E2E PDF dieta ${new Date().toISOString().replace(/[:.]/g, '-')}`;
  let clientId = 0;
  let dietId = 0;
  let assignmentId = 0;

  try {
    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(process.env.E2E_USERNAME);
    await page.getByLabel('Contraseña').fill(process.env.E2E_PASSWORD);
    try {
      await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    } finally {
      // Limpiar incluso si el click falla; las trazas están desactivadas en estos tests.
      await page.locator('input[type="password"]').evaluateAll(inputs => inputs.forEach(input => { input.value = ''; }));
    }
    await expect(page).not.toHaveURL(new RegExp('/login(?:[/?#]|$)'), { timeout: 20_000 });

    await page.goto('/clients/nuevo');
    await page.getByRole('heading', { name: 'Nuevo Cliente' }).waitFor();
    await page.getByLabel('Nombre completo').fill(clientName);
    await page.getByLabel('Email').fill(`e2e-pdf-${Date.now()}@invalid.example`);
    await page.getByLabel('Notas').fill('Ficha ficticia temporal para verificar la generación PDF.');
    await page.getByRole('button', { name: 'Crear cliente' }).click();
    await expect(page).toHaveURL(new RegExp('/clients/[0-9]+(?:[/?#]|$)'), { timeout: 20_000 });
    clientId = Number(new URL(page.url()).pathname.match(/^\/clients\/(\d+)$/)?.[1]);
    expect(clientId).toBeGreaterThan(0);

    await page.goto('/diets/nuevo');
    await expect(page.getByRole('heading', { name: 'Nueva Dieta' })).toBeVisible();
    await page.getByPlaceholder('Escribe el nombre de la dieta...').fill(dietName);
    await page.getByRole('button', { name: /Crear Dieta/ }).click();
    await expect(page).toHaveURL(new RegExp('/diets(?:[/?#]|$)'), { timeout: 20_000 });

    const listResponse = await page.context().request.get(new URL('/api/dietas?search=' + encodeURIComponent(dietName) + '&page=1&pageSize=100', process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org').toString());
    expect(listResponse.ok()).toBeTruthy();
    const listing = await listResponse.json();
    const diets = Array.isArray(listing) ? listing : (listing.items || listing.Items || []);
    const createdDiet = diets.find(diet => (diet.name ?? diet.Name) === dietName);
    expect(createdDiet).toBeTruthy();
    dietId = Number(createdDiet.id ?? createdDiet.Id);

    const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
    const assignmentResponse = await page.context().request.post(new URL(`/api/clients/${clientId}/diets`, baseURL).toString(), {
      data: { dietId, startDate: new Date().toISOString(), notes: 'Generación PDF E2E con datos ficticios.' },
    });
    expect(assignmentResponse.ok(), 'La dieta ficticia debe poder asignarse al paciente E2E').toBeTruthy();
    const assignment = await assignmentResponse.json();
    assignmentId = Number(assignment.id ?? assignment.Id);
    expect(assignmentId).toBeGreaterThan(0);

    const pdfResponse = await page.context().request.get(new URL(`/api/clients/${clientId}/diets/${assignmentId}/pdf`, baseURL).toString());
    expect(pdfResponse.ok(), 'La API debe generar el PDF de dieta').toBeTruthy();
    expect(pdfResponse.headers()['content-type']).toContain('application/pdf');
    const pdf = await pdfResponse.body();
    expect(pdf.subarray(0, 5).toString('ascii')).toBe('%PDF-');
    expect(pdf.byteLength).toBeGreaterThan(1000);
    await testInfo.attach('diet-generated-e2e.pdf', { body: pdf, contentType: 'application/pdf' });
  } finally {
    const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
    const api = page.context().request;
    if (assignmentId && clientId) {
      await api.post(new URL(`/api/clients/${clientId}/diets/${assignmentId}/deactivate`, baseURL).toString()).catch(() => {});
      await api.delete(new URL(`/api/clients/${clientId}/diets/${assignmentId}`, baseURL).toString()).catch(() => {});
    }
    if (dietId) await api.delete(new URL(`/api/dietas/${dietId}`, baseURL).toString()).catch(() => {});
    if (clientId) await api.delete(new URL(`/api/clients/${clientId}`, baseURL).toString()).catch(() => {});
  }
});
