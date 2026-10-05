import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Subject, forkJoin, takeUntil } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ClientSpecialization, Specialization, SpecializationService } from '../../servicios/specialization.service';

@Component({
  selector: 'app-client-specializations',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatIconModule,
    MatProgressSpinnerModule
  ],
  templateUrl: './client-specializations.component.html',
  styleUrl: './client-specializations.component.css'
})
export class ClientSpecializationsComponent implements OnInit, OnDestroy {
  private readonly service = inject(SpecializationService);
  private readonly route = inject(ActivatedRoute);
  private readonly destroy$ = new Subject<void>();

  clientId = 0;
  clientSpecializations: ClientSpecialization[] = [];
  catalog: Specialization[] = [];
  selected = new Set<number>();
  notes: Record<number, string> = {};
  loading = true;
  saving = false;
  error = '';
  success = '';

  ngOnInit(): void {
    this.clientId = Number(this.route.snapshot.paramMap.get('id'));
    if (!this.clientId) {
      this.error = 'Paciente no válido.';
      this.loading = false;
      return;
    }

    // Catálogo y selección se cargan juntos para evitar una UI intermedia con estados inconsistentes.
    forkJoin({
      catalog: this.service.getCatalog(),
      client: this.service.getClientSpecializations(this.clientId)
    })
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: ({ catalog, client }) => {
          this.catalog = catalog;
          this.clientSpecializations = client;
          this.selected = new Set(client.map(x => x.specializationId));
          this.notes = Object.fromEntries(
            client.map(x => [x.specializationId, x.notes ?? ''])
          );
          this.loading = false;
        },
        error: () => {
          this.error = 'No se han podido cargar las especializaciones.';
          this.loading = false;
        }
      });
  }

  get categories(): string[] {
    return [...new Set(this.catalog.map(x => x.category))];
  }

  getCategoryLabel(category: string): string {
    switch (category) {
      case 'dietary': return 'Patrones alimentarios';
      case 'sports': return 'Nutrición deportiva';
      case 'clinical': return 'Condiciones clínicas';
      case 'life_stage': return 'Etapas vitales';
      default: return category;
    }
  }

  itemsFor(category: string): Specialization[] {
    return this.catalog.filter(x => x.category === category);
  }

  isSelected(id: number): boolean {
    return this.selected.has(id);
  }

  toggleClient(id: number, checked: boolean): void {
    if (checked) this.selected.add(id);
    else this.selected.delete(id);
    this.success = '';
    this.error = '';
  }

  toggleTenant(item: Specialization, enabled: boolean): void {
    this.error = '';
    this.success = '';
    this.service.setTenantEnabled(item.id, enabled)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          item.enabled = enabled;
          if (!enabled) {
            this.selected.delete(item.id);
          }
          this.success = `Configuración de «${item.name}» actualizada.`;
        },
        error: () => {
          this.error = `No se ha podido cambiar la configuración de «${item.name}».`;
        }
      });
  }

  save(): void {
    this.saving = true;
    this.error = '';
    this.success = '';

    const items = [...this.selected]
      .filter(id => this.catalog.some(x => x.id === id && x.enabled))
      .map(specializationId => ({
        specializationId,
        notes: this.notes[specializationId]?.trim() || null
      }));

    this.service.setClientSpecializations(this.clientId, items)
      .pipe(takeUntil(this.destroy$))
      .subscribe({
        next: () => {
          this.saving = false;
          this.success = 'Especializaciones del paciente guardadas.';
        },
        error: err => {
          this.saving = false;
          this.error = err?.error || 'No se han podido guardar las especializaciones.';
        }
      });
  }

  ngOnDestroy(): void {
    this.destroy$.next();
    this.destroy$.complete();
  }
}
