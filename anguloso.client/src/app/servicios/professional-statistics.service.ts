import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ProfessionalStatisticsPoint {
  month:string;
  newPatients:number;
  completedAppointments:number;
  checkins:number;
}

export interface ProfessionalStatisticsWorkload {
  nutritionistId:number;
  nutritionistName:string;
  activePatients:number;
  completedAppointments:number;
  checkins:number;
}

export interface ProfessionalStatistics {
  from:string;
  to:string;
  scope:'nutritionist'|'clinic';
  activePatients:number;
  newPatients:number;
  archivedPatients:number;
  totalAppointments:number;
  completedAppointments:number;
  noShowAppointments:number;
  cancelledAppointments:number;
  checkins:number;
  reviewedCheckins:number;
  averageAdherence:number|null;
  subscriptionRevenue:number;
  paidPayments:number;
  nutritionistWorkload:ProfessionalStatisticsWorkload[];
  activeSubscriptions:number;
  scheduledCancellations:number;
  cancelledSubscriptions:number;
  newSubscriptions:number;
  weightChangeKg:number|null;
  bodyFatChangePoints:number|null;
  muscleMassChangeKg:number|null;
  series:ProfessionalStatisticsPoint[];
}

@Injectable({providedIn:'root'})
export class ProfessionalStatisticsService {
  constructor(private http:HttpClient) {}

  get(from:string,to:string):Observable<ProfessionalStatistics> {
    const params=new HttpParams().set('from',from).set('to',to);
    return this.http.get<ProfessionalStatistics>('/api/professional/statistics',{params});
  }
}
