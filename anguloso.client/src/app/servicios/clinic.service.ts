import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
export interface ClinicLicense { tenantId:number; planCode:string; planName:string; status:string; expiresAt?:string; nutritionists:number; clients:number; maxNutritionists?:number; maxClientsPerNutritionist?:number; maxTotalClients?:number; features:string[]; }
export interface ClinicNutritionist { id:number; full_name:string; username:string; email:string; role:string; last_login?:string; clientCount:number; }
export interface ClinicClient { id:number; full_name:string; email:string; phone:string; nutritionistId:number|null; nutritionistName:string|null; }
export interface ClinicDashboard { license:ClinicLicense; nutritionists:ClinicNutritionist[]; clients:ClinicClient[]; unassignedClientCount:number; }
export interface NutritionistDeactivationPreview { nutritionist:any; clients:{clientId:number;fullName:string;email:string}[]; candidates:{id:number;fullName:string;username:string}[]; requiresReassignment:boolean; }
@Injectable({providedIn:'root'})
export class ClinicService {
 constructor(private http:HttpClient){}
 getDashboard():Observable<ClinicDashboard>{return this.http.get<ClinicDashboard>('/api/clinic/dashboard');}
 createNutritionist(data:any):Observable<any>{return this.http.post('/api/clinic/nutritionists',data);}
 disableNutritionist(id:number,data:any):Observable<any>{return this.http.put('/api/clinic/nutritionists/'+id+'/disable',data);}
 getDeactivationPreview(id:number):Observable<NutritionistDeactivationPreview>{return this.http.get<NutritionistDeactivationPreview>('/api/clinic/nutritionists/'+id+'/deactivation-preview');}
 assignClient(clientId:number,nutritionistId:number|null):Observable<any>{return this.http.put('/api/clinic/clients/'+clientId+'/assign',{nutritionistId});}
}