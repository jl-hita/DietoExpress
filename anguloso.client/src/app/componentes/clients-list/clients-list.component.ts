import { Component, Input, Output, EventEmitter, OnInit, ViewChild } from '@angular/core';
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
  ],
  templateUrl: './clients-list.component.html',
  styleUrls: ['./clients-list.component.css']
})
export class ClientsListComponent implements OnInit {
  @Input() clients: ClientItem[] = [];
  @Input() loading: boolean = false;

  @Output() clientSelected = new EventEmitter<ClientItem>();
  @Output() create = new EventEmitter<void>();
  @Output() edit = new EventEmitter<ClientItem>();
  @Output() remove = new EventEmitter<ClientItem>();

  @ViewChild(MatPaginator) paginator!: MatPaginator;

  searchTerm: string = '';
  filtered: ClientItem[] = [];
  pagedClients: ClientItem[] = [];
  totalCount = 0;
  pageSize = 10;              // tamaño de página por defecto
  currentPage = 1;            // página actual (1-based)
  totalPages = 1;
  selected: ClientItem | null = null;
  canCreateClient = true;
  createClientReason = '';
  checkingCreatePermission = true;
  showAllClients = false;
  isSuperAdmin = false;

  constructor(private router: Router, private clientService: ClientService, private authService: AuthService) { }

  ngOnInit(): void {
    this.isSuperAdmin = this.authService.isSuperAdmin();
    this.loadClients();
    this.checkCreatePermission();
  }

  loadClients(): void {
    this.loading = true;
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
        this.clients = [];
        this.filtered = [];
        this.pagedClients = [];
        this.totalCount = 0;
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

  refresh() {
    this.loadClients();
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
  deleteClient(c: ClientItem) { this.remove.emit(c); }
}
