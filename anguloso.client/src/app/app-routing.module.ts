import { NgModule } from '@angular/core';
import { Route, RouterModule, Routes } from '@angular/router';
import { UserCreateComponent } from './componentes/user-create/user-create.component';
import { AuthGuard } from './guards/auth.guard';
import { LoginComponent } from './componentes/login/login.component';
import { UserResetComponent } from './componentes/user-reset/user-reset.component';
import { ConfirmarEmailComponent } from './componentes/confirmar-email/confirmar-email.component';
import { FoodTesterComponent } from './componentes/food-tester/food-tester.component';
import { ClientDetailComponent } from './componentes/client-detail/client-detail.component';
import { ClientsListComponent } from './componentes/clients-list/clients-list.component';
import { LayoutComponent } from './componentes/layout/layout.component';
import { ClientCreateComponent } from './componentes/client-create/client-create.component';
import { DietsListComponent } from './componentes/diets-list/diets-list.component';
import { DietCreateComponent } from './componentes/diet-create/diet-create.component';
import { SettingsComponent } from './componentes/settings/settings.component';

export interface AppRoute extends Route {
  showInMenu?: boolean;
  title?: string;
}

const routes: AppRoute[] = [
  { path: 'login', component: LoginComponent },
  { path: 'crear-usuario', component: UserCreateComponent, title: 'Crear usuario', showInMenu: false },
  { path: 'reset-pwd', component: UserResetComponent, title: 'Crear usuario', showInMenu: false },
  { path: 'confirmar-email', component: ConfirmarEmailComponent, title: 'Confirmar email', showInMenu: false },

  { path: '', component: LayoutComponent,
    children: [
      { path: 'clients', component: ClientsListComponent },
      { path: 'clients/:id', component: ClientDetailComponent },
      { path: 'clients/nuevo', component: ClientCreateComponent, title: 'Nuevo cliente' },
      { path: 'diets', component: DietsListComponent, title: 'Dietas' },
      { path: 'diets/nuevo', component: DietCreateComponent, title: 'Nueva dieta' },
      { path: 'diets/:id', component: DietCreateComponent, title: 'Editar dieta' },
      { path: 'settings', component: SettingsComponent, title: 'Ajustes' }
          // aquí meterás también el resto de páginas:
          // { path: 'reports', component: ReportsComponent },
    ]
  },
  //{ path: 'login', component: LoginComponent },
  //{ path: 'crear-usuario', component: UserCreateComponent, title: 'Crear usuario', showInMenu: false },
  //{ path: 'reset-pwd', component: UserResetComponent, title: 'Crear usuario', showInMenu: false },
  //{ path: 'confirmar-email', component: ConfirmarEmailComponent, title: 'Confirmar email', showInMenu: false },
  { path: '**', redirectTo: '' } // fallback SPA → LayoutComponent
];

@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
