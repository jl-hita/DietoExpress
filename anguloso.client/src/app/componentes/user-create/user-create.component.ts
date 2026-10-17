import { Component, ChangeDetectionStrategy, NgZone, AfterViewInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule, FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { CommonModule } from '@angular/common';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../servicios/auth.service';
import { environment } from '../../../environments/environments';
import { LegalDocument, LegalService } from '../../servicios/legal.service';

declare const google: any;

interface Usuario {
  username: string;
  fullName?: string;
  passwordPlain: string;
  email: string;
  legalDocumentKey: string;
  legalDocumentVersion: number;
  legalDocumentSha256: string;
}

interface LoginRequest {
  username: string,
  password: string
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-user-create',
  templateUrl: './user-create.component.html',
  styleUrls: ['./user-create.component.css'],
    standalone: true,
  imports: [CommonModule, ReactiveFormsModule, FormsModule, RouterLink, MatSnackBarModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule],
})
// La creación de usuarios separa validación de formulario, selección de permisos y envío para evitar enviar estados parciales al backend.
export class UserCreateComponent implements AfterViewInit {
  private baseUrl = environment.apiUrl;
  form: FormGroup;
  loading = false;
  usuarioCreado = false;
  legalDocuments: LegalDocument[] = [];
  currentTerms: LegalDocument | null = null;
  termsAccepted = false;
  legalLoading = true;
  googleTermsAccepted = false;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private snackBar: MatSnackBar,
    private authService: AuthService,
    private router: Router,
    private legalService: LegalService,
    private ngZone: NgZone,
  ) {
    this.loadLegalDocuments();
    this.form = this.fb.group({
      username: ['', [Validators.required]],
      fullName: [''],
      password: ['', [Validators.required, Validators.minLength(12)]],
      email: ['', [Validators.required, Validators.email]],
      termsAccepted: [false, Validators.requiredTrue],
    });
  }

  ngAfterViewInit(): void {
    this.http.get<{ clientId: string }>(`${this.baseUrl}/auth/google-client-id`).subscribe({
      next: res => {
        if (!res.clientId) return;
        this.loadGoogleIdentityScript().then(() => {
          google.accounts.id.initialize({
            client_id: res.clientId,
            callback: (response: any) => this.handleCredentialResponse(response)
          });
          const button = document.getElementById('googleSignupBtn');
          if (button) google.accounts.id.renderButton(button, { theme: 'outline', size: 'large', text: 'signup_with', width: 300 });
        }).catch(err => {
          console.error('No se pudo cargar Google Identity Services', err);
          this.snackBar.open('No se pudo cargar el acceso con Google', 'Cerrar', { duration: 4000 });
        });
      },
      error: err => {
        console.error('No se pudo cargar la configuración de Google', err);
        this.snackBar.open('No se pudo cargar la configuración de Google', 'Cerrar', { duration: 4000 });
      }
    });
  }

  private loadGoogleIdentityScript(): Promise<void> {
    if (typeof google !== 'undefined' && google?.accounts?.id) return Promise.resolve();
    return new Promise((resolve, reject) => {
      let script = document.querySelector<HTMLScriptElement>('script[data-google-identity="true"]');
      if (script) {
        if (typeof google !== 'undefined' && google?.accounts?.id) { resolve(); return; }
        script.addEventListener('load', () => resolve(), { once: true });
        script.addEventListener('error', () => reject(new Error('Google Identity Services no disponible')), { once: true });
        return;
      }
      script = document.createElement('script');
      script.src = 'https://accounts.google.com/gsi/client';
      script.async = true;
      script.defer = true;
      script.dataset['googleIdentity'] = 'true';
      script.onload = () => resolve();
      script.onerror = () => reject(new Error('Google Identity Services no disponible'));
      document.head.appendChild(script);
    });
  }

  handleCredentialResponse(response: any): void {
    const idToken = response?.credential;
    if (!idToken) {
      this.snackBar.open('No se ha recibido la credencial de Google', 'Cerrar', { duration: 3000 });
      return;
    }
    this.googleLogin(idToken).subscribe({
      next: res => {
        this.authService.login(res);
        this.snackBar.open(`Bienvenido ${res.username}`, 'Cerrar', { duration: 3000 });
        const destination = res.role === 'superadmin' ? '/admin'
          : res.role === 'clinic_admin' ? '/clinic'
          : res.role === 'nutritionist' ? '/dashboard' : '/clients';
        this.ngZone.run(() => this.router.navigate([destination]));
      },
      error: err => {
        console.error('Error al autenticar con Google', err);
        if (err?.status === 428) {
          this.snackBar.open('Para crear una cuenta con Google debes aceptar las condiciones de contratación vigentes.', 'Cerrar', { duration: 5000 });
        } else {
          this.snackBar.open('Error al autenticar con Google', 'Cerrar', { duration: 4000 });
        }
      }
    });
  }

  googleLogin(idToken: string) {
    return this.http.post<any>(`${this.baseUrl}/auth/google`, {
      idToken,
      legalDocumentVersion: this.googleTermsAccepted ? this.currentTerms?.version : null,
      legalDocumentSha256: this.googleTermsAccepted ? this.currentTerms?.sha256 : null
    });
  }

  irALogin(): void {
    this.router.navigate(['/login']);
  }

  loadLegalDocuments(): void {
    this.legalService.getCurrent().subscribe({
      next: docs => {
        this.legalDocuments = docs;
        this.currentTerms = docs.find(d => d.key === 'saas_terms') ?? null;
        this.legalLoading = false;
      },
      error: () => {
        this.currentTerms = null;
        this.legalLoading = false;
      }
    });
  }

  crearUsuario() {
    if (this.form.invalid || !this.currentTerms) return;

    const usuario: Usuario = {
      username: this.form.value.username,
      fullName: this.form.value.fullName,
      passwordPlain: this.form.value.password,
      email: this.form.value.email,
      legalDocumentKey: this.currentTerms!.key,
      legalDocumentVersion: this.currentTerms!.version,
      legalDocumentSha256: this.currentTerms!.sha256
    };

    this.loading = true;

    this.http.put<{ exito: Boolean, mensaje: string }>(`${this.baseUrl}/api/auth/crearUser`, usuario).subscribe(bm => {
      if (bm.exito) {
        var loginRequest: LoginRequest = {
          username: this.form.value.username,
          password: this.form.value.password
        };

        //En lugar de intentar login, mostramos el mensaje de que se le ha enviado un email de confirmación
        this.usuarioCreado = true;

      } else {
        console.error(bm.mensaje);
        this.snackBar.open(bm.mensaje, 'Cerrar', { duration: 3000 });
        this.loading = false;
      }
    });
  }
}
