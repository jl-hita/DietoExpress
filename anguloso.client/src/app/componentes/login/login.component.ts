import { ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { Component, NgZone } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { AuthService } from '../../servicios/auth.service';
import { environment } from '../../../environments/environments';

declare const google: any;

@Component({
  selector: 'app-login',
    standalone: true,
  imports: [ReactiveFormsModule, RouterLink, MatSnackBarModule, MatFormFieldModule, MatInputModule, MatIconModule, MatButtonModule],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent {
  private baseUrl = environment.apiUrl;
  form: FormGroup;
  loading = false;
  private googleClientId: string | null = null;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private authService: AuthService,
    private router: Router,
    private snackBar: MatSnackBar,
    private ngZone: NgZone
  ) {
    this.form = this.fb.group({
      username: ['', Validators.required],
      password: ['', Validators.required]
    });
  }

  ngAfterViewInit(): void {
    this.http.get<{ clientId: string }>(`${this.baseUrl}/auth/google-client-id`).subscribe({
      next: (res) => {
        this.googleClientId = res.clientId;
        google.accounts.id.initialize({
          client_id: res.clientId,
          callback: (response: any) => this.handleCredentialResponse(response)
        });

        google.accounts.id.renderButton(
          document.getElementById("googleBtn"),
          { theme: "outline", size: "large" }
        );
      },
      error: (err) => {
        console.error('No se pudo cargar la configuración de Google', err);
        this.snackBar.open('No se pudo cargar la configuración de Google', 'Cerrar', { duration: 4000 });
      }
    });
  }

  handleCredentialResponse(response: any) {
    const idToken = response?.credential;
    if (!idToken) {
      this.snackBar.open('Error Google login', 'Cerrar', { duration: 3000 });
      return;
    }

    this.googleLogin(idToken).subscribe({
      next: (res) => {
        this.authService.login(res);
        this.snackBar.open(`Bienvenido ${res.username}`, 'Cerrar', { duration: 3000 });
        this.ngZone.run(() => this.router.navigate(['/clients'])); // navegar en Angular zone
      },
      error: (err) => {
        console.error(err);
        this.snackBar.open('Error al autenticar con Google', 'Cerrar', { duration: 4000 });
      }
    });
  }

  //Para el login con nombre de usuario y contraseña
  login() {
    if (this.form.invalid) return;

    this.loading = true;
    this.http.post<any>(`${this.baseUrl}/auth/login`, this.form.value)
    .subscribe({
      next: (res) => {
        this.authService.login(res); // la sesión se mantiene en la cookie HttpOnly
        console.log("ID -> " + res.id);
        console.log("User -> " + res.username);
        console.log("Role -> " + res.role);
        this.snackBar.open("Bienvenido " + res.username, 'Cerrar', { duration: 3000 });
        this.router.navigate(['/clients']); // los usuarios FREE serán enviados al onboarding por el guard
      },
      error: (err) => {
        console.error(err);
        this.snackBar.open(err.error || 'Usuario o contraseña incorrectos', 'Cerrar', { duration: 3000 });
        this.loading = false;
      }
    });
  }

  googleLogin(idToken: string) {
    return this.http.post<any>(`${this.baseUrl}/auth/google`, { idToken });
  }


  volverInicio() {
    this.router.navigate(['/']);
  }

  navegarCrearUser() {
    this.router.navigate(['crear-usuario']);
  }

  navegarPwdReset() {
    this.router.navigate(['reset-pwd']);
  }
}
