import { test, expect } from '@playwright/test';

// Estas pruebas introducen credenciales reales de GitHub Secrets: no guardar trazas DOM que puedan contenerlas.
test.use({ trace: 'off' });

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
const patientName = `ZZZ E2E consulta guiada ${new Date().toISOString().replace(/[:.]/g, '-')}`;
let patientId = 0;

function localDateTime(daysAhead, hour = 21, minute = 15) {
  const date = new Date();
  date.setDate(date.getDate() + daysAhead);
  date.setHours(hour, minute, 0, 0);
  const pad = value => String(value).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

test.describe('P0 — recorrido completo de consulta guiada', () => {
  test.setTimeout(120_000);

  test.beforeEach(async ({ page }) => {
    test.skip(!username || !password, 'Configura E2E_USERNAME y E2E_PASSWORD para la consulta guiada completa.');
    await page.goto('/login');
    await page.getByLabel('Email o usuario').fill(username);
    await page.getByLabel('Contraseña').fill(password);
    await page.getByRole('button', { name: 'Iniciar sesión' }).click();
    // Limpiar el campo del DOM antes de que un fallo posterior genere capturas/contexto.
    await page.locator('input[type="password"]').evaluateAll(inputs => inputs.forEach(input => { input.value = ''; }));
    await expect(page).not.toHaveURL(/\/login(?:[/?#]|$)/, { timeout: 20_000 });
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
      expect([200, 204, 404].includes(response.status()), 'El paciente ficticio E2E debe archivarse al terminar').toBe(true);
      patientId = 0;
    }
  });

  test('crear paciente, agendar fuera del horario público, completar primera consulta y seguimiento', async ({ page }) => {
    await page.goto('/clients/nuevo');
    await expect(page.getByRole('heading', { name: 'Nuevo Cliente' })).toBeVisible();
    await page.getByLabel('Nombre completo').fill(patientName);
    await page.getByLabel('Email').fill(`e2e-${Date.now()}@invalid.example`);
    await page.getByLabel('Notas').fill('Paciente ficticio para la batería Playwright de consulta guiada.');
    await page.getByRole('button', { name: 'Crear cliente' }).click();
    await expect(page).toHaveURL(/\/clients\/\d+(?:[/?#]|$)/, { timeout: 20_000 });
    patientId = Number(page.url().match(/\/clients\/(\d+)/)?.[1]);
    expect(patientId).toBeGreaterThan(0);
    await expect(page.locator('body')).toContainText(patientName);

    const scheduleManually = async (daysAhead) => {
      await page.goto('/appointments');
      await expect(page.getByRole('heading', { name: 'Agendar cita manualmente' })).toBeVisible();
      await page.getByLabel('Buscar paciente').fill(patientName);
      await page.getByRole('button', { name: 'Buscar' }).click();
      await expect(page.getByLabel('Paciente para la cita')).toContainText(patientName, { timeout: 15_000 });
      await page.getByLabel('Paciente para la cita').selectOption({ label: patientName });
      await page.getByLabel('Fecha y hora de la cita').fill(localDateTime(daysAhead, 21, 15));
      await page.getByLabel('Duración de la cita').selectOption('30');
      await page.getByLabel('Modalidad de la cita').selectOption('in_person');
      await page.getByRole('button', { name: 'Crear cita confirmada' }).click();
      await expect(page.getByText('Cita creada y confirmada. La disponibilidad semanal solo afecta a las reservas públicas.')).toBeVisible({ timeout: 15_000 });
      const appointmentCard = page.locator('.appointment').filter({ hasText: patientName }).filter({ has: page.getByRole('button', { name: 'Iniciar consulta' }) }).first();
      await expect(appointmentCard).toBeVisible({ timeout: 15_000 });
      await appointmentCard.getByRole('button', { name: 'Iniciar consulta' }).click();
      await expect(page.getByText('CONSULTA GUIADA')).toBeVisible({ timeout: 20_000 });
      await expect(page.getByRole('heading', { name: patientName })).toBeVisible();
    };

    await scheduleManually(8);
    await page.getByRole('button', { name: /Objetivos/ }).first().click();
    await page.getByLabel('Objetivos y cambios acordados').fill('Mejorar regularidad de comidas y acordar un desayuno.');
    await page.getByLabel('Decisiones clínicas y acuerdos').fill('Se acuerda una revisión de hábitos en la próxima visita.');
    await page.getByLabel('Acciones y tareas acordadas').fill('Registrar desayunos durante siete días\nRevisar el registro en el seguimiento');
    await page.getByLabel('Observaciones clínicas').fill('Consulta inicial E2E; datos ficticios.');
    await page.locator('.stepper button').filter({ hasText: 'Cerrar' }).click();
    await expect(page.getByRole('button', { name: 'Cerrar consulta' })).toBeEnabled({ timeout: 15_000 });
    page.once('dialog', dialog => dialog.accept());
    await page.getByRole('button', { name: 'Cerrar consulta' }).click();
    await expect(page.getByText('Completada')).toBeVisible({ timeout: 15_000 });

    const tasksResponse = await page.context().request.get(new URL('/api/professional/tasks?status=open&limit=100', baseURL).toString());
    expect(tasksResponse.ok(), 'La API de tareas profesionales debe responder').toBeTruthy();
    const taskPayload = await tasksResponse.json();
    const tasks = Array.isArray(taskPayload) ? taskPayload : (taskPayload.items || taskPayload.Items || []);
    expect(tasks.some(task => (task.title ?? task.Title) === 'Registrar desayunos durante siete días')).toBe(true);
    expect(tasks.some(task => (task.title ?? task.Title) === 'Revisar el registro en el seguimiento')).toBe(true);

    await page.getByRole('button', { name: 'Agenda' }).click();
    await scheduleManually(11);
    await expect(page.getByText('Seguimiento', { exact: false })).toBeVisible();
    await page.getByRole('button', { name: /Objetivos/ }).first().click();
    await expect(page.getByRole('heading', { name: 'Cambios y objetivos' })).toBeVisible();
    await page.getByLabel('Objetivos y cambios acordados').fill('Revisar adherencia y dificultades del periodo.');
    await page.getByLabel('Acciones y tareas acordadas').fill('Mantener registro de desayunos una semana más');
    await page.getByRole('button', { name: /Cerrar/ }).last().click();
    page.once('dialog', dialog => dialog.accept());
    await page.getByRole('button', { name: 'Cerrar consulta' }).click();
    await expect(page.getByText('Completada')).toBeVisible({ timeout: 15_000 });
  });
});
