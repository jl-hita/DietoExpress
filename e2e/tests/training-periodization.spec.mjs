import { test, expect } from '@playwright/test';

test.use({ trace: 'off' });

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
const patientName = `ZZZ E2E periodización ${new Date().toISOString().replace(/[:.]/g, '-')}`;
let patientId = 0;

function datePlusDays(days) {
  const date = new Date();
  date.setDate(date.getDate() + days);
  const pad = value => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

function apiUrl(path) {
  return new URL(path, baseURL).toString();
}

test.describe('MVP de periodización deportiva', () => {
  test.setTimeout(120_000);

  test.beforeEach(async ({ page }) => {
    test.skip(!username || !password, 'Configura E2E_USERNAME y E2E_PASSWORD para probar la periodización.');
    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });
    await page.locator('input[type="password"]').evaluateAll(inputs => inputs.forEach(input => { input.value = ''; }));
  });

  test.afterEach(async ({ page }) => {
    const api = page.context().request;
    if (!patientId) {
      const response = await api.get(apiUrl('/api/clients?search=' + encodeURIComponent(patientName) + '&page=1&pageSize=100'));
      if (response.ok()) {
        const listing = await response.json();
        const patients = Array.isArray(listing) ? listing : (listing.items || listing.Items || []);
        const created = patients.find(patient => (patient.fullName ?? patient.FullName) === patientName);
        patientId = Number(created?.id ?? created?.Id ?? 0);
      }
    }
    if (patientId > 0) {
      const response = await api.delete(apiUrl('/api/clients/' + patientId));
      expect([200, 204, 404].includes(response.status()), 'El paciente ficticio E2E debe archivarse al terminar').toBe(true);
      patientId = 0;
    }
  });

  test('edita, reordena y elimina ciclos sin perder hijos por accidente', async ({ page }) => {
    await page.goto('/clients/nuevo');
    await expect(page.getByRole('heading', { name: 'Nuevo Cliente' })).toBeVisible();
    await page.getByLabel('Nombre completo').fill(patientName);
    await page.getByLabel('Email').fill(`e2e-training-${Date.now()}@invalid.example`);
    await page.getByLabel('Notas').fill('Paciente ficticio para pruebas E2E de periodización deportiva.');
    await page.getByRole('button', { name: 'Crear cliente' }).click();
    await expect(page).toHaveURL(/\/clients\/\d+(?:[/?#]|$)/, { timeout: 20_000 });
    patientId = Number(page.url().match(/\/clients\/(\d+)/)?.[1]);
    expect(patientId).toBeGreaterThan(0);

    const api = page.context().request;
    const profileResponse = await api.put(apiUrl(`/api/training/clients/${patientId}/profile`), {
      data: { enabled: true, primaryGoal: 'general_fitness', experienceLevel: 'beginner', managementMode: 'self', notes: 'Perfil E2E' }
    });
    expect(profileResponse.ok(), 'Debe poder activarse el perfil deportivo').toBeTruthy();

    const start = datePlusDays(30);
    const end = datePlusDays(90);
    const planResponse = await api.post(apiUrl(`/api/training/clients/${patientId}/plans`), {
      data: { name: 'Plan E2E ciclos', periodizationModel: 'linear', startDate: start, endDate: end, notes: 'Plan ficticio E2E' }
    });
    expect(planResponse.ok(), 'Debe crearse el plan deportivo').toBeTruthy();
    const plan = await planResponse.json();

    const meso1Response = await api.post(apiUrl(`/api/training/plans/${plan.id}/mesocycles`), {
      data: { name: 'Mesociclo E2E A', sequenceNumber: 1, startDate: start, endDate: datePlusDays(13), objective: 'Base', notes: null }
    });
    expect(meso1Response.ok(), 'Debe crearse el primer mesociclo').toBeTruthy();
    const meso1 = await meso1Response.json();

    const meso2Response = await api.post(apiUrl(`/api/training/plans/${plan.id}/mesocycles`), {
      data: { name: 'Mesociclo E2E B', sequenceNumber: 2, startDate: datePlusDays(44), endDate: datePlusDays(57), objective: 'Progresión', notes: null }
    });
    expect(meso2Response.ok(), 'Debe crearse el segundo mesociclo sin solapamiento').toBeTruthy();

    const micro1Response = await api.post(apiUrl(`/api/training/mesocycles/${meso1.id}/microcycles`), {
      data: { name: 'Microciclo E2E 1', sequenceNumber: 1, startDate: start, endDate: datePlusDays(6), focus: 'Adaptación', notes: null }
    });
    expect(micro1Response.ok(), 'Debe crearse el primer microciclo').toBeTruthy();
    const micro2Response = await api.post(apiUrl(`/api/training/mesocycles/${meso1.id}/microcycles`), {
      data: { name: 'Microciclo E2E 2', sequenceNumber: 2, startDate: datePlusDays(7), endDate: datePlusDays(13), focus: 'Carga', notes: null }
    });
    expect(micro2Response.ok(), 'Debe crearse el segundo microciclo sin solapamiento').toBeTruthy();

    await page.goto(`/clients/${patientId}/training`);
    await expect(page.getByRole('heading', { name: 'Planes de entrenamiento' })).toBeVisible({ timeout: 15_000 });
    await expect(page.locator('.plan-card > .cycle-section > .mesocycle-card').filter({ hasText: 'Mesociclo E2E A' })).toBeVisible();
    await expect(page.locator('li').filter({ hasText: 'Microciclo E2E 1' })).toBeVisible();
    await expect(page.locator('.week-grid')).toBeVisible();
    await expect(page.locator('.week-summary')).toContainText('sesiones esta semana');
    const sessionTitle = 'Sesión E2E fuerza visual';
    await page.locator(`input[name="sessionTitle${plan.id}"]`).fill(sessionTitle);
    await page.locator(`input[name="sessionDate${plan.id}"]`).fill(start);
    await page.locator(`select[name="sessionType${plan.id}"]`).selectOption('strength');
    await page.locator(`input[name="sessionDuration${plan.id}"]`).fill('60');
    await page.locator(`input[name="sessionIntensity${plan.id}"]`).fill('RPE 7 - técnica controlada');
    await page.locator(`textarea[name="sessionInstructions${plan.id}"]`).fill('Calentamiento, tres bloques principales y vuelta a la calma.');
    await page.getByRole('button', { name: /Añadir sesión/ }).click();
    await expect(page.locator('.week-session').filter({ hasText: sessionTitle })).toBeVisible();
    await expect(page.locator('.week-summary')).toContainText('60 min');
    await page.getByRole('button', { name: /Lista/ }).click();
    let sessionCard = page.locator('.session-card').filter({ hasText: sessionTitle });
    await expect(sessionCard).toBeVisible();
    await expect(sessionCard).toContainText('RPE 7 - técnica controlada');
    await expect(sessionCard).toContainText('Calentamiento, tres bloques principales y vuelta a la calma.');

    await sessionCard.getByRole('button', { name: 'Editar' }).click();
    await sessionCard.locator('input[name^="editSessionTitle"]').fill('Sesión E2E fuerza editada');
    await sessionCard.locator('input[name^="editSessionIntensity"]').fill('RPE 8 - progresión');
    await sessionCard.locator('textarea[name^="editSessionInstructions"]').fill('Versión actualizada de las instrucciones.');
    await sessionCard.getByRole('button', { name: 'Guardar cambios' }).click();
    sessionCard = page.locator('.session-card').filter({ hasText: 'Sesión E2E fuerza editada' });
    await expect(sessionCard).toBeVisible();
    await expect(sessionCard).toContainText('RPE 8 - progresión');
    await expect(sessionCard).toContainText('Versión actualizada de las instrucciones.');

    const mesocycles = page.locator('.plan-card > .cycle-section > .mesocycle-card');
    const firstMeso = mesocycles.first();
    await firstMeso.getByRole('button', { name: 'Editar' }).click();
    await page.locator(`input[name="editMesoName${meso1.id}"]`).fill('Mesociclo E2E A editado');
    await firstMeso.getByRole('button', { name: 'Guardar cambios' }).click();
    await expect(page.locator('.plan-card > .cycle-section > .mesocycle-card').filter({ hasText: 'Mesociclo E2E A editado' })).toBeVisible();

    const currentFirst = page.locator('.plan-card > .cycle-section > .mesocycle-card').first();
    await currentFirst.getByRole('button', { name: 'Bajar mesociclo' }).click();
    await expect(page.locator('.plan-card > .cycle-section > .mesocycle-card').first()).toContainText('Mesociclo E2E B');

    const editedMeso = page.locator('.plan-card > .cycle-section > .mesocycle-card').filter({ hasText: 'Mesociclo E2E A editado' }).first();
    page.once('dialog', dialog => dialog.accept());
    await editedMeso.getByRole('button', { name: 'Eliminar' }).click();
    await expect(page.getByRole('alert')).toContainText('No se puede borrar un mesociclo que contiene microciclos');

    for (const microName of ['Microciclo E2E 1', 'Microciclo E2E 2']) {
      const microRow = page.locator('li').filter({ hasText: microName }).first();
      page.once('dialog', dialog => dialog.accept());
      await microRow.getByRole('button', { name: 'Eliminar' }).click();
      await expect(page.locator('li').filter({ hasText: microName })).toHaveCount(0);
    }

    const mesoToDelete = page.locator('.plan-card > .cycle-section > .mesocycle-card').filter({ hasText: 'Mesociclo E2E A editado' }).first();
    page.once('dialog', dialog => dialog.accept());
    await mesoToDelete.getByRole('button', { name: 'Eliminar' }).click();
    await expect(page.locator('.plan-card > .cycle-section > .mesocycle-card').filter({ hasText: 'Mesociclo E2E A editado' })).toHaveCount(0);

    const finalMeso = page.locator('.plan-card > .cycle-section > .mesocycle-card').filter({ hasText: 'Mesociclo E2E B' }).first();
    page.once('dialog', dialog => dialog.accept());
    await finalMeso.getByRole('button', { name: 'Eliminar' }).click();
    await expect(page.locator('.plan-card > .cycle-section > .mesocycle-card').filter({ hasText: 'Mesociclo E2E B' })).toHaveCount(0);
  });
});
