import { Route } from '@angular/router';
import { UserCreateComponent } from './componentes/user-create/user-create.component';
import { AuthGuard } from './guards/auth.guard';
import { LoginComponent } from './componentes/login/login.component';
import { UserResetComponent } from './componentes/user-reset/user-reset.component';
import { ConfirmarEmailComponent } from './componentes/confirmar-email/confirmar-email.component';
import { ClientDetailComponent } from './componentes/client-detail/client-detail.component';
import { ClientsListComponent } from './componentes/clients-list/clients-list.component';
import { LayoutComponent } from './componentes/layout/layout.component';
import { ClientCreateComponent } from './componentes/client-create/client-create.component';
import { DietsListComponent } from './componentes/diets-list/diets-list.component';
import { DietCreateComponent } from './componentes/diet-create/diet-create.component';
import { SettingsComponent } from './componentes/settings/settings.component';
import { PatientPortalComponent } from './componentes/patient-portal/patient-portal.component';
import { SetupWizardComponent } from './componentes/setup/setup-wizard.component';
import { AdminDashboardComponent } from './componentes/admin/admin-dashboard.component';
import { AdminLogComponent } from './componentes/admin/admin-log.component';
import { SetupGuard } from './guards/setup.guard';
import { SuperAdminGuard } from './guards/super-admin.guard';
import { ClinicDashboardComponent } from './componentes/clinic/clinic-dashboard.component';
import { AdminPlansComponent } from './componentes/admin/admin-plans.component';
import { BillingComponent } from './componentes/billing/billing.component';
import { LandingComponent } from './componentes/landing/landing.component';
import { OnboardingComponent } from './componentes/onboarding/onboarding.component';
import { SubscriptionGuard } from './guards/subscription.guard';
import { AppointmentsComponent } from './componentes/appointments/appointments.component';

export interface AppRoute extends Route {
  showInMenu?: boolean;
  title?: string;
}

export const routes: AppRoute[] = [
  { path: '', component: LandingComponent, pathMatch: 'full', title: 'DietoExpress' },
  { path: 'setup', component: SetupWizardComponent, canActivate: [SetupGuard], title: 'Inicialización del Sistema' },
  { path: 'login', component: LoginComponent },
  { path: 'crear-usuario', component: UserCreateComponent, title: 'Crear cuenta', showInMenu: false },
  { path: 'reset-pwd', component: UserResetComponent, title: 'Crear usuario', showInMenu: false },
  { path: 'confirmar-email', component: ConfirmarEmailComponent, title: 'Confirmar email', showInMenu: false },
  { path: 'patient', component: PatientPortalComponent, title: 'Portal del Paciente' },

  { path: '', component: LayoutComponent, canActivate: [AuthGuard],
    children: [
      { path: 'clients', component: ClientsListComponent, canActivate: [SubscriptionGuard] },
      { path: 'appointments', component: AppointmentsComponent, canActivate: [SubscriptionGuard], title: 'Agenda y citas' },
      { path: 'clients/nuevo', component: ClientCreateComponent, canActivate: [SubscriptionGuard], title: 'Nuevo cliente' },
      { path: 'clients/:id', component: ClientDetailComponent, canActivate: [SubscriptionGuard] },
      { path: 'diets', component: DietsListComponent, canActivate: [SubscriptionGuard], title: 'Dietas' },
      { path: 'diets/nuevo', component: DietCreateComponent, canActivate: [SubscriptionGuard], title: 'Nueva dieta' },
      { path: 'diets/:id', component: DietCreateComponent, canActivate: [SubscriptionGuard], title: 'Editar dieta' },
      { path: 'onboarding', component: OnboardingComponent, title: 'Bienvenido a DietoExpress' },
      { path: 'settings', component: SettingsComponent, title: 'Ajustes' },
      { path: 'billing', component: BillingComponent, title: 'Suscripción' },
      { path: 'admin', component: AdminDashboardComponent, canActivate: [SuperAdminGuard], title: 'Panel SuperAdmin' },
      { path: 'admin/logs', component: AdminLogComponent, canActivate: [SuperAdminGuard], title: 'Logs del sistema' },
      { path: 'admin/plans', component: AdminPlansComponent, canActivate: [SuperAdminGuard], title: 'Planes SaaS' },
      { path: 'clinic', component: ClinicDashboardComponent, title: 'Panel de clínica' }
    ]
  },
  { path: '**', redirectTo: '' }
];
