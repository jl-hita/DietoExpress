import { provideHttpClient } from '@angular/common/http';
import { provideZoneChangeDetection } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { AppComponent } from './app/app.component';
import { routes } from './app/app.routes';

bootstrapApplication(AppComponent, {
  providers: [
    provideRouter(routes),
    provideHttpClient(),
    // Angular 21+ uses zoneless change detection by default. Existing screens
    // update component fields in HttpClient subscriptions, so keep Zone.js-based
    // change detection until those screens are migrated to signals/async pipes.
    provideZoneChangeDetection()
  ]
}).catch(err => console.error(err));
