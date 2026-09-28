import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environments';
import { Biometric, ClientListItem, ClientDetail, ClientDiet, AssignDietPayload, UpdateClientDietPayload } from '../modelos/client';

export interface ClientCreationAvailability {
  allowed: boolean;
  reason?: string | null;
}

@Injectable({ providedIn: 'root' })
export class ClientService {
  private base = environment.apiUrl;

  constructor(private http: HttpClient) { }

  getClients(): Observable<ClientListItem[]> {
    return this.http.get<ClientListItem[]>(`${this.base}/clients`);
  }

  canCreateClient(): Observable<ClientCreationAvailability> {
    return this.http.get<ClientCreationAvailability>(this.base + '/clients/can-create');
  }

  getClient(id: number): Observable<ClientDetail> {
    return this.http.get<ClientDetail>(`${this.base}/clients/${id}`);
  }

  createClient(dto: any) {
    return this.http.post(`${this.base}/clients`, dto);
  }

  updateClient(id: number, dto: any) {
    return this.http.put(`${this.base}/clients/${id}`, dto);
  }

  deleteClient(id: number) {
    return this.http.delete(`${this.base}/clients/${id}`);
  }

  // biometrics
  getBiometrics(clientId: number) {
    return this.http.get<Biometric[]>(`${this.base}/clients/${clientId}/biometrics`);
  }

  getBiometric(clientId: number, id: number) {
    return this.http.get<Biometric>(`${this.base}/clients/${clientId}/biometrics/${id}`);
  }

  createBiometric(clientId: number, dto: any) {
    return this.http.post(`${this.base}/clients/${clientId}/biometrics`, dto);
  }

  updateBiometric(clientId: number, id: number, dto: any) {
    return this.http.put(`${this.base}/clients/${clientId}/biometrics/${id}`, dto);
  }

  deleteBiometric(clientId: number, id: number) {
    return this.http.delete(`${this.base}/clients/${clientId}/biometrics/${id}`);
  }

  // bioimpedance import (Tanita / InBody)
  previewBioimpedanceImport(clientId: number, file: File, device?: string): Observable<any> {
    const formData = new FormData();
    formData.append('file', file);
    if (device) formData.append('device', device);
    return this.http.post<any>(`${this.base}/clients/${clientId}/biometrics/import/preview`, formData);
  }

  confirmBioimpedanceImport(clientId: number, rows: any[]): Observable<any> {
    return this.http.post<any>(`${this.base}/clients/${clientId}/biometrics/import/confirm`, { rows });
  }

  // client_diets (asignación de dietas)
  getClientDiets(clientId: number): Observable<ClientDiet[]> {
    return this.http.get<ClientDiet[]>(`${this.base}/clients/${clientId}/diets`);
  }

  getActiveClientDiet(clientId: number): Observable<any> {
    return this.http.get<any>(`${this.base}/clients/${clientId}/diets/active`);
  }

  assignDiet(clientId: number, payload: AssignDietPayload): Observable<ClientDiet> {
    return this.http.post<ClientDiet>(`${this.base}/clients/${clientId}/diets`, payload);
  }

  updateClientDiet(clientId: number, assignmentId: number, dto: UpdateClientDietPayload): Observable<any> {
    return this.http.put<any>(`${this.base}/clients/${clientId}/diets/${assignmentId}`, dto);
  }

  deactivateClientDiet(clientId: number, assignmentId: number): Observable<any> {
    return this.http.post<any>(`${this.base}/clients/${clientId}/diets/${assignmentId}/deactivate`, {});
  }

  deleteClientDiet(clientId: number, assignmentId: number): Observable<any> {
    return this.http.delete<any>(`${this.base}/clients/${clientId}/diets/${assignmentId}`);
  }

  getEvolution(clientId: number): Observable<Biometric[]> {
    return this.http.get<Biometric[]>(`${this.base}/clients/${clientId}/evolution`);
  }

  downloadDietPdf(clientId: number, assignmentId: number): Observable<Blob> {
    return this.http.get(`${this.base}/clients/${clientId}/diets/${assignmentId}/pdf`, { responseType: 'blob' });
  }

  downloadActiveDietPdf(clientId: number): Observable<Blob> {
    return this.http.get(`${this.base}/clients/${clientId}/diets/active/pdf`, { responseType: 'blob' });
  }

  getEnergyRequirements(clientId: number): Observable<any> {
    return this.http.get<any>(`${this.base}/clients/${clientId}/energy-requirements`);
  }

  validateSavedDiet(clientId: number, dietId: number): Observable<any[]> {
    return this.http.get<any[]>(`${this.base}/clients/${clientId}/diets/${dietId}/validate`);
  }

  validateDietDraft(clientId: number, draft: any): Observable<any[]> {
    return this.http.post<any[]>(`${this.base}/clients/${clientId}/diets/validate-draft`, draft);
  }

  getPatientProfile(clientId: number): Observable<any> {
    return this.http.get<any>(`${this.base}/clients/${clientId}/patient-profile`);
  }

  getActiveShoppingList(clientId: number): Observable<any[]> {
    return this.http.get<any[]>(`${this.base}/clients/${clientId}/diets/active/shopping-list`);
  }
}
