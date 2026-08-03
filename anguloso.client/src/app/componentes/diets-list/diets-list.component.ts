import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { MATERIAL_IMPORTS } from '../../shared/material.imports';
import { DietService } from '../../servicios/diet.service';
import { Diet } from '../../modelos/diet';
import { MatSnackBar } from '@angular/material/snack-bar';

@Component({
  selector: 'app-diets-list',
  standalone: true,
  imports: [MATERIAL_IMPORTS],
  templateUrl: './diets-list.component.html',
  styleUrls: ['./diets-list.component.css']
})
export class DietsListComponent implements OnInit {
  diets: Diet[] = [];
  loading = false;
  error: string | null = null;

  searchTerm = '';
  filtered: Diet[] = [];
  pagedDiets: Diet[] = [];
  pageSize = 10;
  currentPage = 1;
  totalPages = 1;

  constructor(
    private dietService: DietService,
    private router: Router,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.loadDiets();
  }

  loadDiets(): void {
    this.loading = true;
    this.error = null;
    this.dietService.getDiets().subscribe({
      next: (list) => {
        this.diets = list || [];
        this.refresh();
        this.loading = false;
      },
      error: (err) => {
        this.loading = false;
        this.diets = [];
        this.filtered = [];
        this.pagedDiets = [];
        this.error = err?.status === 404
          ? 'El API de dietas no está disponible aún. Configure el backend.'
          : 'Error al cargar las dietas.';
      }
    });
  }

  refresh(): void {
    const term = (this.searchTerm || '').toLowerCase().trim();
    this.filtered = term
      ? this.diets.filter(d => (d.name || '').toLowerCase().includes(term))
      : [...this.diets];
    this.recalculate();
  }

  recalculate(): void {
    const len = this.filtered.length || 0;
    this.totalPages = Math.max(1, Math.ceil(len / this.pageSize));
    this.updatePaged();
  }

  updatePaged(): void {
    const start = (this.currentPage - 1) * this.pageSize;
    this.pagedDiets = (this.filtered || []).slice(start, start + this.pageSize);
  }

  goToPage(p: number): void {
    if (p < 1 || p > this.totalPages) return;
    this.currentPage = p;
    this.updatePaged();
  }

  prevPage(): void {
    if (this.currentPage > 1) {
      this.currentPage--;
      this.updatePaged();
    }
  }

  nextPage(): void {
    if (this.currentPage < this.totalPages) {
      this.currentPage++;
      this.updatePaged();
    }
  }

  pagesToShow(): number[] {
    const pages: number[] = [];
    const maxButtons = 5;
    let start = Math.max(1, this.currentPage - Math.floor(maxButtons / 2));
    let end = start + maxButtons - 1;
    if (end > this.totalPages) {
      end = this.totalPages;
      start = Math.max(1, end - maxButtons + 1);
    }
    for (let i = start; i <= end; i++) pages.push(i);
    return pages;
  }

  createNew(): void {
    this.router.navigate(['/diets/nuevo']);
  }

  editDiet(d: Diet): void {
    if (d.id != null) this.router.navigate(['/diets', d.id]);
  }

  deleteDiet(d: Diet): void {
    if (d.id == null) return;
    if (!confirm(`¿Eliminar la dieta "${d.name}"?`)) return;
    this.dietService.deleteDiet(d.id).subscribe({
      next: () => {
        this.snackBar.open('Dieta eliminada', 'Cerrar', { duration: 3000 });
        this.loadDiets();
      },
      error: () => {
        this.snackBar.open('Error al eliminar la dieta', 'Cerrar', { duration: 4000 });
      }
    });
  }
}
