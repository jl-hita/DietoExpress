import { test, expect } from '@playwright/test';

// API interceptada en todos los casos: no se accede a mensajes ni documentos reales.
async function mockPortal(page, { messages = [], documents = [], failDocumentTabLoad = false } = {}) {
  const calls = [];
  let documentGets = 0;
  await page.route('**/api/**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const method = request.method();
    calls.push({ path: url.pathname, method });
    if (url.pathname.endsWith('/portal/auth') && method === 'POST') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ clientId: 321, fullName: 'Paciente E2E Ficticio' }) });
    } else if (url.pathname.endsWith('/portal/profile') && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ id: 321, fullName: 'Paciente E2E Ficticio', hasActiveDiet: false, clinicName: 'Clínica E2E' }) });
    } else if (url.pathname.endsWith('/portal/documents') && method === 'GET') {
      documentGets++;
      if (failDocumentTabLoad && documentGets === 2) {
        await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Documentación temporalmente no disponible.' }) });
      } else {
        await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(documents) });
      }
    } else if (url.pathname.endsWith('/portal/documents/44/accept') && method === 'POST') {
      await route.fulfill({ status: 204, body: '' });
    } else if (url.pathname.endsWith('/broadcast-messages/inbox') && method === 'GET') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(messages) });
    } else if (url.pathname.endsWith('/broadcast-messages/77/read') && method === 'PATCH') {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({}) });
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
  await expect(page.locator('.patient-login-screen')).toHaveCount(0);
}

test.describe('P1 — mensajes y documentación del portal del paciente', () => {
  test('el paciente puede marcar una comunicación oficial como leída', async ({ page }) => {
    const calls = await mockPortal(page, {
      messages: [{ id: 77, title: 'Aviso E2E', body: 'Comunicación ficticia de prueba.', createdAt: '2026-10-01T10:00:00Z', readAt: null }]
    });
    await loginByPin(page);
    const messagesTab = page.getByRole('button', { name: 'Ver mis mensajes', exact: true });
    await messagesTab.click();
    await expect(page.getByRole('heading', { name: 'Comunicaciones de DietoExpress' })).toBeVisible();
    await expect(page.getByText('Comunicación ficticia de prueba.')).toBeVisible();
    await page.getByRole('button', { name: 'Marcar como leído' }).click();
    await expect(page.locator('.broadcast-read-status')).toContainText('Leído');
    expect(calls.some(call => call.path.endsWith('/broadcast-messages/77/read') && call.method === 'PATCH')).toBe(true);
  });

  test('el paciente puede aceptar un documento pendiente y ver el estado actualizado', async ({ page }) => {
    const calls = await mockPortal(page, {
      documents: [{ id: 44, name: 'Consentimiento E2E', documentType: 'consent', status: 'pending', version: 1, requiresSignature: true, mimeType: 'application/pdf', fileSize: 100, sha256: 'e2e-test-hash', createdAt: '2026-10-01T10:00:00Z', updatedAt: '2026-10-01T10:00:00Z' }]
    });
    await loginByPin(page);
    await page.getByRole('button', { name: 'Ver mi documentación', exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Documentación' })).toBeVisible();
    await expect(page.getByText('Consentimiento E2E')).toBeVisible();
    await expect(page.getByText('Pendiente de firma/aceptación')).toBeVisible();
    await page.getByRole('button', { name: 'Aceptar documento' }).click();
    await expect(page.getByText('Aceptado')).toBeVisible();
    expect(calls.some(call => call.path.endsWith('/portal/documents/44/accept') && call.method === 'POST')).toBe(true);
  });

  test('un fallo al cargar documentos se muestra y permite reintentar', async ({ page }) => {
    const calls = await mockPortal(page, { failDocumentTabLoad: true, documents: [] });
    await loginByPin(page);
    await page.getByRole('button', { name: 'Ver mi documentación', exact: true }).click();
    await expect(page.getByText('Documentación temporalmente no disponible.')).toBeVisible();
    await page.getByRole('button', { name: 'Reintentar documentación' }).click();
    await expect(page.getByRole('heading', { name: 'No tienes documentos todavía' })).toBeVisible();
    expect(calls.filter(call => call.path.endsWith('/portal/documents') && call.method === 'GET')).toHaveLength(3);
  });
});
