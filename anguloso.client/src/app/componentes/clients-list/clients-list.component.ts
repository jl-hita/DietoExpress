import { Component, Input, Output, EventEmitter, OnInit, OnDestroy, ViewChild, ChangeDetectionStrategy, signal} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatListModule } from '@angular/material/list';
import { MatPaginator, MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { Router } from '@angular/router';
import { ClientService } from '../../servicios/client.service';
import { AuthService } from '../../servicios/auth.service';

export interface ClientItem {
  id: number;
  name: string;
  email?: string;
  phone?: string;
  created_at?: string | null;
}

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-clients-list',
  standalone: true,
  imports: [
    CommonModule,
    FormsModule,
    MatCardModule,
    MatInputModule,
    MatIconModule,
    MatButtonModule,
    MatListModule,
    MatPaginatorModule,
    MatProgressSpinnerModule,
    MatTooltipModule,
    MatCheckboxModule,
    MatSnackBarModule,
  ],
  templateUrl: './clients-list.component.html',
  styleUrls: ['./clients-list.component.css']
})
// Documentación: este componente coordina estado de interfaz y operaciones asíncronas que deben mantenerse alineadas con la API.
export class ClientsListComponent implements OnInit, OnDestroy {
  private readonly clientsState = signal<ClientItem[]>([]);
  @Input()
  get clients(): ClientItem[] { return this.clientsState(); }
  set clients(value: ClientItem[]) { this.clientsState.set(value); }

  private readonly loadErrorState = signal<string | null>(null);
  get loadError(): string | null { return this.loadErrorState(); }
  private readonly deletingClientIdState = signal<number | null>(null);
  get deletingClientId(): number | null { return this.deletingClientIdState(); }

  private readonly loadingState = signal(false);
  @Input()
  get loading(): boolean { return this.loadingState(); }
  set loading(value: boolean) { this.loadingState.set(value); }

  @Output() clientSelected = new EventEmitter<ClientItem>();
  @Output() create = new EventEmitter<void>();
  @Output() edit = new EventEmitter<ClientItem>();

  @ViewChild(MatPaginator) paginator!: MatPaginator;

  private readonly searchTermState = signal('');
  get searchTerm(): string { return this.searchTermState(); }
  set searchTerm(value: string) { this.searchTermState.set(value); }

  private readonly filteredState = signal<ClientItem[]>([]);
  get filtered(): ClientItem[] { return this.filteredState(); }
  set filtered(value: ClientItem[]) { this.filteredState.set(value); }

  private readonly pagedClientsState = signal<ClientItem[]>([]);
  get pagedClients(): ClientItem[] { return this.pagedClientsState(); }
  set pagedClients(value: ClientItem[]) { this.pagedClientsState.set(value); }

  private readonly totalCountState = signal(0);
  get totalCount(): number { return this.totalCountState(); }
  set totalCount(value: number) { this.totalCountState.set(value); }

  pageSize = 10;

  private readonly currentPageState = signal(1);
  get currentPage(): number { return this.currentPageState(); }
  set currentPage(value: number) { this.currentPageState.set(value); }

  private readonly totalPagesState = signal(1);
  get totalPages(): number { return this.totalPagesState(); }
  set totalPages(value: number) { this.totalPagesState.set(value); }

  private readonly selectedState = signal<ClientItem | null>(null);
  get selected(): ClientItem | null { return this.selectedState(); }
  set selected(value: ClientItem | null) { this.selectedState.set(value); }

  private readonly canCreateClientState = signal(true);
  get canCreateClient(): boolean { return this.canCreateClientState(); }
  set canCreateClient(value: boolean) { this.canCreateClientState.set(value); }

  private readonly createClientReasonState = signal('');
  get createClientReason(): string { return this.createClientReasonState(); }
  set createClientReason(value: string) { this.createClientReasonState.set(value); }

  private readonly checkingCreatePermissionState = signal(true);
  get checkingCreatePermission(): boolean { return this.checkingCreatePermissionState(); }
  set checkingCreatePermission(value: boolean) { this.checkingCreatePermissionState.set(value); }

  private readonly showAllClientsState = signal(false);
  get showAllClients(): boolean { return this.showAllClientsState(); }
  set showAllClients(value: boolean) { this.showAllClientsState.set(value); }

  private readonly isSuperAdminState = signal(false);
  get isSuperAdmin(): boolean { return this.isSuperAdminState(); }
  set isSuperAdmin(value: boolean) { this.isSuperAdminState.set(value); }
  private searchTimer?: ReturnType<typeof setTimeout>;

  constructor(private router: Router, private clientService: ClientService, private authService: AuthService, private snackBar: MatSnackBar) { }

  ngOnInit(): void {
    this.isSuperAdmin = this.authService.isSuperAdmin();
    this.showAllClients = this.isSuperAdmin;
    this.loadClients();
    if (!this.isSuperAdmin) this.checkCreatePermission();
    else { this.checkingCreatePermission = false; this.canCreateClient = false; this.createClientReason = 'La creación requiere seleccionar un tenant profesional.'; }
  }

  // La paginación y búsqueda se resuelven en el servidor; el componente adapta la respuesta al modelo visual sin mantener una copia completa de los clientes.
  loadClients(): void {
    this.loading = true;
    this.loadErrorState.set(null);
    const page = this.paginator ? this.paginator.pageIndex + 1 : this.currentPage;
    this.clientService.getClients(page, this.pageSize, this.searchTerm, this.showAllClients && this.isSuperAdmin).subscribe({
      next: result => {
        this.clients = (result.items || []).map(client => ({
          id: client.id,
          name: client.fullName,
          email: client.email,
          phone: client.phone,
          created_at: client.createdAt
        }));
        this.totalCount = result.totalCount;
        this.currentPage = result.page;
        this.filtered = this.clients;
        this.pagedClients = this.clients;
        this.loading = false;

      },
      error: () => {
        this.loadErrorState.set('No se ha podido cargar la lista de pacientes. Comprueba la conexión e inténtalo de nuevo.');
        this.loading = false;
      }
    });
  }

  toggleShowAllClients(): void {
    if (!this.isSuperAdmin) return;
    this.showAllClients = !this.showAllClients;
    this.loadClients();
  }

  checkCreatePermission(): void {
    this.checkingCreatePermission = true;
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

  ngOnChanges(): void {
    this.refresh();
  }

  // Se aplica debounce al refresco para no saturar el API cuando cambia rápidamente el filtro de búsqueda.
  refresh(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.loadClients(), 350);
  }

  ngOnDestroy(): void {
    if (this.searchTimer) clearTimeout(this.searchTimer);
  }


  applyPaging() {
    this.pagedClients = this.clients;
  }


  pageChanged(event: PageEvent) {
    this.pageSize = event.pageSize;
    this.currentPage = event.pageIndex + 1;
    this.loadClients();
    window.scrollTo({ top: 0 });
  }


  selectClient(c: ClientItem) {
    this.selected = c;
    this.clientSelected.emit(c);
  }

  // paginador personalizado: navegación
  goToPage(p: number) {
    if (p < 1 || p > this.totalPages) return;
    this.currentPage = p;
    this.loadClients();
  }

  prevPage() {
    if (this.currentPage > 1) {
      this.currentPage--;
      this.loadClients();
    }
  }

  nextPage() {
    if (this.currentPage < this.totalPages) {
      this.currentPage++;
      this.loadClients();
    }
  }

  // recalcula páginas y slice visible
  recalculate() {
    this.totalPages = Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }


  // El servidor ya devuelve únicamente la página solicitada.
  updatePaged() {
    this.pagedClients = this.clients;
    this.totalPages = Math.max(1, Math.ceil(this.totalCount / this.pageSize));
  }


  // función que devuelve los números de página que queremos mostrar (ajustable)
  // Mantiene un máximo de cinco botones visibles y desplaza la ventana alrededor de la página actual.
  pagesToShow() {
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

  createNew() {
    if (!this.canCreateClient || this.checkingCreatePermission) return;
    this.router.navigate(['/clients/nuevo']);
  }

  editClient(c: ClientItem) {
    this.edit.emit(c);
    this.router.navigate(['/clients', c.id]);
  }
  isDeletingClient(id: number): boolean { return this.deletingClientIdState() === id; }

  deleteClient(c: ClientItem): void {
    if (this.isSuperAdmin || this.deletingClientIdState() !== null) return;
    const confirmed = confirm(
      `¿Archivar el expediente de ${c.name}? Se revocará su acceso al portal y se desactivarán sus asignaciones profesionales. El expediente se conservará; no se eliminará físicamente.`
    );
    if (!confirmed) return;

    this.deletingClientIdState.set(c.id);
    this.clientService.deleteClient(c.id).subscribe({
      next: () => {
        this.deletingClientIdState.set(null);
        this.snackBar.open('Expediente archivado. Se ha revocado el acceso al portal.', 'Cerrar', { duration: 4000 });
        this.loadClients();
      },
      error: err => {
        this.deletingClientIdState.set(null);
        const message = err?.error?.message || (typeof err?.error === 'string' ? err.error : null)
          || 'No se ha podido archivar el expediente. No se han aplicado cambios.';
        this.snackBar.open(message, 'Cerrar', { duration: 5000 });
      }
    });
  }
}
