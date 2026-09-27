import { AuthInterceptor } from './auth.interceptor';

describe('AuthInterceptor', () => {
  it('should be created', () => {
    const interceptor = new AuthInterceptor();
    expect(interceptor).toBeTruthy();
  });
});
