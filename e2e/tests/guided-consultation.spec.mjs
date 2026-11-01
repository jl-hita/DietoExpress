import { test, expect } from '@playwright/test';

// Estas pruebas introducen credenciales reales de GitHub Secrets: no guardar trazas DOM que puedan contenerlas.
test.use({ trace: 'off' });

const username = process.env.E2E_USERNAME;
const password = process.env.E2E_PASSWORD;
const baseURL = process.env.E2E_BASE_URL || 'https://jlhitap.duckdns.org';
const patientName = `ZZZ E2E consulta guiada ${new Date().toISOString().replace(/[:.]/g, '-')}`;
let patientId = 0;

function localDateTime(daysAhead, hour = 21, minute = 0) {
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
    // La ficha presenta el nombre en un control de formulario editable.
    await expect(page.getByLabel('Nombre completo')).toHaveValue(patientName);

    const scheduleManually = async (daysAhead) => {
      await page.goto('/appointments');
      await expect(page.getByRole('heading', { name: 'Agendar cita manualmente' })).toBeVisible();
      await page.getByLabel('Buscar paciente').fill(patientName);
      await page.getByRole('button', { name: 'Buscar' }).click();
      await expect(page.getByLabel('Paciente para la cita')).toContainText(patientName, { timeout: 15_000 });
      await page.getByLabel('Paciente para la cita').selectOption({ label: patientName });
      // Los minutos aleatorios no evitan solapamientos: una cita de 30 minutos puede
      // colisionar con otra ya existente. Probar huecos alejados y reintentar solo ante
      // el conflicto esperado, manteniendo siempre la cita fuera del horario público.
      await page.getByLabel('Duración de la cita').selectOption({ label: '30 min' });
      await page.getByLabel('Modalidad de la cita').selectOption('in_person');
      const candidateHours = [1, 4, 7, 22];
      let appointmentCreated = false;
      let lastConflict = '';
      for (let attempt = 0; attempt < 12 && !appointmentCreated; attempt++) {
        const candidateDate = localDateTime(daysAhead + Math.floor(attempt / candidateHours.length), candidateHours[attempt % candidateHours.length], 0);
        await page.getByLabel('Fecha y hora de la cita').fill(candidateDate);
        const appointmentResponsePromise = page.waitForResponse(response =>
          response.request().method() === 'POST' && new URL(response.url()).pathname.endsWith('/professional/consultation-actions/appointment'));
        await page.getByRole('button', { name: 'Crear cita confirmada' }).click();
        const appointmentResponse = await appointmentResponsePromise;
        const appointmentResponseBody = await appointmentResponse.text();
        if (appointmentResponse.status() === 409 && /horario.*ocupado|ya está ocupado/i.test(appointmentResponseBody)) {
          lastConflict = appointmentResponseBody;
          continue;
        }
        expect(appointmentResponse.ok(), `El API de cita manual respondió ${appointmentResponse.status()}: ${appointmentResponseBody}`).toBeTruthy();
        appointmentCreated = true;
      }
      expect(appointmentCreated, `No se encontró un hueco libre tras 12 intentos. Último conflicto: ${lastConflict}`).toBe(true);
      await expect(page.getByText('Cita creada y confirmada. La disponibilidad semanal solo afecta a las reservas públicas.')).toBeVisible({ timeout: 15_000 });
      const appointmentCard = page.locator('.appointment').filter({ hasText: patientName }).filter({ has: page.getByRole('button', { name: 'Iniciar consulta' }) }).first();
      await expect(appointmentCard).toBeVisible({ timeout: 15_000 });
      await appointmentCard.getByRole('button', { name: 'Iniciar consulta' }).click();
      await expect(page.getByText('CONSULTA GUIADA', { exact: true })).toBeVisible({ timeout: 20_000 });
      await expect(page.getByRole('heading', { name: patientName })).toBeVisible();
      await expect(page.getByLabel('¿Cómo se gestionará el entrenamiento?')).toBeVisible();
      await page.getByLabel('¿Cómo se gestionará el entrenamiento?').selectOption('self');
      await expect(page.getByText('Decisión guardada; podrás modificarla más adelante.', { exact: true })).toBeVisible({ timeout: 10_000 });
      await page.locator('.stepper button').filter({ hasText: 'Entrenamiento' }).click();
      await expect(page.getByRole('heading', { name: 'Entrenamiento', exact: true })).toBeVisible();
      await page.getByLabel('Nombre de la rutina').fill('Rutina inicial E2E');
      await page.getByLabel('Ejercicios y prescripción').fill('Sentadilla · 3 × 8–12 · descanso 90 s');
      const routineResponsePromise = page.waitForResponse(response =>
        response.request().method() === 'POST' && new URL(response.url()).pathname.endsWith('/quick-routine'));
      await page.getByRole('button', { name: 'Crear y asignar rutina' }).click();
      const routineResponse = await routineResponsePromise;
      expect(routineResponse.status(), 'La rutina rápida debe crearse de forma transaccional').toBe(201);
      await expect(page.getByText('Rutina creada y asignada', { exact: false })).toBeVisible({ timeout: 10_000 });
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

  test('la videollamada online permanece integrada al cambiar de paso y espera su ventana de acceso', async ({ page }) => {
    await page.goto('/clients/nuevo');
    await expect(page.getByRole('heading', { name: 'Nuevo Cliente' })).toBeVisible();
    await page.getByLabel('Nombre completo').fill(patientName);
    await page.getByLabel('Email').fill(`e2e-video-${Date.now()}@invalid.example`);
    await page.getByLabel('Notas').fill('Paciente ficticio para la prueba E2E de videollamada integrada.');
    await page.getByRole('button', { name: 'Crear cliente' }).click();
    await expect(page).toHaveURL(/\/clients\/\d+(?:[/?#]|$)/, { timeout: 20_000 });
    patientId = Number(page.url().match(/\/clients\/(\d+)/)?.[1]);
    expect(patientId).toBeGreaterThan(0);

    await page.goto('/appointments');
    await expect(page.getByRole('heading', { name: 'Agendar cita manualmente' })).toBeVisible();
    await page.getByLabel('Buscar paciente').fill(patientName);
    await page.getByRole('button', { name: 'Buscar' }).click();
    await expect(page.getByLabel('Paciente para la cita')).toContainText(patientName, { timeout: 15_000 });
    await page.getByLabel('Paciente para la cita').selectOption({ label: patientName });
    await page.getByLabel('Duración de la cita').selectOption({ label: '30 min' });
    await page.getByLabel('Modalidad de la cita').selectOption('online');

    let appointmentCreated = false;
    for (let attempt = 0; attempt < 12 && !appointmentCreated; attempt++) {
      const candidateDate = localDateTime(8 + Math.floor(attempt / 4), [1, 7, 22, 23][attempt % 4], 0);
      await page.getByLabel('Fecha y hora de la cita').fill(candidateDate);
      const responsePromise = page.waitForResponse(response =>
        response.request().method() === 'POST' &&
        new URL(response.url()).pathname.endsWith('/professional/consultation-actions/appointment'));
      await page.getByRole('button', { name: 'Crear cita confirmada' }).click();
      const response = await responsePromise;
      const body = await response.text();
      if (response.status() === 409 && /horario.*ocupado|ya está ocupado/i.test(body)) continue;
      expect(response.ok(), `El API de cita online respondió ${response.status()}: ${body}`).toBeTruthy();
      appointmentCreated = true;
    }
    expect(appointmentCreated, 'Debe poder encontrarse un hueco para la cita online E2E').toBe(true);
    await expect(page.getByText('Cita creada y confirmada. La disponibilidad semanal solo afecta a las reservas públicas.')).toBeVisible({ timeout: 15_000 });
    const appointmentCard = page.locator('.appointment').filter({ hasText: patientName }).filter({ has: page.getByRole('button', { name: 'Iniciar consulta' }) }).first();
    await expect(appointmentCard).toBeVisible({ timeout: 15_000 });
    await appointmentCard.getByRole('button', { name: 'Iniciar consulta' }).click();

    await expect(page.getByText('CONSULTA GUIADA', { exact: true })).toBeVisible({ timeout: 20_000 });
    await expect(page.locator('app-video-consultation')).toBeVisible();
    await expect(page.getByText('Videollamada integrada')).toBeVisible();
    await expect(page.getByText('Acceso aún no disponible')).toBeVisible();
    const consultationUrl = page.url();

    await page.locator('.stepper button').filter({ hasText: 'Entrenamiento' }).click();
    await expect(page.getByRole('heading', { name: 'Entrenamiento', exact: true })).toBeVisible();
    await expect(page.getByLabel('Gestión del entrenamiento')).toHaveValue('none');
    await page.getByRole('button', { name: 'Omitir entrenamiento por ahora' }).click();
    await expect(page.getByRole('heading', { name: 'Plan nutricional inicial' })).toBeVisible();

    await page.getByRole('button', { name: /Objetivos/ }).first().click();
    await expect(page.getByRole('heading', { name: 'Objetivos y cambios acordados' })).toBeVisible();
    await expect(page.locator('app-video-consultation')).toBeVisible();
    await expect(page).toHaveURL(consultationUrl);

    await page.locator('.stepper button').filter({ hasText: 'Anamnesis' }).click();
    await expect(page.locator('app-video-consultation')).toBeVisible();
    await expect(page).toHaveURL(consultationUrl);

    await page.locator('app-video-consultation').getByRole('button', { name: 'Cerrar vídeo' }).click();
    await expect(page.locator('app-video-consultation')).toHaveCount(0);
    await page.getByRole('button', { name: 'Mostrar videollamada' }).click();
    await expect(page.locator('app-video-consultation')).toBeVisible();
    await expect(page.getByText('Acceso aún no disponible')).toBeVisible();
  });

});
