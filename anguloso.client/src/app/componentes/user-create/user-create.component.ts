import { Component } from '@angular/core';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
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
  selector: 'app-user-create',
  templateUrl: './user-create.component.html',
  styleUrls: ['./user-create.component.css'],
    standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, MatSnackBarModule, MatFormFieldModule, MatInputModule, MatButtonModule, MatIconModule],
})
// La creación de usuarios separa validación de formulario, selección de permisos y envío para evitar enviar estados parciales al backend.
export class UserCreateComponent {
  private baseUrl = environment.apiUrl;
  form: FormGroup;
  loading = false;
  usuarioCreado = false;
  legalDocuments: LegalDocument[] = [];
  currentTerms: LegalDocument | null = null;
  termsAccepted = false;
  legalLoading = true;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private snackBar: MatSnackBar,
    private authService: AuthService,
    private router: Router,
    private legalService: LegalService,
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
