import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';

interface DocumentTemplate {
  id: number;
  name: string;
  description?: string | null;
  documentType: string;
  version: number;
  isActive: boolean;
  requiredOnClientCreation: boolean;
  requiredBeforeConsultation: boolean;
  requiresSignature: boolean;
  fileName?: string | null;
  createdAt: string;
  updatedAt: string;
}

@Component({
  selector: 'app-document-templates',
  standalone: true,
  templateUrl: './document-templates.component.html',
  imports: [CommonModule, FormsModule, MatButtonModule, MatCardModule, MatCheckboxModule, MatIconModule, MatInputModule, MatSelectModule, MatProgressSpinnerModule]
})
export class DocumentTemplatesComponent implements OnInit {
  templates: DocumentTemplate[] = [];
  loading = true;
  saving = false;
  error = '';
  selectedFile: File | null = null;
  form = { name: '', description: '', documentType: 'consent', requiredOnClientCreation: false, requiredBeforeConsultation: false, requiresSignature: true };

  constructor(private http: HttpClient) {}

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading = true;
    this.http.get<DocumentTemplate[]>('/api/document-templates').subscribe({
      next: x => { this.templates = x; this.loading = false; },
      error: e => { this.error = e?.error?.message || 'No se pudieron cargar las plantillas.'; this.loading = false; }
    });
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedFile = input.files?.[0] || null;
  }

  upload(): void {
    if (!this.selectedFile || !this.form.name.trim() || this.saving) return;
    this.saving = true;
    const data = new FormData();
    data.append('file', this.selectedFile);
    data.append('name', this.form.name.trim());
    data.append('description', this.form.description);
    data.append('documentType', this.form.documentType);
    data.append('requiredOnClientCreation', String(this.form.requiredOnClientCreation));
    data.append('requiredBeforeConsultation', String(this.form.requiredBeforeConsultation));
    data.append('requiresSignature', String(this.form.requiresSignature));
    this.http.post('/api/document-templates', data).subscribe({
      next: () => {
        this.form = { name: '', description: '', documentType: 'consent', requiredOnClientCreation: false, requiredBeforeConsultation: false, requiresSignature: true };
        this.selectedFile = null;
        this.saving = false;
        this.load();
      },
      error: e => { this.error = e?.error?.message || 'No se pudo subir la plantilla.'; this.saving = false; }
    });
  }

  toggle(template: DocumentTemplate): void {
    this.http.patch('/api/document-templates/' + template.id + '/active', { active: !template.isActive }).subscribe({
      next: () => template.isActive = !template.isActive,
      error: e => this.error = e?.error?.message || 'No se pudo cambiar el estado.'
    });
  }
}
