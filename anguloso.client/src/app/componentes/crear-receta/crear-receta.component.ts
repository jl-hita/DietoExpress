import { Component } from '@angular/core';
import { FormBuilder, FormGroup, Validators, FormArray } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { AuthService } from '../../servicios/auth.service';
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

interface Receta {
  id?: number;           // Opcional, porque al crear una receta todavía no tiene ID
  nombre: string;
  descripcion?: string;  // Opcional
  receta?: string;       // El texto de la receta
  fecha_creacion?: string | Date; // Puede venir como string ISO o Date
  id_usuario: number;
}

@Component({
  selector: 'app-crear-receta',
  templateUrl: './crear-receta.component.html',
  styleUrls: ['./crear-receta.component.css'],
  standalone: false
})
export class CrearRecetaComponent {
  private apiUrl = environment.apiUrl +'receta/';
  formReceta: FormGroup;
  mensaje: string | null = null;
  cargando = false;

  unidades = [
    { value: 'unidades', label: 'Unidades' },
    { value: 'g', label: 'Gramos' },
    { value: 'mg', label: 'Miligramos' },
    { value: 'kg', label: 'Kilogramos' },
    { value: 'ml', label: 'Mililitros' },
    { value: 'l', label: 'Litros' }
  ];

  constructor(private fb: FormBuilder, private http: HttpClient, private authService: AuthService) {
    this.formReceta = this.fb.group({
      nombre: ['', Validators.required],
      descripcion: [''],
      receta: ['', Validators.required],
      ingredientes: this.fb.array([])
    });
  }

  get ingredientes(): FormArray {
    return this.formReceta.get('ingredientes') as FormArray;
  }

  agregarIngrediente() {
    const nuevo = this.fb.group({
      nombre: ['', Validators.required],
      cantidad: [1, [Validators.required, Validators.min(1)]],
      unidad: ['unidades', Validators.required],
      descripcion: ['']
    });
    this.ingredientes.push(nuevo);
  }

  eliminarIngrediente(i: number) {
    this.ingredientes.removeAt(i);
  }

  guardarReceta() {
    if (this.formReceta.invalid) return;
    this.cargando = true;

    /*
    var receta: Receta = {
      nombre: this.formReceta.value.nombre,
      descripcion: this.formReceta.value.descripcion,
      receta: this.formReceta.value.receta,
      id_usuario: 0
    };

    const ingredientes = this.formReceta.value.ingredientes;
    const user = this.authService.getUser();
    console.log("User -> " + user);

    this.http.put<{ exito: boolean, mensaje: string }>(this.apiUrl+'addRecetaSolo', receta).subscribe(
      bm => {
        if (bm.exito) console.log("id receta -> " + bm.mensaje);
        else console.log(bm.mensaje);
      },
      err => {
        console.error("Error guardando receta", err);
      }
    );
    */
    const body = {
      receta: {
        nombre: this.formReceta.value.nombre,
        descripcion: this.formReceta.value.descripcion,
        receta: this.formReceta.value.receta
      },
      ingredientes: this.formReceta.value.ingredientes
    };

    this.http.put<any>(this.apiUrl + 'addReceta', body).subscribe({
      next: res => {
        this.mensaje = res.mensaje;
        this.cargando = false;
        if (res.exito) this.formReceta.reset();
      },
      error: err => {
        console.error(err);
        this.mensaje = 'Error al guardar la receta';
        this.cargando = false;
      }
    });
  }
}
