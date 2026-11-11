import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { registerLocaleData } from '@angular/common';
import localeCa from '@angular/common/locales/ca';
import localeEs from '@angular/common/locales/es';
import { ErrorHandler, LOCALE_ID, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { bootstrapApplication } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { AppComponent } from './app/app.component';
import { routes } from './app/app.routes';
import { zoneChangeDetectionInterceptor } from './app/interceptors/zone-change-detection.interceptor';
import { ApplicationErrorHandler } from './app/servicios/application-error-handler';
import { I18nService, detectLanguage, readStoredLanguage } from './app/i18n/i18n.service';

// Los pipes de fecha y número usan el idioma elegido (nombres de meses, separadores decimales). El inglés viene incluido en Angular.
registerLocaleData(localeEs, 'es');
registerLocaleData(localeCa, 'ca');
const startupLanguage = detectLanguage(readStoredLanguage(), navigator.languages ?? [navigator.language]);

bootstrapApplication(AppComponent, {
  providers: [
    provideRouter(routes),
    { provide: LOCALE_ID, useValue: startupLanguage },
    // Carga el diccionario del idioma elegido antes de pintar nada, para que no haya un parpadeo con textos en otro idioma.
    provideAppInitializer(() => inject(I18nService).init()),
    { provide: ErrorHandler, useClass: ApplicationErrorHandler },
    provideHttpClient(withInterceptors([zoneChangeDetectionInterceptor])),
    // Angular 21+ defaults to zoneless change detection. Keep Zone.js-based
    // detection while the existing screens still update ordinary fields from
    // HttpClient subscriptions; the interceptor re-enters the zone for every
    // HTTP notification so views do not need per-component detectChanges().
    provideZoneChangeDetection()
  ]
}).catch(err => console.error(err));
