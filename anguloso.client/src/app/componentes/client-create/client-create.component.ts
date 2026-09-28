import { Component, EventEmitter, Input, Output } from '@angular/core';
import { Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ClientDetail } from '../../modelos/client';
import { ClientService } from '../../servicios/client.service';

@Component({
  selector: 'app-client-create',
  standalone: false,
  templateUrl: './client-create.component.html',
  styleUrl: './client-create.component.css'
})
export class ClientCreateComponent {
  @Output() save = new EventEmitter<ClientDetail>();
  @Output() cancel = new EventEmitter<void>();

  form: FormGroup;
  canCreateClient = true;
  createClientReason = '';
  checkingCreatePermission = true;

  constructor(private fb: FormBuilder, private clientService: ClientService, private router: Router, private snack: MatSnackBar) {
    this.form = this.fb.group({
      fullName: ['', Validators.required],
      email: ['', [Validators.email]],
      phone: [''],
      birthDate: [''],
      gender: [''],
      notes: ['']
    });

    this.checkCreatePermission();
  }

  checkCreatePermission(): void {
    this.clientService.canCreateClient().subscribe({
      next: result => {
        this.canCreateClient = result.allowed;
        this.createClientReason = result.reason || '';
        this.checkingCreatePermission = false;
      },
      error: () => {
        this.canCreateClient = true;
        this.createClientReason = '';
        this.checkingCreatePermission = false;
      }
    });
  }

  submit() {
    if (!this.canCreateClient || this.checkingCreatePermission || this.form.invalid) return;

    const client: ClientDetail = {
      ...this.form.value,
      biometrics: [] // siempre vacío al crear
    };

    this.clientService.createClient(client).subscribe({
      next: (res: any) => {
        const newId = res?.id;
        this.snack.open('Cliente creado', 'Cerrar', { duration: 2000 });

        if (newId) {
          this.router.navigate(['/clients', newId]);
        } else {
          this.router.navigate(['/clients']);
        }
      },
      error: (error) => {
        const message = error?.error?.message || error?.error || 'Error al crear el cliente';
        this.snack.open(typeof message === 'string' ? message : 'Error al crear el cliente', 'Cerrar', { duration: 4000 });
      }
    });
  }
}
