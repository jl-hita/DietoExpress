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
import { Router } from '@angular/router';

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
  pageSize = 10;              // tamaño de página por defecto
  currentPage = 1;            // página actual (1-based)
  totalPages = 1;
  selected: ClientItem | null = null;

  constructor(private router: Router) { }

  ngOnInit(): void {
    this.refresh();
  }

  ngOnChanges(): void {
    this.refresh();
  }

  refresh() {
    const term = this.searchTerm?.toLowerCase()?.trim() || '';
    this.filtered = this.clients.filter(c => {
      return (
        c.name.toLowerCase().includes(term) ||
        (c.email || '').toLowerCase().includes(term) ||
        (c.phone || '').toLowerCase().includes(term)
      );
    });
    // reset paginator
    if (this.paginator) {
      this.paginator.pageIndex = 0; // <-- mejor que firstPage()
    }
    this.applyPaging();
  }

  applyPaging() {
    const pageIndex = this.paginator ? this.paginator.pageIndex : 0;
    const start = pageIndex * this.pageSize;
    this.pagedClients = this.filtered.slice(start, start + this.pageSize);
  }

  pageChanged(event: PageEvent) {
    this.pageSize = event.pageSize;
    this.applyPaging();
    window.scrollTo({ top: 0 }); // opcional: para evitar que quede abajo al cambiar página
  }

  selectClient(c: ClientItem) {
    this.selected = c;
    this.clientSelected.emit(c);
  }

  // paginador personalizado: navegación
  goToPage(p: number) {
    if (p < 1 || p > this.totalPages) return;
    this.currentPage = p;
    this.updatePaged();
  }

  prevPage() {
    if (this.currentPage > 1) {
      this.currentPage--;
      this.updatePaged();
    }
  }

  nextPage() {
    if (this.currentPage < this.totalPages) {
      this.currentPage++;
      this.updatePaged();
    }
  }

  // recalcula páginas y slice visible
  recalculate() {
    const len = this.filtered.length || 0;
    this.totalPages = Math.max(1, Math.ceil(len / this.pageSize));
    this.updatePaged();
  }

  // actualiza el slice mostrado en la tabla
  updatePaged() {
    const start = (this.currentPage - 1) * this.pageSize;
    this.pagedClients = (this.filtered || []).slice(start, start + this.pageSize);
    // opcional: si quieres mantener compatibilidad con pageChanged($event) existente:
    // this.pageChanged({ pageIndex: this.currentPage - 1, pageSize: this.pageSize, length: this.filtered.length });
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
    this.router.navigate(['/clients/nuevo']);
  }

  editClient(c: ClientItem) { this.edit.emit(c); }
  deleteClient(c: ClientItem) { this.remove.emit(c); }
}
