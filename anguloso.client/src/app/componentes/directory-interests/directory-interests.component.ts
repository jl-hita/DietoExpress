import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, OnInit } from '@angular/core';
import { RouterLink } from '@angular/router';
import { environment } from '../../../environments/environments';

interface DirectoryInterest {
  id: number;
  fullName: string;
  email: string;
  phone?: string | null;
  message?: string | null;
  createdAt: string;
  viewedAt?: string | null;
}

@Component({
  selector: 'app-directory-interests',
  standalone: true,
  imports: [CommonModule, RouterLink],
  template: `
    <section class="page-container" style="max-width: 900px; margin: 2rem auto; padding: 1rem;">
      <a routerLink="/dashboard">← Volver al panel</a>
      <h1>Personas interesadas en tu perfil</h1>
      <p>Consulta cuántas personas han mostrado interés. Sus datos personales solo se muestran cuando la licencia está activa.</p>
      <p *ngIf="loading" role="status">Cargando solicitudes…</p>
      <p *ngIf="error" role="alert">{{ error }}</p>
      <p *ngIf="!loading && !error && !canViewDetails">Tienes {{ interestCount }} persona{{ interestCount === 1 ? '' : 's' }} interesada{{ interestCount === 1 ? '' : 's' }}. Para ver sus datos y responder, activa tu licencia.</p>
      <p *ngIf="!loading && !error && canViewDetails && !interests.length" role="status">No tienes solicitudes de interés pendientes.</p>
      <article *ngFor="let interest of interests" style="border: 1px solid var(--border-color, #ddd); border-radius: 12px; padding: 1rem; margin: 1rem 0;">
        <p><strong>{{ interest.fullName }}</strong></p>
        <p><a [href]="'mailto:' + interest.email">{{ interest.email }}</a></p>
        <p *ngIf="interest.phone"><a [href]="'tel:' + interest.phone">{{ interest.phone }}</a></p>
        <p *ngIf="interest.message">{{ interest.message }}</p>
        <small>Solicitud recibida el {{ interest.createdAt | date:'d MMM yyyy, HH:mm' }}</small>
      </article>
    </section>
  `
})
export class DirectoryInterestsComponent implements OnInit {
  interests: DirectoryInterest[] = [];
  interestCount = 0;
  canViewDetails = false;
  loading = true;
  error = '';

  constructor(private readonly http: HttpClient) {}

  ngOnInit(): void {
    this.http.get<{ canViewDetails: boolean; count: number; interests?: DirectoryInterest[]; message?: string }>(`${environment.apiUrl}/directory/my/interests`).subscribe({
      next: result => {
        this.canViewDetails = result.canViewDetails;
        this.interestCount = result.count;
        this.interests = result.interests || [];
        this.loading = false;
      },
      error: error => {
        this.loading = false;
        this.error = error?.error?.message || 'No se han podido cargar las solicitudes de interés.';
      }
    });
  }
}
