import { Component, EventEmitter, Input, Output } from '@angular/core';
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

  constructor(private fb: FormBuilder, private clientService: ClientService) {
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

    this.save.emit(client);
  }
}
