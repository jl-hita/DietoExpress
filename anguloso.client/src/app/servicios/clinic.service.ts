import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
export interface ClinicLicense { tenantId:number; planCode:string; planName:string; status:string; expiresAt?:string; nutritionists:number; clients:number; maxNutritionists?:number; maxClientsPerNutritionist?:number; maxTotalClients?:number; features:string[]; }
export interface ClinicNutritionist { id:number; full_name:string; username:string; email:string; role:string; last_login?:string; clientCount:number; }
export interface ClinicClient { id:number; full_name:string; email:string; phone:string; nutritionistId:number; nutritionistName:string; }
export interface ClinicDashboard { license:ClinicLicense; nutritionists:ClinicNutritionist[]; clients:ClinicClient[]; }
@Injectable({providedIn:'root'})
export class ClinicService {
 constructor(private http:HttpClient){}
 getDashboard():Observable<ClinicDashboard>{return this.http.get<ClinicDashboard>('/api/clinic/dashboard');}
 createNutritionist(data:any):Observable<any>{return this.http.post('/api/clinic/nutritionists',data);}
 assignClient(clientId:number,nutritionistId:number):Observable<any>{return this.http.put('/api/clinic/clients/'+clientId+'/assign',{nutritionistId});}
}