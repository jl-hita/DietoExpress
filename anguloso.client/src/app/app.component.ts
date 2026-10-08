import { RouterOutlet } from '@angular/router';
import { Component, OnDestroy, OnInit } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Subscription, timer, of } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';

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
  imports: [RouterOutlet],
  styleUrl: './app.component.css'
})
// El componente raíz coordina el estado global mínimo de la aplicación; la lógica de negocio permanece en servicios y componentes especializados.
export class AppComponent implements OnInit, OnDestroy {
  title = 'anguloso.client';
  maintenanceNotice?: MaintenanceNotice;
  private maintenanceSubscription?: Subscription;

  constructor(private http: HttpClient) {}

  ngOnInit(): void {
    this.maintenanceSubscription = timer(0, 2000).pipe(
      switchMap(() => this.http.get<MaintenanceNotice>('/api/maintenance')),
      catchError(() => of<MaintenanceNotice>({ active: false, title: '', message: '', startedAtUtc: '' }))
    ).subscribe(notice => {
      this.maintenanceNotice = notice.active ? notice : undefined;
    });
  }

  ngOnDestroy(): void {
    this.maintenanceSubscription?.unsubscribe();
  }
}
