import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environments';
import { Biometric, Client, ClientDiet, AssignDietPayload, UpdateClientDietPayload } from '../modelos/client';

@Injectable({ providedIn: 'root' })
export class ClientService {
  private base = environment.apiUrl;

  constructor(private http: HttpClient) { }

  getClients(): Observable<Client[]> {
    return this.http.get<Client[]>(`${this.base}/clients`);
  }

  getClient(id: number): Observable<Client> {
    return this.http.get<Client>(`${this.base}/clients/${id}`);
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
}

