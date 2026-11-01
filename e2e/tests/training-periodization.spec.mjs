import { test, expect } from '@playwright/test';

test.use({ trace: 'off' });

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
const patientName = `ZZZ E2E periodización ${new Date().toISOString().replace(/[:.]/g, '-')}`;
let patientId = 0;
let sessionTemplateId = 0;

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
    if (sessionTemplateId > 0) {
      const response = await api.delete(apiUrl('/api/training/session-templates/' + sessionTemplateId));
      expect([200, 204, 404].includes(response.status()), 'La plantilla de sesión E2E debe poder eliminarse al terminar').toBe(true);
      sessionTemplateId = 0;
    }
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

    const sessionResponse = await api.post(apiUrl(`/api/training/plans/${plan.id}/sessions`), {
      data: { scheduledDate: start, title: 'Sesión plantilla E2E', sessionType: 'strength', durationMinutes: 45,
        intensityTarget: 'Moderada', instructions: 'Sesión reutilizable', exercisePrescription: 'Sentadilla · 3 × 8' }
    });
    expect(sessionResponse.ok(), 'Debe crearse una sesión que pueda guardarse en una plantilla').toBeTruthy();

    const saveSessionTemplateResponse = await api.post(apiUrl('/api/training/session-templates'), {
      data: { name: 'Plantilla sesión gimnasio E2E', sessionType: 'strength', durationMinutes: 45,
        intensityTarget: 'RPE 7', instructions: 'Calentamiento y bloques principales', exercisePrescription: 'Sentadilla · 3 × 8 · descanso 2 min' }
    });
    expect(saveSessionTemplateResponse.ok(), 'Debe guardarse una plantilla de sesión independiente').toBeTruthy();
    const savedSessionTemplate = await saveSessionTemplateResponse.json();
    sessionTemplateId = Number(savedSessionTemplate.id);
    expect(sessionTemplateId).toBeGreaterThan(0);
    const sessionTemplatesResponse = await api.get(apiUrl('/api/training/session-templates'));
    expect(sessionTemplatesResponse.ok(), 'Debe consultarse la biblioteca de sesiones').toBeTruthy();
    expect((await sessionTemplatesResponse.json()).some(item => item.id === sessionTemplateId), 'La plantilla de sesión debe aparecer en la biblioteca').toBe(true);

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

    const saveTemplateResponse = await api.post(apiUrl(`/api/training/plans/${plan.id}/templates`), {
      data: { name: 'Plantilla E2E reutilizable' }
    });
    expect(saveTemplateResponse.ok(), 'Debe guardarse un plan como plantilla reutilizable').toBeTruthy();
    const templatesResponse = await api.get(apiUrl('/api/training/templates'));
    expect(templatesResponse.ok(), 'Debe poder consultarse la biblioteca de plantillas').toBeTruthy();
    const templates = await templatesResponse.json();
    const template = templates.find(item => item.name === 'Plantilla E2E reutilizable');
    expect(template, 'La plantilla guardada debe aparecer en la biblioteca').toBeTruthy();
    expect(template.sessionCount).toBe(1);

    await page.goto(`/clients/${patientId}/training`);
    await expect(page.getByRole('heading', { name: 'Planes de entrenamiento' })).toBeVisible({ timeout: 15_000 });
    await expect(page.getByLabel('Nombre del plan')).toBeHidden();
    await page.getByRole('button', { name: /Nueva planificación/ }).click();
    await expect(page.getByLabel('Nombre del plan')).toBeVisible();
    await page.getByRole('button', { name: 'Cancelar' }).click();
    await expect(page.getByLabel('Nombre del plan')).toBeHidden();
    await expect(page.getByRole('navigation', { name: 'Secciones de planificación' })).toBeVisible();
    await expect(page.locator('.training-assignment-overview')).toContainText('Planificación asignada');
    await page.getByRole('button', { name: /Mesociclos/ }).click();
    await expect(page.locator('.plan-card > .cycle-section > .mesocycle-card').filter({ hasText: 'Mesociclo E2E A' })).toBeVisible();
    await expect(page.locator('li').filter({ hasText: 'Microciclo E2E 1' })).toBeVisible();
    await page.getByRole('button', { name: /Rutinas/ }).click();
    await expect(page.locator('.week-grid')).toBeVisible();
    await expect(page.locator('.week-summary')).toContainText('sesiones esta semana');
    await expect(page.getByRole('heading', { name: 'Construye la sesión' })).toBeVisible();
    const savedTemplateSelect = page.locator(`select[name="savedSessionTemplate${plan.id}"]`);
    await expect(savedTemplateSelect.locator('option')).toContainText('Plantilla sesión gimnasio E2E');
    await savedTemplateSelect.selectOption(String(sessionTemplateId));
    await page.getByRole('button', { name: 'Aplicar', exact: true }).click();
    await expect(page.locator(`input[name="sessionTitle${plan.id}"]`)).toHaveValue('Plantilla sesión gimnasio E2E');
    await expect(page.locator(`textarea[name="sessionExercises${plan.id}"]`)).toContainText('Sentadilla');
    await expect(page.locator('.exercise-result')).not.toHaveCount(0);
    await page.locator(`input[name="exerciseSearch${plan.id}"]`).fill('sentadilla');
    await expect(page.locator('.exercise-result').filter({ hasText: 'Sentadilla' })).toBeVisible();
    await page.locator('.exercise-result').filter({ has: page.getByText('Sentadilla', { exact: true }) }).getByRole('button', { name: /Añadir/ }).click();
    await expect(page.locator(`textarea[name="sessionExercises${plan.id}"]`)).toContainText('Sentadilla');
    await page.locator(`input[name="exerciseSearch${plan.id}"]`).fill('');
    await page.locator(`select[name="generatorGoal${plan.id}"]`).selectOption('strength');
    await page.locator(`select[name="generatorLevel${plan.id}"]`).selectOption('beginner');
    await page.locator(`select[name="generatorEquipment${plan.id}"]`).selectOption('mixed');
    await page.getByRole('button', { name: '✦ Proponer sesión' }).click();
    await expect(page.locator(`textarea[name="sessionExercises${plan.id}"]`)).not.toBeEmpty();
    const sessionTitle = 'Sesión E2E fuerza visual';
    await page.locator(`input[name="sessionTitle${plan.id}"]`).fill(sessionTitle);
    await page.locator(`input[name="sessionDate${plan.id}"]`).fill(start);
    await page.locator(`select[name="sessionType${plan.id}"]`).selectOption('strength');
    await page.locator(`input[name="sessionDuration${plan.id}"]`).fill('60');
    await page.locator(`input[name="sessionIntensity${plan.id}"]`).fill('RPE 7 - técnica controlada');
    await page.locator(`textarea[name="sessionInstructions${plan.id}"]`).fill('Calentamiento, tres bloques principales y vuelta a la calma.');
    await page.locator(`textarea[name="sessionExercises${plan.id}"]`).fill('Sentadilla · 3 × 8 · descanso 2 min · RPE 7\nPeso muerto rumano · 3 × 10 · descanso 90 s');
    await page.getByRole('button', { name: /Añadir sesión/ }).click();
    await expect(page.locator('.week-session').filter({ hasText: sessionTitle })).toBeVisible();
    await expect(page.locator('.week-summary')).toContainText('60 min');
    const csvDownload = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Exportar CSV' }).click();
    expect((await csvDownload).suggestedFilename()).toMatch(/dietoexpress-entrenamiento-.*\.csv/);
    const trendResponse = await api.get(apiUrl(`/api/training/plans/${plan.id}/load-trend`));
    expect(trendResponse.ok(), 'Debe consultar la tendencia de carga del plan').toBeTruthy();
    expect(Array.isArray(await trendResponse.json())).toBe(true);
    await page.getByRole('button', { name: /Lista/ }).click();
    let sessionCard = page.locator('.session-card').filter({ hasText: sessionTitle });
    await expect(sessionCard).toBeVisible();
    await expect(sessionCard).toContainText('RPE 7 - técnica controlada');
    await expect(sessionCard).toContainText('Calentamiento, tres bloques principales y vuelta a la calma.');
    await expect(sessionCard).toContainText('Sentadilla · 3 × 8 · descanso 2 min · RPE 7');

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

    // Exercise the new server-side mesocycle flow end-to-end after the manual cycles are removed.
    await page.locator(`input[name="autoMesoName${plan.id}"]`).fill('Mesociclo E2E generado');
    await page.locator(`select[name="autoGoal${plan.id}"]`).selectOption('1');
    await page.locator(`select[name="autoExperience${plan.id}"]`).selectOption('0');
    await page.locator(`input[name="autoWeeks${plan.id}"]`).fill('2');
    await page.locator(`input[name="autoDays${plan.id}"]`).fill('1,4');
    await page.locator(`input[name="autoMinutes${plan.id}"]`).fill('60');
    await page.locator(`input[name="autoEquipment${plan.id}"]`).fill('bodyweight, dumbbell, barbell, cable, machine, kettlebell, resistance band');
    await page.getByRole('button', { name: 'Generar vista previa' }).click();
    await expect(page.locator('.generated-preview')).toBeVisible({ timeout: 30_000 });
    await expect(page.locator('.quality-report')).toBeVisible();
    await expect(page.locator('.generated-session')).toHaveCount(4);
    await page.getByRole('button', { name: 'Confirmar y guardar mesociclo' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Mesociclo guardado' })).toBeVisible({ timeout: 20_000 });
    const generatedSessionsResponse = await api.get(apiUrl(`/api/training/plans/${plan.id}/sessions`));
    expect(generatedSessionsResponse.ok(), 'Debe poder consultarse el mesociclo guardado').toBeTruthy();
    const generatedSessions = await generatedSessionsResponse.json();
    const structuredSessions = generatedSessions.filter(session => session.exercisePrescriptionData?.exercises?.length > 0);
    expect(structuredSessions, 'Las sesiones generadas deben persistir su prescripción estructurada').toHaveLength(4);

    const copiedPlanResponse = await api.post(apiUrl(`/api/training/clients/${patientId}/plans/from-template/${template.id}`), {
      data: { name: 'Plan creado desde plantilla E2E', startDate: datePlusDays(120) }
    });
    expect(copiedPlanResponse.ok(), 'Debe crearse un plan independiente desde la plantilla').toBeTruthy();
    const copiedPlan = await copiedPlanResponse.json();
    expect(copiedPlan.copiedSessions).toBe(1);
    expect(copiedPlan.copiedMesocycles).toBe(2);
    const copiedSessionsResponse = await api.get(apiUrl(`/api/training/plans/${copiedPlan.id}/sessions`));
    expect(copiedSessionsResponse.ok()).toBeTruthy();
    expect(await copiedSessionsResponse.json()).toHaveLength(1);

    await page.goto(`/clients/${patientId}/training`);
    const originalPlanCard = page.locator('.plan-card').filter({ hasText: 'Plan E2E ciclos' });
    await expect(originalPlanCard).toBeVisible();
    await originalPlanCard.locator('.plan-card-actions').getByRole('button', { name: 'Editar', exact: true }).click();
    await page.locator(`input[name="editPlanName${plan.id}"]`).fill('Plan E2E ciclos editado');
    await originalPlanCard.getByRole('button', { name: 'Guardar cambios' }).click();
    const editedPlanCard = page.locator('.plan-card').filter({ hasText: 'Plan E2E ciclos editado' });
    await expect(editedPlanCard).toBeVisible();

    await page.getByRole('button', { name: 'Rutina sencilla' }).click();
    await page.getByLabel('Nombre de la rutina').fill('Rutina sencilla desde gestor E2E');
    await page.getByLabel('Ejercicios').fill('Sentadilla · 3 × 10 · descanso 90 s');
    const quickRoutinePromise = page.waitForResponse(response =>
      response.request().method() === 'POST' && new URL(response.url()).pathname.endsWith('/quick-routine'));
    await page.getByRole('button', { name: 'Crear y asignar rutina' }).click();
    const quickRoutineResponse = await quickRoutinePromise;
    expect(quickRoutineResponse.status()).toBe(201);
    const quickRoutineCard = page.locator('.plan-card').filter({ hasText: 'Rutina sencilla desde gestor E2E' });
    await expect(quickRoutineCard).toBeVisible();

    page.once('dialog', dialog => dialog.accept());
    await quickRoutineCard.locator('.plan-card-actions').getByRole('button', { name: 'Eliminar', exact: true }).click();
    await expect(page.locator('.plan-card').filter({ hasText: 'Rutina sencilla desde gestor E2E' })).toHaveCount(0);

    page.once('dialog', dialog => dialog.accept());
    await editedPlanCard.locator('.plan-card-actions').getByRole('button', { name: 'Eliminar', exact: true }).click();
    await expect(page.locator('.plan-card').filter({ hasText: 'Plan E2E ciclos editado' })).toHaveCount(0);
  });
});
