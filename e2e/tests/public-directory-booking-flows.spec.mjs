import { test, expect } from '@playwright/test';

// Las fixtures de disponibilidad se calculan en Node; fijar la zona horaria del navegador
// evita que un slot cercano a medianoche cambie de día entre el fixture y el calendario.
test.use({ timezoneId: 'UTC' });

// Estas pruebas ejercitan la UI real del directorio con respuestas API aisladas.
// No crean reservas reales, no envían correos y no alteran datos de producción.
const slug = 'e2e-profesional-fixture';
const apiProfile = {
  username: 'e2e-fixture',
  slug,
  fullName: 'Profesional E2E de Prueba',
  clinicName: 'Clínica E2E',
  city: 'Valencia',
  province: 'Valencia',
  clinicLogo: '',
  publicBio: 'Perfil ficticio para comprobar los flujos públicos del directorio.',
  specialties: 'Nutrición deportiva',
  onlineConsultations: true,
  specialtyList: ['Nutrición deportiva'],
  profileCompleteness: 100,
  rankingScore: 10,
  isVerified: true,
  availableRuleCount: 1,
  averageRating: 5,
  reviewCount: 1
};

function futureSlot(daysAhead = 10, hour = 11) {
  const start = new Date();
  start.setDate(start.getDate() + daysAhead);
  start.setHours(hour, 0, 0, 0);
  const end = new Date(start.getTime() + 30 * 60_000);
  return { startsAt: start.toISOString(), endsAt: end.toISOString(), nutritionistId: 999, nutritionistName: apiProfile.fullName };
}

async function mockDirectory(page, { profiles = [apiProfile], availability = [futureSlot()], bookingStatus = 201, bookingError = null } = {}) {
  const requests = { searches: [], bookings: [], availability: [] };
  await page.route('**/directory/professionals**', async route => {
    const request = route.request();
    const url = new URL(request.url());
    const path = url.pathname;
    if (request.method() === 'POST' && /\/appointments\/?$/.test(path)) {
      const payload = request.postDataJSON();
      requests.bookings.push({ url: request.url(), payload });
      if (bookingError) {
        await route.fulfill({ status: 409, contentType: 'application/json', body: JSON.stringify({ message: bookingError }) });
      } else {
        await route.fulfill({ status: bookingStatus, contentType: 'application/json', body: JSON.stringify({ startsAt: payload.startsAt, endsAt: new Date(new Date(payload.startsAt).getTime() + payload.durationMinutes * 60_000).toISOString(), status: 'pending', nutritionistName: apiProfile.fullName }) });
      }
      return;
    }
    if (/\/availability\/?$/.test(path)) {
      requests.availability.push({ url: request.url(), count: availability.length });
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(availability) });
      return;
    }
    if (/\/reviews\/?$/.test(path)) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ averageRating: 5, reviewCount: 1, reviews: [{ id: 1, rating: 5, comment: 'Atención excelente', createdAt: new Date().toISOString(), verified: true }] }) });
      return;
    }
    if (path.endsWith('/' + slug) || path.endsWith('/' + slug + '/')) {
      await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(apiProfile) });
      return;
    }
    if (path.endsWith('/not-found-e2e')) {
      await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ message: 'No encontrado' }) });
      return;
    }
    requests.searches.push({ url: request.url(), query: Object.fromEntries(url.searchParams.entries()) });
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(profiles) });
  });
  return requests;
}

async function openProfile(page) {
  await page.goto('/nutricionistas/' + slug);
  await expect(page.getByRole('heading', { name: 'Profesional E2E de Prueba', exact: true })).toBeVisible();
  await expect(page.locator('.availability')).toBeVisible();
  await expect(page.locator('.calendar-day.available').first()).toBeVisible({ timeout: 10_000 });
}

async function chooseSlot(page) {
  const day = page.locator('.calendar-day.available').first();
  await expect(day).toBeVisible();
  await day.click();
  // Wait for Angular to commit the selected date before querying the conditional slot list.
  await expect(day).toHaveClass(/selected/, { timeout: 5_000 });
  const firstSlot = page.locator('.day-slots .slot-list .slot').first();
  await expect(firstSlot).toBeVisible({ timeout: 5_000 });
  await firstSlot.click();
  await expect(page.locator('.booking-form')).toBeVisible();
}

test.describe('P0 — directorio público: búsqueda, perfil y solicitudes (API aislada)', () => {
  test('el directorio carga filtros y realiza una búsqueda sin filtros', async ({ page }) => {
    const requests = await mockDirectory(page);
    await page.goto('/nutricionistas');
    await expect(page.getByRole('heading', { name: 'Encuentra a tu nutricionista' })).toBeVisible();
    await expect(page.getByLabel('Ciudad o municipio')).toBeVisible();
    await expect(page.getByLabel('Provincia')).toBeVisible();
    await expect(page.getByLabel('Especialidad')).toBeVisible();
    await expect(page.getByLabel('Tu objetivo')).toBeVisible();
    await expect(page.getByRole('button', { name: /Buscar profesionales/ })).toBeEnabled();
    await expect(page.locator('.results .profile-card')).toHaveCount(1);
    expect(requests.searches.length).toBeGreaterThan(0);
  });

  test('los filtros se serializan en la consulta enviada al directorio', async ({ page }) => {
    const requests = await mockDirectory(page);
    await page.goto('/nutricionistas');
    await page.getByLabel('Ciudad o municipio').fill('  Valencia  ');
    await page.getByLabel('Provincia').fill('Valencia');
    await page.getByLabel('Especialidad').fill('Nutrición deportiva');
    await page.getByLabel('Tu objetivo').selectOption({ label: 'Nutrición deportiva' });
    await page.getByText('Con disponibilidad').click();
    await page.locator('.filter-option').filter({ hasText: 'Consulta online' }).click();
    await page.getByRole('button', { name: /Buscar profesionales/ }).click();
    await expect.poll(() => requests.searches.some(item => item.query.city === 'Valencia' && item.query.province === 'Valencia' && item.query.speciality === 'Nutrición deportiva' && item.query.goal === 'nutrición deportiva' && item.query.online === 'true' && item.query.availableOnly === 'true')).toBe(true);
    await expect(page.locator('.results .profile-card')).toHaveCount(1);
  });

  test('el filtro de consulta online de la ruta directa se mantiene activo', async ({ page }) => {
    const requests = await mockDirectory(page);
    await page.goto('/nutricionistas/online');
    await expect(page.getByLabel('Consulta online')).toBeChecked();
    await expect.poll(() => requests.searches.some(item => item.query.online === 'true')).toBe(true);
  });

  test('una búsqueda sin coincidencias presenta un estado vacío claro', async ({ page }) => {
    await mockDirectory(page, { profiles: [] });
    await page.goto('/nutricionistas');
    await expect(page.getByRole('status').filter({ hasText: 'No hemos encontrado profesionales' })).toBeVisible();
    await expect(page.locator('.results')).toHaveCount(0);
  });

  test('un error de API del directorio se presenta sin excepción JS', async ({ page }) => {
    await page.route('**/directory/professionals**', async route => {
      if (route.request().method() === 'GET') {
        await route.fulfill({ status: 503, contentType: 'application/json', body: JSON.stringify({ message: 'Temporalmente no disponible' }) });
      } else await route.continue();
    });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('/nutricionistas');
    await expect(page.getByRole('alert')).toContainText('No se ha podido cargar el directorio.');
    expect(errors).toEqual([]);
  });

  test('el perfil muestra identidad, especialidad, reputación y disponibilidad', async ({ page }) => {
    await mockDirectory(page);
    await openProfile(page);
    await expect(page.locator('.public-profile')).toContainText('Clínica E2E');
    await expect(page.locator('.public-profile')).toContainText('Valencia');
    await expect(page.locator('.public-profile')).toContainText('Nutrición deportiva');
    await expect(page.getByRole('heading', { name: 'Valoraciones' })).toBeVisible();
    await expect(page.getByText('Atención excelente')).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Próximos horarios disponibles' })).toBeVisible();
  });

  test('un perfil inexistente muestra un error claro sin provocar excepciones', async ({ page }) => {
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('**/directory/professionals/not-found-e2e', route => route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({ message: 'No encontrado' }) }));
    await page.goto('/nutricionistas/not-found-e2e');
    await expect(page.getByRole('alert')).toContainText('No se ha encontrado el profesional solicitado.');
    expect(errors).toEqual([]);
  });

  test('seleccionar una fecha y un hueco solo abre el formulario; no reserva por sí solo', async ({ page }) => {
    const requests = await mockDirectory(page);
    await openProfile(page);
    await chooseSlot(page);
    expect(requests.bookings).toHaveLength(0);
    await expect(page.getByLabel('Nombre completo')).toBeVisible();
    await expect(page.getByLabel('Email', { exact: true })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Solicitar cita' })).toBeVisible();
  });

  test('cancelar la selección cierra el formulario sin enviar una solicitud', async ({ page }) => {
    const requests = await mockDirectory(page);
    await openProfile(page);
    await chooseSlot(page);
    await page.locator('.booking-actions').getByRole('button', { name: 'Cancelar', exact: true }).click();
    await expect(page.locator('.booking-form')).toHaveCount(0);
    expect(requests.bookings).toHaveLength(0);
  });

  test('los campos obligatorios y el formato email impiden enviar datos inválidos', async ({ page }) => {
    const requests = await mockDirectory(page);
    await openProfile(page);
    await chooseSlot(page);
    await page.getByRole('button', { name: 'Solicitar cita' }).click();
    expect(await page.getByLabel('Nombre completo').evaluate(element => element.validity.valueMissing)).toBe(true);
    await page.getByLabel('Nombre completo').fill('Paciente E2E Ficticio');
    await page.getByLabel('Email', { exact: true }).fill('no-es-un-email');
    await page.getByRole('button', { name: 'Solicitar cita' }).click();
    expect(await page.getByLabel('Email', { exact: true }).evaluate(element => element.validity.typeMismatch)).toBe(true);
    expect(requests.bookings).toHaveLength(0);
  });

  test('la solicitud envía modalidad online y datos normalizados, sin cobrar ni enviar correo real', async ({ page }) => {
    const requests = await mockDirectory(page);
    await openProfile(page);
    await chooseSlot(page);
    await page.getByLabel('Nombre completo').fill('  Paciente E2E Ficticio  ');
    await page.getByLabel('Email', { exact: true }).fill('E2E-BOOKING@INVALID.EXAMPLE');
    await page.getByLabel('Teléfono').fill('600000000');
    await page.getByLabel('Comentario').fill('Prueba automatizada ficticia');
    await page.locator('.booking-modality label').filter({ hasText: 'Consulta online' }).click();
    await page.getByRole('button', { name: 'Solicitar cita' }).click();
    await expect(page.getByRole('status').filter({ hasText: 'Solicitud enviada' })).toBeVisible();
    expect(requests.bookings).toHaveLength(1);
    expect(requests.bookings[0].payload).toMatchObject({
      fullName: 'Paciente E2E Ficticio',
      email: 'e2e-booking@invalid.example',
      phone: '600000000',
      patientNotes: 'Prueba automatizada ficticia',
      modality: 'online',
      durationMinutes: 30
    });
    await expect(page.locator('.booking-form')).toHaveCount(0);
  });

  test('un conflicto de disponibilidad se comunica y permite corregir la selección', async ({ page }) => {
    await mockDirectory(page, { bookingError: 'El horario ya no está disponible.' });
    await openProfile(page);
    await chooseSlot(page);
    await page.getByLabel('Nombre completo').fill('Paciente E2E Ficticio');
    await page.getByLabel('Email', { exact: true }).fill('e2e-conflict@invalid.example');
    await page.getByRole('button', { name: 'Solicitar cita' }).click();
    await expect(page.getByRole('alert')).toContainText('El horario ya no está disponible.');
    await expect(page.locator('.booking-form')).toBeVisible();
  });

  test('la modalidad online explica dónde se recibirá el acceso a la consulta', async ({ page }) => {
    await mockDirectory(page);
    await openProfile(page);
    await chooseSlot(page);
    await page.locator('.booking-modality label').filter({ hasText: 'Consulta online' }).click();
    await expect(page.locator('.booking-form')).toContainText('Recibirás el acceso a la consulta online en tu portal');
    await expect(page.locator('.booking-note')).toContainText('no requiere pago online');
  });

  test('la búsqueda y el perfil no producen errores JavaScript no controlados', async ({ page }) => {
    await mockDirectory(page);
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('/nutricionistas');
    await expect(page.locator('.results .profile-card')).toHaveCount(1);
    await page.getByRole('link', { name: 'Ver perfil y disponibilidad' }).click();
    await expect(page.locator('.public-profile')).toBeVisible();
    expect(errors).toEqual([]);
  });

  test('el botón volver del perfil regresa a la lista pública', async ({ page }) => {
    await mockDirectory(page);
    await openProfile(page);
    await page.getByRole('link', { name: /Volver al directorio/ }).click();
    await expect(page).toHaveURL(/\/nutricionistas(?:[/?#]|$)/);
    await expect(page.getByRole('heading', { name: 'Encuentra a tu nutricionista' })).toBeVisible();
  });
});
