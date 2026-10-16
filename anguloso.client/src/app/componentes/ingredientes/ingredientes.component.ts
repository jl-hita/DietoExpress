import { CommonModule } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Component, ChangeDetectionStrategy, signal} from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatOptionModule } from '@angular/material/core';
import { environment } from '../../../environments/environments';

interface Ingrediente {
  id?: number;           // Opcional, porque al crear uno nuevo todavía no tiene ID
  nombre: string;
  comprado?: boolean;    // Opcional, default false
  descripcion?: string;  // Opcional
  cantidad: number;
  unidad: string;
  id_usuario?: number;   // Opcional
  id_receta?: number;    // Opcional
}

interface MutationResponse { mensaje?: string; }

// Documentación: este componente coordina estado local, validación y llamadas asíncronas; la vista solo refleja ese estado.
@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-ingredientes',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatSelectModule, MatFormFieldModule, MatOptionModule],
  templateUrl: './ingredientes.component.html',
  styleUrl: './ingredientes.component.css'
})
export class IngredientesComponent {
  private readonly ingredientesState = signal<Ingrediente[]>([]);
  get ingredientes(): Ingrediente[] { return this.ingredientesState(); }
  set ingredientes(value: Ingrediente[]) { this.ingredientesState.set(value); }
  formIngrediente: FormGroup;
  private readonly cargandoState = signal(false);
  get cargando(): boolean { return this.cargandoState(); }
  set cargando(value: boolean) { this.cargandoState.set(value); }
  private readonly mensajeState = signal('');
  get mensaje(): string { return this.mensajeState(); }
  set mensaje(value: string) { this.mensajeState.set(value); }

  unidades = [
    { value: 'ud', label: 'Unidades' },
    //{ value: 'mg', label: 'Miligramos' },
    { value: 'g', label: 'Gramos' },
    { value: 'kg', label: 'Kilogramos' },
    { value: 'ml', label: 'Mililitros' },
    { value: 'l', label: 'Litros' }
  ];

  private apiUrl = `${environment.apiUrl}/ingrediente`;

  constructor(
    private fb: FormBuilder,
    private http: HttpClient
  ) {
    this.formIngrediente = this.fb.group({
      nombre: ['', Validators.required],
      descripcion: [''],
      cantidad: [1, [Validators.required, Validators.min(0.1)]],
      unidad: ['ud', Validators.required]
    });
  }


  ngOnInit() {
    this.cargarIngredientes();
  }

  cargarIngredientes() {
    this.cargando = true;
    this.http.get<Ingrediente[]>(`${this.apiUrl}/getIngredientes`).subscribe({
      next: (lista) => {
        this.ingredientes = lista;
        this.cargando = false;
      },
      error: (err) => {
        console.error(err);
        this.cargando = false;
      }
    });
  }

  agregarIngrediente() {
    if (this.formIngrediente.invalid) return;
    const nuevo = this.formIngrediente.value;
    this.http.put<MutationResponse>(`${this.apiUrl}/addIngrediente`, nuevo).subscribe({
      next: (res: MutationResponse) => {
        this.mensaje = res.mensaje || 'Ingrediente guardado';
        this.formIngrediente.reset({ cantidad: 1, unidad: 'ud' });
        this.cargarIngredientes();
      },
      error: (err) => {
        console.error(err);
        this.mensaje = 'Error guardando ingrediente';
      }
    });
  }

  marcarComprado(ing: Ingrediente) {
    // Aquí luego llamarás al endpoint correspondiente
    const updated = { ...ing, comprado: !ing.comprado };
    this.ingredientes = this.ingredientes.map(item => item === ing ? updated : item);

    this.http.put<MutationResponse>(`${this.apiUrl}/marcarCompradoIngredientes`, updated).subscribe({
      next: (res: MutationResponse) => {
        this.mensaje = res.mensaje || 'Ingrediente guardado';
        this.formIngrediente.reset({ cantidad: 1, unidad: 'ud' });
        this.cargarIngredientes();
      },
      error: (err) => {
        console.error(err);
        this.mensaje = 'Error guardando ingrediente';
      }
    });
  }

  borrarIngrediente(ing: Ingrediente) {
    this.ingredientes = this.ingredientes.filter(i => i.id !== ing.id);

    this.http.put<MutationResponse>(`${this.apiUrl}/delIngredientes`, ing).subscribe({
      next: (res: MutationResponse) => {
        this.mensaje = res.mensaje || 'Ingrediente guardado';
        this.formIngrediente.reset({ cantidad: 1, unidad: 'ud' });
        this.cargarIngredientes();
      },
      error: (err) => {
        console.error(err);
        this.mensaje = 'Error guardando ingrediente';
      }
    });
  }
}
