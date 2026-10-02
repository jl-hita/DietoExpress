import { CommonModule } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { Location } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { environment } from '../../../environments/environments';

@Component({
  selector: 'app-user-reset',
    standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatSnackBarModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  templateUrl: './user-reset.component.html',
  styleUrl: './user-reset.component.css'
})
export class UserResetComponent implements OnInit {
  private baseUrl = environment.apiUrl;
  requestForm: FormGroup;
  resetForm: FormGroup;
  resetToken: string | null = null;
  loading = false;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private route: ActivatedRoute,
    private router: Router,
    private location: Location,
    private snackBar: MatSnackBar
  ) {
    this.requestForm = this.fb.group({
      email: ['', [Validators.required, Validators.email]]
    });

    this.resetForm = this.fb.group({
      newPassword: ['', [Validators.required, Validators.minLength(12)]],
      newPasswordRep: ['', [Validators.required, Validators.minLength(12)]]
    });
  }

  ngOnInit(): void {
    const token = this.route.snapshot.queryParamMap.get('token');

    if (token) {
      this.resetToken = token;
      // El token queda únicamente en memoria; no lo mantenemos en la URL del navegador.
      this.location.replaceState('/reset-pwd');
    }
  }

  requestReset(): void {
    if (this.requestForm.invalid) return;

    this.loading = true;
    this.http.post<{ exito: boolean, mensaje: string }>(
      `${this.baseUrl}/auth/enviarReset`,
      { email: this.requestForm.value.email }
    ).subscribe({
      next: bm => {
        this.loading = false;
        this.snackBar.open(
          bm.mensaje || 'Si el email corresponde a una cuenta, recibirás instrucciones.',
          'Cerrar',
          { duration: 5000 }
        );
      },
      error: () => {
        this.loading = false;
        // No revelamos desde el cliente si la cuenta existe.
        this.snackBar.open(
          'Si el email corresponde a una cuenta, recibirás instrucciones.',
          'Cerrar',
          { duration: 5000 }
        );
      }
    });
  }

  resetPassword(): void {
    if (!this.resetToken || this.resetForm.invalid) return;

    const newPassword = this.resetForm.value.newPassword;
    const newPasswordRep = this.resetForm.value.newPasswordRep;

    if (newPassword !== newPasswordRep) {
      this.snackBar.open('Las contraseñas no coinciden.', 'Cerrar', { duration: 3000 });
      return;
    }

    this.loading = true;
    this.http.put<{ exito: boolean, mensaje: string }>(
      `${this.baseUrl}/auth/resetPassword`,
      { token: this.resetToken, newPassword }
    ).subscribe({
      next: bm => {
        this.loading = false;
        this.snackBar.open(bm.mensaje, 'Cerrar', { duration: 4000 });

        if (bm.exito) {
          this.resetToken = null;
          this.resetForm.reset();
          this.router.navigate(['/login']);
        }
      },
      error: err => {
        this.loading = false;
        this.snackBar.open(
          err?.error?.mensaje || err?.error || 'No se pudo restablecer la contraseña.',
          'Cerrar',
          { duration: 4000 }
        );
      }
    });
  }
}
