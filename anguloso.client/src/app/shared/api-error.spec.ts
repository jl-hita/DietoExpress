import { apiErrorMessage } from './api-error';

describe('apiErrorMessage', () => {
  it('muestra el texto o el campo message que envía la API', () => {
    expect(apiErrorMessage({ error: 'El email ya está registrado.' }, 'x')).toBe('El email ya está registrado.');
    expect(apiErrorMessage({ error: { message: 'Faltan pacientes' } }, 'x')).toBe('Faltan pacientes');
    expect(apiErrorMessage({ error: { error: 'Slug duplicado' } }, 'x')).toBe('Slug duplicado');
  });

  it('usa el texto alternativo cuando el cuerpo no es un mensaje', () => {
    expect(apiErrorMessage({ error: { title: 'Se ha producido un error inesperado.', status: 500 } }, 'x')).toBe('x');
    expect(apiErrorMessage({ error: '<html><body>502</body></html>' }, 'x')).toBe('x');
    expect(apiErrorMessage({ error: null }, 'x')).toBe('x');
    expect(apiErrorMessage(undefined, 'x')).toBe('x');
  });
});
