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
  standalone: false,
  templateUrl: './confirmar-email.component.html',
  styleUrl: './confirmar-email.component.css'
})
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
        console.log("ID -> " + res.id);
        console.log("User -> " + res.username);
        console.log("Role -> " + res.role);
        this.authService.login(res.token); // guardamos el token
        this.snackBar.open("Bienvenido " + res.username, 'Cerrar', { duration: 3000 });
        this.router.navigate(['']); // ruta principal tras login
      },
      error: (r) => {
        this.estado = 'error';
        //this.mensaje = 'Error de red al confirmar el email.';
        this.mensaje = 'Error en login: ' + r;
      }
    });
  }

  /*
  reintentar() {
    window.location.reload();
  }
  */
}
