import { ClientDetail } from './client';

describe('Client model', () => {
  it('should allow a valid client object', () => {
    const client: ClientDetail = {
      id: 1,
      fullName: 'Test',
      biometrics: []
    };

    expect(client).toBeTruthy();
    expect(client.fullName).toBe('Test');
  });
});
