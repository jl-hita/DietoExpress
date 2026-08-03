import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { Client } from '../../modelos/client';

@Component({
  selector: 'app-client-create',
  standalone: false,
  templateUrl: './client-create.component.html',
  styleUrl: './client-create.component.css'
})
export class ClientCreateComponent {
  @Output() save = new EventEmitter<Client>();
  @Output() cancel = new EventEmitter<void>();

  form: FormGroup;

  constructor(private fb: FormBuilder) {
    this.form = this.fb.group({
      fullName: ['', Validators.required],
      email: ['', [Validators.email]],
      phone: [''],
      birthDate: [''],
      gender: [''],
      notes: ['']
    });
  }

  submit() {
    if (this.form.invalid) return;

    const client: Client = {
      ...this.form.value,
      biometrics: [] // siempre vacío al crear
    };

    this.save.emit(client);
  }
}
