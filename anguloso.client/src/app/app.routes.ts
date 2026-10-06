import { Route } from '@angular/router';
import { AuthGuard } from './guards/auth.guard';
import { SetupGuard } from './guards/setup.guard';
import { SuperAdminGuard } from './guards/super-admin.guard';
import { SubscriptionGuard } from './guards/subscription.guard';

export interface AppRoute extends Route {
  showInMenu?: boolean;
  title?: string;
}

// Las rutas agrupan las fronteras de acceso de la aplicación; los guards mejoran el control de navegación,
// mientras la API mantiene la autorización definitiva. Las vistas se cargan bajo demanda para evitar
// incluir módulos completos (dietas, pacientes, administración, etc.) en el bundle inicial.
export const routes: AppRoute[] = [
  { path: '', loadComponent: () => import('./componentes/landing/landing.component').then(m => m.LandingComponent), pathMatch: 'full', title: 'DietoExpress' },
  { path: 'setup', loadComponent: () => import('./componentes/setup/setup-wizard.component').then(m => m.SetupWizardComponent), canActivate: [SetupGuard], title: 'Inicialización del Sistema' },
  { path: 'login', loadComponent: () => import('./componentes/login/login.component').then(m => m.LoginComponent) },
  { path: 'crear-usuario', loadComponent: () => import('./componentes/user-create/user-create.component').then(m => m.UserCreateComponent), title: 'Crear cuenta', showInMenu: false },
  { path: 'reset-pwd', loadComponent: () => import('./componentes/user-reset/user-reset.component').then(m => m.UserResetComponent), title: 'Crear usuario', showInMenu: false },
  { path: 'reset-password', loadComponent: () => import('./componentes/user-reset/user-reset.component').then(m => m.UserResetComponent), title: 'Restablecer contraseña', showInMenu: false },
  { path: 'confirmar-email', loadComponent: () => import('./componentes/confirmar-email/confirmar-email.component').then(m => m.ConfirmarEmailComponent), title: 'Confirmar email', showInMenu: false },
  { path: 'patient', loadComponent: () => import('./componentes/patient-portal/patient-portal.component').then(m => m.PatientPortalComponent), title: 'Portal del Paciente' },
  { path: 'video-consultation/:appointmentId', loadComponent: () => import('./componentes/video-consultation/video-consultation.component').then(m => m.VideoConsultationComponent), title: 'Consulta online' },
  { path: 'legal', loadComponent: () => import('./componentes/legal-documents/legal-documents.component').then(m => m.LegalDocumentsComponent), title: 'Documentación legal' },
  { path: 'nutricionistas', loadComponent: () => import('./componentes/directory/directory.component').then(m => m.DirectoryComponent), title: 'Directorio de nutricionistas' },
  { path: 'nutricionistas/:slug', loadComponent: () => import('./componentes/directory/directory.component').then(m => m.DirectoryComponent), title: 'Perfil profesional' },

  { path: '', loadComponent: () => import('./componentes/layout/layout.component').then(m => m.LayoutComponent), canActivate: [AuthGuard],
    children: [
      { path: 'dashboard', loadComponent: () => import('./componentes/dashboard/dashboard.component').then(m => m.DashboardComponent), canActivate: [SubscriptionGuard], title: 'Dashboard' },
      { path: 'clients', loadComponent: () => import('./componentes/clients-list/clients-list.component').then(m => m.ClientsListComponent), canActivate: [SubscriptionGuard] },
      { path: 'appointments', loadComponent: () => import('./componentes/appointments/appointments.component').then(m => m.AppointmentsComponent), canActivate: [SubscriptionGuard], title: 'Agenda y citas' },
      { path: 'appointments/:appointmentId/consultation', loadComponent: () => import('./componentes/guided-consultation/guided-consultation.component').then(m => m.GuidedConsultationComponent), canActivate: [SubscriptionGuard], title: 'Consulta guiada' },
      { path: 'support', loadComponent: () => import('./componentes/support/support.component').then(m => m.SupportComponent), title: 'Soporte' },
      { path: 'messages', loadComponent: () => import('./componentes/messages/messages.component').then(m => m.MessagesComponent), canActivate: [SubscriptionGuard], title: 'Mensajes' },
      { path: 'statistics', loadComponent: () => import('./componentes/statistics/statistics.component').then(m => m.StatisticsComponent), canActivate: [SubscriptionGuard], title: 'Estadísticas' },
      { path: 'clients/nuevo', loadComponent: () => import('./componentes/client-create/client-create.component').then(m => m.ClientCreateComponent), canActivate: [SubscriptionGuard], title: 'Nuevo cliente' },
      { path: 'clients/:id/specializations', loadComponent: () => import('./componentes/client-specializations/client-specializations.component').then(m => m.ClientSpecializationsComponent), canActivate: [SubscriptionGuard], title: 'Especializaciones del paciente' },
      { path: 'clients/:id', loadComponent: () => import('./componentes/client-detail/client-detail.component').then(m => m.ClientDetailComponent), canActivate: [SubscriptionGuard] },
      { path: 'clients/:id/messages', loadComponent: () => import('./componentes/patient-chat/patient-chat.component').then(m => m.PatientChatComponent), canActivate: [SubscriptionGuard], title: 'Mensajes del paciente' },
      { path: 'diets', loadComponent: () => import('./componentes/diets-list/diets-list.component').then(m => m.DietsListComponent), canActivate: [SubscriptionGuard], title: 'Dietas' },
      { path: 'diets/nuevo', loadComponent: () => import('./componentes/diet-create/diet-create.component').then(m => m.DietCreateComponent), canActivate: [SubscriptionGuard], title: 'Nueva dieta' },
      { path: 'diets/:id', loadComponent: () => import('./componentes/diet-create/diet-create.component').then(m => m.DietCreateComponent), canActivate: [SubscriptionGuard], title: 'Editar dieta' },
      { path: 'onboarding', loadComponent: () => import('./componentes/onboarding/onboarding.component').then(m => m.OnboardingComponent), title: 'Bienvenido a DietoExpress' },
      { path: 'settings', loadComponent: () => import('./componentes/settings/settings.component').then(m => m.SettingsComponent), title: 'Ajustes' },
      { path: 'legal-manage', loadComponent: () => import('./componentes/legal/legal-dashboard.component').then(m => m.LegalDashboardComponent), canActivate: [SubscriptionGuard], title: 'Legal' },
      { path: 'documents', loadComponent: () => import('./componentes/document-templates/document-templates.component').then(m => m.DocumentTemplatesComponent), canActivate: [SubscriptionGuard], title: 'Documentación' },
      { path: 'automations', loadComponent: () => import('./componentes/automation-settings/automation-settings.component').then(m => m.AutomationSettingsComponent), canActivate: [SubscriptionGuard], title: 'Automatizaciones' },
      { path: 'billing', loadComponent: () => import('./componentes/billing/billing.component').then(m => m.BillingComponent), title: 'Suscripción' },
      { path: 'admin', loadComponent: () => import('./componentes/admin/admin-dashboard.component').then(m => m.AdminDashboardComponent), canActivate: [SuperAdminGuard], title: 'Panel SuperAdmin' },
      { path: 'admin/legal', loadComponent: () => import('./componentes/admin/admin-legal-settings.component').then(m => m.AdminLegalSettingsComponent), canActivate: [SuperAdminGuard], title: 'Legal — DietoExpress' },
      { path: 'admin/logs', loadComponent: () => import('./componentes/admin/admin-log.component').then(m => m.AdminLogComponent), canActivate: [SuperAdminGuard], title: 'Logs del sistema' },
      { path: 'admin/plans', loadComponent: () => import('./componentes/admin/admin-plans.component').then(m => m.AdminPlansComponent), canActivate: [SuperAdminGuard], title: 'Planes SaaS' },
      { path: 'admin/directory-verification', loadComponent: () => import('./componentes/admin/admin-directory-verification.component').then(m => m.AdminDirectoryVerificationComponent), canActivate: [SuperAdminGuard], title: 'Verificación del directorio' },
      { path: 'clinic', loadComponent: () => import('./componentes/clinic/clinic-dashboard.component').then(m => m.ClinicDashboardComponent), title: 'Panel de clínica' }
    ]
  },
  { path: '**', redirectTo: '' }
];
