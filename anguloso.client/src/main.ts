import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideZoneChangeDetection } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { AppComponent } from './app/app.component';
import { routes } from './app/app.routes';
import { zoneChangeDetectionInterceptor } from './app/interceptors/zone-change-detection.interceptor';

bootstrapApplication(AppComponent, {
  providers: [
    provideRouter(routes),
    provideHttpClient(withInterceptors([zoneChangeDetectionInterceptor])),
    // Angular 21+ defaults to zoneless change detection. Keep Zone.js-based
    // detection while the existing screens still update ordinary fields from
    // HttpClient subscriptions; the interceptor re-enters the zone for every
    // HTTP notification so views do not need per-component detectChanges().
    provideZoneChangeDetection()
  ]
}).catch(err => console.error(err));
