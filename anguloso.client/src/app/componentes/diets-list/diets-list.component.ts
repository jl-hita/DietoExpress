import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MATERIAL_IMPORTS } from '../../shared/material.imports';
import { DietService } from '../../servicios/diet.service';
import { DietListItem } from '../../modelos/diet';
import { MatSnackBar } from '@angular/material/snack-bar';
import { DietShoppingListDialogComponent } from './diet-shopping-list-dialog.component';
import { AuthService } from '../../servicios/auth.service';

@Component({
  selector: 'app-diets-list',
  standalone: true,
  imports: [MATERIAL_IMPORTS],
  templateUrl: './diets-list.component.html',
  styleUrls: ['./diets-list.component.css']
})
export class DietsListComponent implements OnInit {
  diets: DietListItem[] = [];
  loading = false;
  error: string | null = null;

  searchTerm = '';
  filtered: DietListItem[] = [];
  pagedDiets: DietListItem[] = [];
  pageSize = 10;
  currentPage = 1;
  totalPages = 1;
  totalCount = 0;
  showAllDiets = false;
  isSuperAdmin = false;

  constructor(
    private dietService: DietService,
    private router: Router,
    private snackBar: MatSnackBar,
    private dialog: MatDialog,
    private authService: AuthService
  ) {}


  ngOnInit(): void {
    this.isSuperAdmin = this.authService.isSuperAdmin();
    this.loadDiets();
  }

  loadDiets(): void {
    this.loading = true;
    this.error = null;
    this.dietService.getDiets(this.currentPage, this.pageSize, this.searchTerm, this.showAllDiets && this.isSuperAdmin).subscribe({
      next: (result) => {
        this.diets = result.items || [];
        this.totalCount = result.totalCount;
        this.currentPage = result.page;
        this.totalPages = Math.max(1, Math.ceil(this.totalCount / this.pageSize));
        this.filtered = this.diets;
        this.pagedDiets = this.diets;
        this.loading = false;
      },
      error: (err) => {
        this.loading = false;
        this.diets = [];
        this.filtered = [];
        this.pagedDiets = [];
        this.totalCount = 0;
        this.error = err?.status === 404
          ? 'El API de dietas no está disponible aún. Configure el backend.'
          : 'Error al cargar las dietas.';
      }
    });
  }

  toggleShowAllDiets(): void {
    if (!this.isSuperAdmin) return;
    this.showAllDiets = !this.showAllDiets;
    this.loadDiets();
  }

  refresh(): void {
    this.loadDiets();
  }

  recalculate(): void {
    this.totalPages = Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }

  updatePaged(): void {
    this.pagedDiets = this.diets;
  }

  goToPage(p: number): void {
    if (p < 1 || p > this.totalPages) return;
    this.currentPage = p;
    this.loadDiets();
  }

  prevPage(): void {
    if (this.currentPage > 1) {
      this.currentPage--;
      this.loadDiets();
    }
  }

  nextPage(): void {
    if (this.currentPage < this.totalPages) {
      this.currentPage++;
      this.loadDiets();
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

  editDiet(d: DietListItem): void {
    if (d.id != null) this.router.navigate(['/diets', d.id]);
  }

  viewShoppingList(d: DietListItem): void {
    if (d.id == null) return;
    this.dialog.open(DietShoppingListDialogComponent, {
      width: '600px',
      data: {
        dietId: d.id,
        dietName: d.name
      }
    });
  }

  deleteDiet(d: DietListItem): void {
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

