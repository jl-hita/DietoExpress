import { HttpClient } from '@angular/common/http';
import { Component } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { AuthService } from '../../servicios/auth.service';
import { environment } from '../../../environments/environments';

interface PasswordResetRequest {
  username: string,
  email: string
}

@Component({
  selector: 'app-user-reset',
  standalone: false,
  templateUrl: './user-reset.component.html',
  styleUrl: './user-reset.component.css'
})
export class UserResetComponent {
  private baseUrl = environment.apiUrl;
  form: FormGroup;
  loading = false;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient,
    private authService: AuthService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {
    this.form = this.fb.group({
      username: ['', Validators.required],
      email: ['', Validators.required]
    });
  }

  resetPassword() {
    var pwdRequest: PasswordResetRequest = {
      username: this.form.value.username,
      email: this.form.value.email
    }

    this.http.post<{ exito: boolean, mensaje: string }>(`${this.baseUrl}/auth/enviarReset`, pwdRequest).subscribe(bm => {
      this.snackBar.open(`${bm.mensaje}`, 'Cerrar', { duration: 3000 });
    });
  }
}
