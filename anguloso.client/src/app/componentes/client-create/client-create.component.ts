import { MatSnackBar } from '@angular/material/snack-bar';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatOptionModule, MatNativeDateModule } from '@angular/material/core';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { Router } from '@angular/router';
import { ClientDetail } from '../../modelos/client';
import { ClientService } from '../../servicios/client.service';

@Component({
  selector: 'app-client-create',
    standalone: true,
  imports: [CommonModule, ReactiveFormsModule, MatSnackBarModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatOptionModule, MatDatepickerModule, MatNativeDateModule, MatButtonModule, MatIconModule],
  templateUrl: './client-create.component.html',
  styleUrl: './client-create.component.css'
})
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

    const formValue = this.form.value;
    const client: ClientDetail = {
      ...formValue,
      birthDate: this.toIsoDate(formValue.birthDate) ?? undefined,
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
        const message = error?.error?.message || error?.error?.title || error?.error || 'Error al crear el cliente';
        console.error('Error al crear cliente', error);
        this.snack.open(typeof message === 'string' ? message : 'Error al crear el cliente', 'Cerrar', { duration: 5000 });
      }
    });
  }
}
