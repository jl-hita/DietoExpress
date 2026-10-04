import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { DirectoryProfile, PublicAvailabilitySlot } from './directory.models';
import { DirectoryService } from './directory.service';

@Component({
  selector: 'app-directory',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatButtonModule],
  templateUrl: './directory.component.html',
  styleUrl: './directory.component.css'
})
export class DirectoryComponent implements OnInit {
  city = '';
  speciality = '';
  online = false;

  profiles: DirectoryProfile[] = [];
  profile: DirectoryProfile | null = null;
  loading = false;
  error = '';
  availability: PublicAvailabilitySlot[] = [];
  availabilityLoading = false;
  availabilityError = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly directoryService: DirectoryService
  ) {}

  ngOnInit(): void {
    const slug = this.route.snapshot.paramMap.get('slug');

    if (slug) {
      this.loadProfile(slug);
    } else {
      this.search();
    }
  }

  search(): void {
    this.loading = true;
    this.error = '';
    this.profile = null;

    this.directoryService.search(this.city, this.speciality, this.online).subscribe({
      next: profiles => {
        this.profiles = profiles;
        this.loading = false;
      },
      error: () => {
        this.error = 'No se ha podido cargar el directorio.';
        this.loading = false;
      }
    });
  }

  loadProfile(slug: string): void {
    this.loading = true;
    this.error = '';

    this.directoryService.getBySlug(slug).subscribe({
      next: profile => {
        this.profile = profile;
        this.loading = false;
        this.loadAvailability(profile.slug);
      },
      error: () => {
        this.error = 'No se ha encontrado el profesional solicitado.';
        this.loading = false;
      }
    });
  }

  loadAvailability(slug: string): void {
    this.availabilityLoading = true;
    this.availabilityError = '';

    this.directoryService.getAvailability(slug).subscribe({
      next: slots => {
        this.availability = slots;
        this.availabilityLoading = false;
      },
      error: () => {
        this.availability = [];
        this.availabilityError = 'No se ha podido consultar la disponibilidad.';
        this.availabilityLoading = false;
      }
    });
  }

  formatSlot(slot: PublicAvailabilitySlot): string {
    const date = new Date(slot.startsAt);
    return new Intl.DateTimeFormat('es-ES', {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit'
    }).format(date);
  }
}
