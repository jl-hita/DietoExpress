import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
export interface ClinicLicense { tenantId:number; planCode:string; planName:string; status:string; expiresAt?:string; nutritionists:number; clients:number; maxNutritionists?:number; includedNutritionists?:number; contractedNutritionists?:number; availableNutritionistSlots?:number; additionalSeatPriceConfigured?:boolean; maxClientsPerNutritionist?:number; maxTotalClients?:number; nutritionistReplacementAvailableAt?:string|null; features:string[]; }
export interface ClinicNutritionist { id:number; full_name:string; username:string; email:string; role:string; last_login?:string; archived_at?:string|null; active:boolean; clientCount:number; }
export interface ClinicClient { id:number; full_name:string; email:string; phone:string; nutritionistId:number|null; nutritionistName:string|null; }
export interface ClinicDashboard { license:ClinicLicense; nutritionists:ClinicNutritionist[]; clients:ClinicClient[]; unassignedClientCount:number; todayAppointments:number; unreadMessageCount:number; pendingDocumentCount:number; documentProvisioningRetryCount:number; documentProvisioningFailedCount:number;
    documentProvisioningIncompleteCount:number; }
export interface NutritionistDeactivationPreview { nutritionist:any; clients:{clientId:number;fullName:string;email:string}[]; candidates:{id:number;fullName:string;username:string}[]; requiresReassignment:boolean; }
@Injectable({providedIn:'root'})
// Centraliza las operaciones de la clínica para que los componentes no dupliquen URLs ni transformaciones de las respuestas.
export class ClinicService {
 constructor(private http:HttpClient){}
 getDashboard():Observable<ClinicDashboard>{return this.http.get<ClinicDashboard>('/api/clinic/dashboard');}
 createNutritionist(data:any):Observable<any>{return this.http.post('/api/clinic/nutritionists',data);}
 disableNutritionist(id:number,data:any):Observable<any>{return this.http.put('/api/clinic/nutritionists/'+id+'/disable',data);}
 activateNutritionist(id:number):Observable<any>{return this.http.put('/api/clinic/nutritionists/'+id+'/activate',{});}
 getDeactivationPreview(id:number):Observable<NutritionistDeactivationPreview>{return this.http.get<NutritionistDeactivationPreview>('/api/clinic/nutritionists/'+id+'/deactivation-preview');}
 assignClient(clientId:number,nutritionistId:number|null):Observable<any>{return this.http.put('/api/clinic/clients/'+clientId+'/assign',{nutritionistId});}
 changeNutritionistSeats(targetSeats:number):Observable<any>{return this.http.post('/api/clinic/nutritionist-seats',{targetSeats});}
}