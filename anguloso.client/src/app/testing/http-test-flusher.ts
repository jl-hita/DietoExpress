import { HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture } from '@angular/core/testing';

/** Responde a las peticiones de arranque en los smoke tests para que nunca alcancen el servidor Karma. */
export function flushPendingHttpRequests(httpTesting: HttpTestingController, fixture: ComponentFixture<any>): void {
  for (let attempt = 0; attempt < 5; attempt++) {
    const pending = httpTesting.match(() => true);
    if (pending.length === 0) return;

    for (const request of pending) {
      const url = request.request.url;
      if (url.includes('/clients/can-create')) {
        request.flush({ allowed: true, reason: '' });
      } else if (url.includes('/clients?')) {
        request.flush({ items: [], totalCount: 0, page: 1, pageSize: 10 });
      } else if (url.includes('/auth/google-client-id')) {
        request.flush({ clientId: 'test-client' });
      } else if (url.includes('/legal-documents/current')) {
        request.flush([]);
      } else if (url.includes('/ingrediente/getIngredientes')) {
        request.flush([]);
      } else if (url.includes('/profile')) {
        request.flush({});
      } else if (url.includes('/auth/refreshSession')) {
        request.flush({ username: 'test', role: 'nutritionist' });
      } else {
        request.flush({});
      }
    }

    fixture.detectChanges();
  }

  const remaining = httpTesting.match(() => true);
  if (remaining.length > 0) {
    throw new Error(`Smoke test left ${remaining.length} HTTP request(s) unhandled.`);
  }
}
