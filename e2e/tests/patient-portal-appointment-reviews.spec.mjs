import { test, expect } from '@playwright/test';

function pastAppointment() {
  const start = new Date();
  start.setDate(start.getDate() - 10);
  start.setHours(12, 0, 0, 0);
  const end = new Date(start.getTime() + 30 * 60_000);
  return { id: 77, startsAt: start.toISOString(), endsAt: end.toISOString(), status: 'completed', clientId: 321, clientName: 'Paciente E2E Ficticio', nutritionistId: 22, nutritionistName: 'Nutricionista E2E', modality: 'in_person' };
}

async function mockPortal(page) {
  const reviewRequests = [];
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    if (url.pathname.endsWith('/portal/auth') && method === 'POST') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ clientId: 321, fullName: 'Paciente E2E Ficticio' }) });
    } else if (url.pathname.endsWith('/portal/profile') && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ id: 321, fullName: 'Paciente E2E Ficticio', hasActiveDiet: false }) });
    } else if (url.pathname.endsWith('/appointments/slots') && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
    } else if (url.pathname.endsWith('/appointments/mine') && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([pastAppointment()]) });
    } else if (url.pathname.endsWith('/portal/reviews') && method === 'POST') {
      reviewRequests.push(request.postDataJSON());
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ message: 'Gracias por tu valoración. La reseña se ha enviado para revisión.' }) });
    } else if (url.pathname.endsWith('/commercial-communications/preferences')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ emailEnabled: false }) });
    } else if (url.pathname.endsWith('/portal/push/vapid-public-key')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ publicKey: '' }) });
    } else {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify([]) });
    }
  });
  return reviewRequests;
}

async function loginByPin(page) {
  await page.goto('/patient');
  await expect(page.getByRole('heading', { name: 'Portal del Paciente' })).toBeVisible();
  await page.getByLabel('Email o Teléfono').fill('paciente-e2e@invalid.example');
  await page.getByLabel('PIN de Acceso').fill('123456');
  await page.getByRole('button', { name: 'Acceder al Portal' }).click();
  await expect(page.locator('app-patient-header')).toBeVisible({ timeout: 15_000 });
}

test.describe('P1 — valoración verificada desde el historial de citas', () => {
  test('el paciente publica una valoración para una cita completada', async ({ page }) => {
    const requests = await mockPortal(page);
    await loginByPin(page);
    await page.getByRole('button', { name: 'Ver mis citas', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Historial de citas' })).toBeVisible();
    await expect(page.getByText('¿Cómo fue tu consulta?')).toBeVisible();
    const publish = page.getByRole('button', { name: 'Publicar valoración' });
    await expect(publish).toBeDisabled();
    await page.getByRole('button', { name: '5 estrellas' }).click();
    await page.getByPlaceholder('Comentario opcional').fill('Atención excelente. Comentario ficticio E2E.');
    await expect(publish).toBeEnabled();
    await publish.click();
    await expect(page.getByText('Gracias por tu valoración. La reseña se ha enviado para revisión.')).toBeVisible();
    expect(requests).toHaveLength(1);
    expect(requests[0]).toMatchObject({ appointmentId: 77, rating: 5, comment: 'Atención excelente. Comentario ficticio E2E.' });
  });
});
