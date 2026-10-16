import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatDialog } from '@angular/material/dialog';
import { MATERIAL_IMPORTS } from '../../shared/material.imports';
import { DietService } from '../../servicios/diet.service';
import { DietListItem } from '../../modelos/diet';
import { MatSnackBar } from '@angular/material/snack-bar';
import { DietShoppingListDialogComponent } from './diet-shopping-list-dialog.component';
import { AuthService } from '../../servicios/auth.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-diets-list',
  standalone: true,
  imports: [MATERIAL_IMPORTS],
  templateUrl: './diets-list.component.html',
  styleUrls: ['./diets-list.component.css']
})
// La lista coordina carga, filtros y acciones sobre dietas; los cambios de estado se reflejan después de confirmar la operación en el backend.
export class DietsListComponent implements OnInit, OnDestroy {
  private readonly dietsState = signal<DietListItem[]>([]);
  get diets(): DietListItem[] { return this.dietsState(); }
  set diets(value: DietListItem[]) { this.dietsState.set(value); }

  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  private readonly searchTermState = signal('');
  get searchTerm(): string { return this.searchTermState(); }
  set searchTerm(value: string) { this.searchTermState.set(value); }

  private readonly filteredState = signal<DietListItem[]>([]);
  get filtered(): DietListItem[] { return this.filteredState(); }
  set filtered(value: DietListItem[]) { this.filteredState.set(value); }

  private readonly pagedDietsState = signal<DietListItem[]>([]);
  get pagedDiets(): DietListItem[] { return this.pagedDietsState(); }
  set pagedDiets(value: DietListItem[]) { this.pagedDietsState.set(value); }

  pageSize = 10;

  private readonly currentPageState = signal(1);
  get currentPage(): number { return this.currentPageState(); }
  set currentPage(value: number) { this.currentPageState.set(value); }

  private readonly totalPagesState = signal(1);
  get totalPages(): number { return this.totalPagesState(); }
  set totalPages(value: number) { this.totalPagesState.set(value); }

  private readonly totalCountState = signal(0);
  get totalCount(): number { return this.totalCountState(); }
  set totalCount(value: number) { this.totalCountState.set(value); }

  private readonly showAllDietsState = signal(false);
  get showAllDiets(): boolean { return this.showAllDietsState(); }
  set showAllDiets(value: boolean) { this.showAllDietsState.set(value); }

  private readonly isSuperAdminState = signal(false);
  get isSuperAdmin(): boolean { return this.isSuperAdminState(); }
  set isSuperAdmin(value: boolean) { this.isSuperAdminState.set(value); }
  private searchTimer?: ReturnType<typeof setTimeout>;

  constructor(
    private dietService: DietService,
    private router: Router,
    private snackBar: MatSnackBar,
    private dialog: MatDialog,
    private authService: AuthService
  ) {}


  ngOnInit(): void {
    this.isSuperAdmin = this.authService.isSuperAdmin();
    this.showAllDiets = this.isSuperAdmin;
    this.loadDiets();
  }

  // La paginación y búsqueda se envían al servidor; el frontend conserva únicamente la página actual y calcula su representación visual.
  loadDiets(): void {
    this.loading.set(true);
    this.error.set(null);
    this.dietService.getDiets(this.currentPage, this.pageSize, this.searchTerm, this.showAllDiets && this.isSuperAdmin).subscribe({
      next: (result) => {
        this.diets = result.items || [];
        this.totalCount = result.totalCount;
        this.currentPage = result.page;
        this.totalPages = Math.max(1, Math.ceil(this.totalCount / this.pageSize));
        this.filtered = this.diets;
        this.pagedDiets = this.diets;
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.diets = [];
        this.filtered = [];
        this.pagedDiets = [];
        this.totalCount = 0;
        this.error.set(err?.status === 404
          ? 'El API de dietas no está disponible aún. Configure el backend.'
          : 'Error al cargar las dietas.');
      }
    });
  }

  toggleShowAllDiets(): void {
    if (!this.isSuperAdmin) return;
    this.showAllDiets = !this.showAllDiets;
    this.loadDiets();
  }

  // El pequeño debounce evita lanzar una petición por cada pulsación cuando el usuario está escribiendo en el buscador.
  refresh(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.loadDiets(), 350);
  }

  ngOnDestroy(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
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

  // Se limita el número de botones de página para que la paginación siga siendo manejable incluso con muchas dietas.
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

