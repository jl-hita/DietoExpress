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
  standalone: false,
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css']
})
export class LoginComponent {
  private baseUrl = environment.apiUrl;
  form: FormGroup;
  loading = false;

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
    // Asegúrate de que window.google esté disponible
    const clientId = (window as any).__env?.GOOGLE_CLIENT_ID || 'TU_CLIENT_ID.apps.googleusercontent.com';
    //console.log("Client ID google: " + clientId);

    google.accounts.id.initialize({
      client_id: clientId,
      callback: (response: any) => this.handleCredentialResponse(response)
    });

    google.accounts.id.renderButton(
      document.getElementById("googleBtn"),
      { theme: "outline", size: "large" }
    );
  }

  handleCredentialResponse(response: any) {
    const idToken = response?.credential;
    if (!idToken) {
      this.snackBar.open('Error Google login', 'Cerrar', { duration: 3000 });
      return;
    }

    this.googleLogin(idToken).subscribe({
      next: (res) => {
        this.authService.login(res.token);
        this.snackBar.open(`Bienvenido ${res.username}`, 'Cerrar', { duration: 3000 });
        this.ngZone.run(() => this.router.navigate(['/'])); // navegar en Angular zone
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
        this.authService.login(res.token); // guardamos el token
        console.log("ID -> " + res.id);
        console.log("User -> " + res.username);
        console.log("Role -> " + res.role);
        this.snackBar.open("Bienvenido " + res.username, 'Cerrar', { duration: 3000 });
        this.router.navigate(['']); // ruta principal tras login
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

  navegarCrearUser() {
    this.router.navigate(['crear-usuario']);
  }

  navegarPwdReset() {
    this.router.navigate(['reset-pwd']);
  }
}
