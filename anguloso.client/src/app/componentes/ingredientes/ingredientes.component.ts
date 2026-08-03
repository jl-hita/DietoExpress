import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Component } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { MatSelectModule } from '@angular/material/select';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatOptionModule } from '@angular/material/core';

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

@Component({
  selector: 'app-ingredientes',
  standalone: false,
  templateUrl: './ingredientes.component.html',
  styleUrl: './ingredientes.component.css'
})
export class IngredientesComponent {
  ingredientes: Ingrediente[] = [];
  formIngrediente: FormGroup;
  cargando = false;
  mensaje = '';

  unidades = [
    { value: 'ud', label: 'Unidades' },
    //{ value: 'mg', label: 'Miligramos' },
    { value: 'g', label: 'Gramos' },
    { value: 'kg', label: 'Kilogramos' },
    { value: 'ml', label: 'Mililitros' },
    { value: 'l', label: 'Litros' }
  ];

  private apiUrl = 'http://localhost:5125/api/ingrediente';

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
    /*
    var ingrediente: Ingrediente = {
      nombre: this.formIngrediente.value.nombre,
      cantidad: this.formIngrediente.value.cantidad,
      unidad: this.formIngrediente.value.unidad,
    }
    console.log('API URL:', this.apiUrl);
    console.log(this.formIngrediente.value);
    */
    const nuevo = this.formIngrediente.value;
    this.http.put(`${this.apiUrl}/addIngrediente`, nuevo).subscribe({
    //this.http.put(`${this.apiUrl}/addIngrediente`, ingrediente).subscribe({
      next: (res: any) => {
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
    ing.comprado = !ing.comprado;

    const nuevo = this.formIngrediente.value;
    this.http.put(`${this.apiUrl}/marcarCompradoIngredientes`, ing).subscribe({
      next: (res: any) => {
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
    // Aquí luego llamarás al endpoint delete
    this.ingredientes = this.ingredientes.filter(i => i.id !== ing.id);

    this.http.put(`${this.apiUrl}/delIngredientes`, ing).subscribe({
      next: (res: any) => {
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
