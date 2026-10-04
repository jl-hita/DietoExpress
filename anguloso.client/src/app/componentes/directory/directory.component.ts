import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { DirectoryProfile } from './directory.models';
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
      },
      error: () => {
        this.error = 'No se ha encontrado el profesional solicitado.';
        this.loading = false;
      }
    });
  }
}
