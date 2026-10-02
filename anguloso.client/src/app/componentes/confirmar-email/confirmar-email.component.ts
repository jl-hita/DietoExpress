import { CommonModule } from '@angular/common';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { Component } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { AuthService } from '../../servicios/auth.service';
import { HttpBackend, HttpClient } from '@angular/common/http';
import { MatSnackBar } from '@angular/material/snack-bar';
import { environment } from '../../../environments/environments';

interface LoginRequestToken {
  email: string,
  token: string
}

@Component({
  selector: 'app-confirmar-email',
    standalone: true,
  imports: [CommonModule, MatSnackBarModule, MatProgressSpinnerModule],
  templateUrl: './confirmar-email.component.html',
  styleUrl: './confirmar-email.component.css'
})
// Documentación: este componente coordina estado local, validación y llamadas asíncronas; la vista solo refleja ese estado.
export class ConfirmarEmailComponent {
  private baseUrl = environment.apiUrl;
  estado: 'cargando' | 'ok' | 'error' = 'cargando';
  mensaje: string = '';

  constructor(
    private route: ActivatedRoute,
    private authService: AuthService,
    private http: HttpClient,
    private router: Router,
    private snackBar: MatSnackBar
  ) { }

  ngOnInit() {
    const token = this.route.snapshot.queryParamMap.get('token');
    //const email = this.route.snapshot.queryParamMap.get('email');

    //if (!token || !email) {
    if (!token) {
      this.estado = 'error';
      this.mensaje = 'Token o email no válido.';
      return;
    }

    //this.confirmar(email, token);
    this.confirmar(token);
  }

  //confirmar(email: string, token: string) {
  confirmar(token: string) {
    //this.http.put<any>(`${this.baseUrl}/auth/confirmarEmail`, JSON.stringify(token), { headers: { 'Content-Type': 'application/json' } }).subscribe({
    this.http.get<any>(`${this.baseUrl}/auth/confirmarEmail`, { headers: { 'Content-Type': 'application/json' }, params: { token: token } }).subscribe({
      next: (res) => {
        // La confirmación del email no autentica automáticamente.
        // Esto evita convertir la visita automática de un escáner de correo en una sesión.
        this.snackBar.open("Email confirmado. Ya puedes iniciar sesión.", 'Cerrar', { duration: 4000 });
        this.router.navigate(['/login']);
      },
      error: (r) => {
        this.estado = 'error';
        //this.mensaje = 'Error de red al confirmar el email.';
        this.mensaje = r?.error || 'No se ha podido confirmar el email. El enlace puede haber caducado o no ser válido.';
      }
    });
  }

  /*
  reintentar() {
    window.location.reload();
  }
  */
}
