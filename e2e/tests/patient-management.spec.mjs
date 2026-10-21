import { test, expect } from '@playwright/test';

// Estas pruebas introducen credenciales reales de GitHub Secrets: no guardar trazas DOM que puedan contenerlas.
// Los objetivos de seguimiento se verifican aquí mediante la API autenticada y quedan cubiertos en la ejecución E2E semanal.
test.use({ trace: 'off' });

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
const patientName = `ZZZ E2E paciente UI ${new Date().toISOString().replace(/[:.]/g, '-')}`;
let patientId = 0;

test.describe('P1 — gestión de pacientes desde la interfaz', () => {
  test.beforeEach(async ({ page }) => {
    test.skip(!username || !password, 'Configura E2E_USERNAME y E2E_PASSWORD para ejecutar las pruebas de pacientes.');

    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    // No borrar la contraseña hasta que termine la autenticación: vaciarla inmediatamente
    // después del click puede competir con la serialización del formulario.
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });
    await page.locator('input[type="password"]').evaluateAll(inputs => inputs.forEach(input => { input.value = ''; }));
  });

  test.afterEach(async ({ page }) => {
    const api = page.context().request;
    if (!patientId) {
      const response = await api.get(new URL('/api/clients?search=' + encodeURIComponent(patientName) + '&page=1&pageSize=100', baseURL).toString());
      if (response.ok()) {
        const listing = await response.json();
        const patients = Array.isArray(listing) ? listing : (listing.items || listing.Items || []);
        const created = patients.find(patient => (patient.fullName ?? patient.FullName) === patientName);
        patientId = Number(created?.id ?? created?.Id ?? 0);
      }
    }

    if (patientId > 0) {
      const response = await api.delete(new URL('/api/clients/' + patientId, baseURL).toString());
      expect([200, 204, 404].includes(response.status()), 'La ficha ficticia E2E debe archivarse al terminar').toBe(true);
      patientId = 0;
    }
  });


  test('cancelar el archivado conserva el expediente; confirmarlo lo archiva', async ({ page }) => {
    await page.goto('/clients/nuevo');
    await page.getByRole('heading', { name: 'Nuevo Cliente' }).waitFor();
    await page.getByLabel('Nombre completo').fill(patientName);
    await page.getByLabel('Email').fill(`e2e-${Date.now()}@invalid.example`);
    await page.getByLabel('Notas').fill('Paciente ficticio para comprobar confirmaciones destructivas de Playwright.');
    await page.getByRole('button', { name: 'Crear cliente' }).click();
    await expect(page).toHaveURL(new RegExp('/clients/[0-9]+(?:[/?#]|$)'), { timeout: 20_000 });
    patientId = Number(new URL(page.url()).pathname.split('/').filter(Boolean).pop());
    expect(patientId).toBeGreaterThan(0);

    await page.goto('/clients');
    const search = page.getByPlaceholder('Buscar por nombre, email o teléfono...');
    await search.fill(patientName);
    const row = page.locator('tr').filter({ hasText: patientName });
    await expect(row).toBeVisible({ timeout: 15_000 });
    const archiveButton = page.getByRole('button', { name: `Archivar expediente de ${patientName}` });

    let deleteRequests = 0;
    page.on('request', request => {
      if (request.method() === 'DELETE' && new URL(request.url()).pathname.endsWith(`/api/clients/${patientId}`)) deleteRequests++;
    });
    page.once('dialog', dialog => dialog.dismiss());
    await archiveButton.click();
    await expect(row).toBeVisible();
    await page.waitForTimeout(300);
    expect(deleteRequests, 'Cancelar no debe enviar la petición de archivado').toBe(0);

    page.once('dialog', dialog => dialog.accept());
    const archiveResponse = page.waitForResponse(response =>
      response.request().method() === 'DELETE' &&
      new URL(response.url()).pathname.endsWith(`/api/clients/${patientId}`));
    await archiveButton.click();
    const response = await archiveResponse;
    expect([200, 204].includes(response.status()), 'El archivado confirmado debe completarse').toBe(true);
    await expect(row).toHaveCount(0, { timeout: 15_000 });
    patientId = 0;
  });

  test('crear un paciente, abrir su ficha y verificar los datos guardados', async ({ page }) => {
    await page.goto('/clients/nuevo');
    await expect(page.getByRole('heading', { name: 'Nuevo Cliente' })).toBeVisible();
    await expect(page.getByLabel('Nombre completo')).toBeVisible();

    await page.getByLabel('Nombre completo').fill(patientName);
    await page.getByLabel('Email').fill(`e2e-${Date.now()}@invalid.example`);
    await page.getByLabel('Notas').fill('Registro ficticio creado automáticamente por Playwright; archivar al finalizar.');
    await page.getByRole('button', { name: 'Crear cliente' }).click();

    await expect(page).toHaveURL(new RegExp('/clients/[0-9]+(?:[/?#]|$)'), { timeout: 20_000 });
    const match = page.url().match(/\/clients\/(\d+)/);
    expect(match, 'La aplicación debe navegar a la ficha creada').not.toBeNull();
    patientId = Number(match[1]);

    // El nombre se muestra en un input editable, no como texto del documento.
    await expect(page.getByLabel('Nombre completo')).toHaveValue(patientName);
    const apiResponse = await page.context().request.get(new URL('/api/clients/' + patientId, baseURL).toString());
    expect(apiResponse.ok(), 'La ficha creada debe persistir en la API').toBeTruthy();
    const patient = await apiResponse.json();
    expect(patient.fullName ?? patient.FullName).toBe(patientName);
    expect(patient.notes ?? patient.Notes).toContain('Playwright');
  });

  test('crear, consultar y completar un objetivo de adherencia del paciente', async ({ page }) => {
    await page.goto('/clients/nuevo');
    await page.getByRole('heading', { name: 'Nuevo Cliente' }).waitFor();
    await page.getByLabel('Nombre completo').fill(patientName);
    await page.getByLabel('Email').fill(`e2e-${Date.now()}@invalid.example`);
    await page.getByLabel('Notas').fill('Paciente ficticio para prueba E2E de objetivos de adherencia.');
    await page.getByRole('button', { name: 'Crear cliente' }).click();
    await expect(page).toHaveURL(new RegExp('/clients/[0-9]+(?:[/?#]|$)'), { timeout: 20_000 });
    patientId = Number(new URL(page.url()).pathname.split('/').filter(Boolean).pop());
    expect(patientId).toBeGreaterThan(0);

    const api = page.context().request;
    const create = await api.post(new URL('/api/professional/followup-goals', baseURL).toString(), {
      data: {
        clientId: patientId,
        title: 'Adherencia semanal de prueba',
        metricKey: 'adherence',
        targetValue: 80,
        phase: 'building',
        startDate: new Date().toISOString().slice(0, 10),
        notes: 'Objetivo ficticio; se elimina al archivar el paciente.'
      }
    });
    expect(create.status(), 'El profesional asignado debe poder crear el objetivo').toBe(201);
    const created = await create.json();
    expect(created.id).toBeGreaterThan(0);

    const list = await api.get(new URL(`/api/professional/followup-goals/${patientId}`, baseURL).toString());
    expect(list.ok()).toBeTruthy();
    const goals = await list.json();
    expect(goals).toHaveLength(1);
    expect(goals[0]).toMatchObject({ id: created.id, title: 'Adherencia semanal de prueba', metricKey: 'adherence', targetValue: 80, unit: '%', phase: 'building', status: 'active' });

    const update = await api.put(new URL(`/api/professional/followup-goals/${created.id}/status`, baseURL).toString(), { data: { status: 'completed' } });
    expect(update.status()).toBe(204);
    const completed = await api.get(new URL(`/api/professional/followup-goals/${patientId}`, baseURL).toString());
    expect((await completed.json())[0].status).toBe('completed');
  });
});
