import { Component } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatError } from '@angular/material/form-field';
import { MatLabel } from '@angular/material/form-field';
import { Router } from '@angular/router';
import { AuthService } from '../../servicios/auth.service';
import { environment } from '../../../environments/environments';

interface Usuario {
  username: string;
  fullName?: string;
  passwordPlain: string;
  email: string;
}

interface LoginRequest {
  username: string,
  password: string
}

@Component({
  selector: 'app-user-create',
  templateUrl: './user-create.component.html',
  styleUrls: ['./user-create.component.css'],
  standalone: false
})
export class UserCreateComponent {
  private baseUrl = environment.apiUrl;
  form: FormGroup;
  loading = false;
  usuarioCreado = false;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private snackBar: MatSnackBar,
    private authService: AuthService,
    private router: Router,
  ) {
    this.form = this.fb.group({
      username: ['', [Validators.required]],
      fullName: [''],
      password: ['', [Validators.required, Validators.minLength(6)]],
      email: ['', [Validators.required, Validators.email]],
    });
  }

  crearUsuario() {
    if (this.form.invalid) return;

    const usuario: Usuario = {
      username: this.form.value.username,
      fullName: this.form.value.fullName,
      passwordPlain: this.form.value.password,
      email: this.form.value.email
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
