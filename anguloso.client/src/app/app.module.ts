import { /*HTTP_INTERCEPTORS,*/ HttpClientModule, HTTP_INTERCEPTORS } from '@angular/common/http';
import { NgModule } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';

import { AppRoutingModule } from './app-routing.module';
import { AppComponent } from './app.component';
import { UserCreateComponent } from './componentes/user-create/user-create.component';
import { ReactiveFormsModule } from '@angular/forms';
import { BrowserAnimationsModule } from '@angular/platform-browser/animations';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { NavbarComponent } from './componentes/navbar/navbar.component';
import { MatToolbarModule } from '@angular/material/toolbar';
import { LoginComponent } from './componentes/login/login.component';
import { UserResetComponent } from './componentes/user-reset/user-reset.component';
import { AuthInterceptor } from './auth.interceptor';
import { MatSelectModule } from '@angular/material/select';
import { MatOptionModule } from '@angular/material/core';
import { MatIconModule } from '@angular/material/icon';
import { MatCardModule } from '@angular/material/card';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ConfirmarEmailComponent } from './componentes/confirmar-email/confirmar-email.component';
import { FormsModule } from '@angular/forms';
import { MatListModule } from '@angular/material/list';
import { ClientDetailComponent } from './componentes/client-detail/client-detail.component';
import { MatTabsModule } from '@angular/material/tabs';
import { ClientsListComponent } from './componentes/clients-list/clients-list.component';
import { LayoutComponent } from './componentes/layout/layout.component';
import { SidebarComponent } from './componentes/sidebar/sidebar.component';
import { ClientCreateComponent } from './componentes/client-create/client-create.component';
import { DietsListComponent } from './componentes/diets-list/diets-list.component';
import { DietCreateComponent } from './componentes/diet-create/diet-create.component';

@NgModule({
  declarations: [
    AppComponent,
    UserCreateComponent,
    NavbarComponent,
    LoginComponent,
    UserResetComponent,
    ConfirmarEmailComponent,
    ClientCreateComponent
  ],
  imports: [
    BrowserModule,
    AppRoutingModule, MatSnackBarModule,
    BrowserAnimationsModule, 
    ReactiveFormsModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatToolbarModule,
    MatSelectModule,
    MatOptionModule,
    HttpClientModule,
    MatIconModule,
    MatCardModule,
    MatProgressBarModule,
    MatProgressSpinnerModule,
    FormsModule,
    MatListModule,
    MatTabsModule,
    ClientDetailComponent,
    ClientsListComponent,
    LayoutComponent,
    SidebarComponent,
    DietsListComponent,
    DietCreateComponent
  ],
  providers: [
    { provide: HTTP_INTERCEPTORS, useClass: AuthInterceptor, multi: true }
  ],
  bootstrap: [AppComponent]
})
export class AppModule { }
