import { RouterOutlet } from '@angular/router';
import { Component } from '@angular/core';

@Component({
  selector: 'app-root',
  templateUrl: './app.component.html',
  standalone: true,
  imports: [RouterOutlet],
  styleUrl: './app.component.css'
})
// El componente raíz coordina el estado global mínimo de la aplicación; la lógica de negocio permanece en servicios y componentes especializados.
export class AppComponent {
  title = 'anguloso.client';
}
