import { test, expect } from '@playwright/test';

function futureSlot(daysAhead = 10) {
  const start = new Date();
  start.setDate(start.getDate() + daysAhead);
  start.setHours(12, 0, 0, 0);
  const end = new Date(start.getTime() + 30 * 60_000);
  return { startsAt: start.toISOString(), endsAt: end.toISOString(), nutritionistId: 22, nutritionistName: 'Nutricionista E2E', onlineConsultationsAvailable: true, modality: 'both' };
}

function futureAppointment(id = 44, status = 'requested') {
  const slot = futureSlot(12);
  return { id, ...slot, clientId: 321, clientName: 'Paciente E2E Ficticio', status, modality: 'in_person', professionalNotes: null };
}

async function mockPortal(page, { appointments = [], slots = [futureSlot()], bookingError = null } = {}) {
  const calls = [];
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    calls.push({ path: url.pathname, method, body: method === 'POST' ? request.postDataJSON() : null });
    if (url.pathname.endsWith('/portal/auth') && method === 'POST') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ clientId: 321, fullName: 'Paciente E2E Ficticio' }) });
    } else if (url.pathname.endsWith('/portal/profile') && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ id: 321, fullName: 'Paciente E2E Ficticio', hasActiveDiet: false }) });
    } else if (url.pathname.endsWith('/appointments/slots') && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(slots) });
    } else if (url.pathname.endsWith('/appointments/mine') && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(appointments) });
    } else if (url.pathname.endsWith('/appointments') && method === 'POST') {
      if (bookingError) {
        await route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ message: bookingError }) });
      } else {
        const body = request.postDataJSON();
        await route.fulfill({ status: 201, contentType: 'application/json', body: JSON.stringify({ id: 55, startsAt: body.startsAt, endsAt: new Date(new Date(body.startsAt).getTime() + body.durationMinutes * 60_000).toISOString(), status: 'requested', clientId: 321, nutritionistId: 22, nutritionistName: 'Nutricionista E2E', modality: body.modality }) });
      }
    } else if (url.pathname.endsWith('/cancel') && method === 'POST') {
      await route.fulfill({ status: 204, body: '' });
    } else if (url.pathname.endsWith('/commercial-communications/preferences')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ emailEnabled: false }) });
    } else if (url.pathname.endsWith('/portal/push/vapid-public-key')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ publicKey: '' }) });
    } else {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
    }
  });
  return calls;
}

async function loginByPin(page) {
  await page.goto('/patient');
  await expect(page.getByRole('heading', { name: 'Portal del Paciente' })).toBeVisible();
  await page.getByLabel('Email o Teléfono').fill('paciente-e2e@invalid.example');
  await page.getByLabel('PIN de Acceso').fill('123456');
  await page.getByRole('button', { name: 'Acceder al Portal' }).click();
  await expect(page.locator('app-patient-header')).toBeVisible({ timeout: 15_000 });
}

async function openAppointments(page) {
  await page.getByRole('button', { name: 'Ver mis citas', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Reserva una cita con tu nutricionista' })).toBeVisible();
}

test.describe('P0 — citas del paciente: reservar y cancelar', () => {
  test('solicita una cita online y refleja el estado pendiente de confirmación', async ({ page }) => {
    const calls = await mockPortal(page);
    await loginByPin(page);
    await openAppointments(page);
    await page.locator('.calendar-day.available').first().click();
    await expect(page.locator('.appointment-slots .appointment-slot').first()).toBeVisible();
    await page.locator('.appointment-modality').getByRole('button', { name: /Online/ }).click();
    await page.locator('.appointment-slots .appointment-slot').first().click();
    await expect(page.locator('.appointment-success')).toContainText('Solicitud de cita enviada.');
    await expect(page.locator('.appointment-item')).toContainText('Pendiente de confirmación');
    const booking = calls.find(call => call.path.endsWith('/appointments') && call.method === 'POST');
    expect(booking?.body).toMatchObject({ durationMinutes: 30, modality: 'online' });
    expect(booking?.body.startsAt).toBeTruthy();
  });

  test('cancelar una cita requiere confirmación y actualiza el historial', async ({ page }) => {
    const calls = await mockPortal(page, { appointments: [futureAppointment()] , slots: [] });
    await loginByPin(page);
    await openAppointments(page);
    await expect(page.locator('.appointment-item').getByText('Pendiente de confirmación').first()).toBeVisible();
    page.once('dialog', dialog => dialog.accept());
    await page.locator('.appointment-item-actions button.appointment-cancel').click();
    await expect(page.locator('.appointment-success')).toContainText('Cita cancelada.');
    await expect(page.getByText('Cancelada', { exact: true })).toBeVisible();
    expect(calls.some(call => call.path.endsWith('/appointments/44/cancel') && call.method === 'POST')).toBe(true);
  });

  test('rechazar la confirmación de cancelación no modifica la cita', async ({ page }) => {
    const calls = await mockPortal(page, { appointments: [futureAppointment()], slots: [] });
    await loginByPin(page);
    await openAppointments(page);
    page.once('dialog', dialog => dialog.dismiss());
    await page.locator('.appointment-item-actions button.appointment-cancel').click();
    await expect(page.locator('.appointment-item').getByText('Pendiente de confirmación').first()).toBeVisible();
    expect(calls.some(call => call.path.endsWith('/appointments/44/cancel') && call.method === 'POST')).toBe(false);
  });
});
