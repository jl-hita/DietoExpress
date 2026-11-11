import { Routes } from '@angular/router';
import { PatientAppShellComponent } from './patient-app-shell.component';
import { PatientHomeComponent } from './patient-home.component';
import { PatientAppPendingComponent } from './patient-app-pending.component';

/**
 * Nueva interfaz del paciente (`/portal`), construida para tener la misma estructura que la app Android. Convive con el portal
 * actual (`/patient`) hasta que todas las secciones estén trasladadas; entonces `/patient` pasará a redirigir aquí.
 * Las secciones sin trasladar usan `PatientAppPendingComponent`.
 */
export const PATIENT_APP_ROUTES: Routes = [
  {
    path: '',
    component: PatientAppShellComponent,
    children: [
      { path: '', pathMatch: 'full', component: PatientHomeComponent },
      ...['diet', 'training', 'progress', 'appointments', 'documents', 'shopping', 'messages', 'profile']
        .map(path => ({ path, component: PatientAppPendingComponent })),
      { path: '**', redirectTo: '' }
    ]
  }
];
