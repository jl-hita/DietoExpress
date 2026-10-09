import { RouterOutlet } from '@angular/router';
import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';

interface MaintenanceNotice {
  active: boolean;
  title: string;
  message: string;
  startedAtUtc: string;
}

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  standalone: true,
  imports: [CommonModule, RouterOutlet],
  styleUrl: './app.component.css'
})
// El componente raíz coordina el estado global mínimo de la aplicación; la lógica de negocio permanece en servicios y componentes especializados.
export class AppComponent {
  title = 'anguloso.client';

  // Polling de /api/maintenance desactivado para evitar peticiones periódicas desde todos los clientes.
  // El endpoint y MaintenanceNoticeService se conservan: DatabaseBackupService los utiliza para activar
  // un aviso persistente durante las restauraciones, cuando la aplicación/base de datos pueden estar
  // temporalmente indisponibles. Si se quiere volver a mostrar el banner en vivo, se puede reactivar
  // aquí el sondeo y asignar el resultado a maintenanceNotice.
  //
  // private maintenanceSubscription?: Subscription;
  // constructor(private http: HttpClient) {}
  // ngOnInit(): void {
  //   this.maintenanceSubscription = timer(0, 30000).pipe(
  //     switchMap(() => this.http.get<MaintenanceNotice>('/api/maintenance')),
  //     catchError(() => of<MaintenanceNotice>({ active: false, title: '', message: '', startedAtUtc: '' }))
  //   ).subscribe(notice => {
  //     this.maintenanceNotice = notice.active ? notice : undefined;
  //   });
  // }
  // ngOnDestroy(): void {
  //   this.maintenanceSubscription?.unsubscribe();
  // }

  // La propiedad se conserva para que la plantilla del banner siga siendo compatible si se reactiva el polling.
  maintenanceNotice?: MaintenanceNotice;
}
