import { Component, EventEmitter, Output } from '@angular/core';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { FormBuilder, FormGroup, Validators, ReactiveFormsModule } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatOptionModule, MatNativeDateModule } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';
import { ClientDetail } from '../../modelos/client';
import { AddressSuggestion } from '../../modelos/address';
import { AddressAutocompleteComponent } from '../address-autocomplete/address-autocomplete.component';
import { ClientService } from '../../servicios/client.service';

@Component({
  selector: 'app-client-create',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatSnackBarModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatOptionModule, MatDatepickerModule, MatNativeDateModule, MatButtonModule, MatIconModule, AddressAutocompleteComponent],
  templateUrl: './client-create.component.html',
  styleUrl: './client-create.component.css'
})
// Este formulario coordina datos personales, biometría y preferencias; mantiene la validación y el payload final separados de la vista.
export class ClientCreateComponent {
  @Output() save = new EventEmitter<ClientDetail>();
  @Output() cancel = new EventEmitter<void>();

  form: FormGroup;

  private toIsoDate(value: Date | string | null | undefined): string | null {
    if (!value) return null;
    if (typeof value === 'string') return value.slice(0, 10);
    const year = value.getFullYear();
    const month = String(value.getMonth() + 1).padStart(2, '0');
    const day = String(value.getDate()).padStart(2, '0');
    return `${year}-${month}-${day}`;
  }
  canCreateClient = true;
  createClientReason = '';
  checkingCreatePermission = true;

  constructor(private fb: FormBuilder, private clientService: ClientService, private router: Router, private snack: MatSnackBar) {
    this.form = this.fb.group({
      fullName: ['', Validators.required],
      email: ['', [Validators.email]],
      phone: [''],
      address: [''],
      postalCode: [''],
      city: [''],
      province: [''],
      country: ['España'],
      latitude: [null],
      longitude: [null],
      birthDate: [''],
      gender: [''],
      notes: ['']
    });
    this.checkCreatePermission();
  }

  // El límite del plan se consulta antes de habilitar la creación, pero el backend debe volver a comprobarlo al guardar.
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

  /** Mantiene los campos estructurados sincronizados cuando el usuario selecciona una sugerencia. */
  applyAddressSuggestion(suggestion: AddressSuggestion): void {
    this.form.patchValue({
      address: suggestion.displayName,
      postalCode: suggestion.postalCode,
      city: suggestion.city,
      province: suggestion.province,
      country: suggestion.country || 'España',
      latitude: suggestion.latitude ?? null,
      longitude: suggestion.longitude ?? null
    });
  }

  /** Permite editar la dirección manualmente después de una selección sin perder el valor escrito. */
  onAddressValueChanged(value: string): void {
    this.form.patchValue({ address: value, latitude: null, longitude: null }, { emitEvent: false });
  }

  // La fecha se normaliza antes de construir el DTO para no enviar al API el desfase introducido por el DatePicker.
  submit() {
    if (!this.canCreateClient || this.checkingCreatePermission || this.form.invalid) return;
    const formValue = this.form.value;
    const client: ClientDetail = {
      ...formValue,
      birthDate: this.toIsoDate(formValue.birthDate) ?? undefined,
      biometrics: []
    };
    this.clientService.createClient(client).subscribe({
      next: (res: any) => {
        const newId = res?.id;
        this.snack.open('Cliente creado', 'Cerrar', { duration: 2000 });
        this.router.navigate(newId ? ['/clients', newId] : ['/clients']);
      },
      error: (error) => {
        const message = error?.error?.message || error?.error?.title || error?.error || 'Error al crear el cliente';
        console.error('Error al crear cliente', error);
        this.snack.open(typeof message === 'string' ? message : 'Error al crear el cliente', 'Cerrar', { duration: 5000 });
      }
    });
  }
}