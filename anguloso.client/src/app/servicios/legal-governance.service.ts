import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface LegalRatActivity { id:number; name:string; purpose:string; role:string; legalBasis?:string; subjectCategories?:string; dataCategories?:string; specialCategories?:string; recipients?:string; internationalTransfers?:string; retention?:string; securityMeasures?:string; notes?:string; status:string; }
export interface LegalRiskAssessment { id:number; name:string; riskDescription:string; likelihood:number; impact:number; measures?:string; residualRisk?:string; owner?:string; reviewDate?:string; status:string; }
export interface LegalEipdDecision { id:number; decision:string; justification:string; additionalMeasures?:string; decidedAt:string; reviewDate?:string; documentReference?:string; }
export interface LegalRetentionPolicy { id:number; treatmentKey:string; label:string; startEvent:string; periodValue:number; periodUnit:string; deletionAction:string; legalHold:boolean; exceptionNotes?:string; status:string; }
export interface SaveLegalRetentionPolicy { treatmentKey:string; label:string; startEvent:string; periodValue:number; periodUnit:string; deletionAction:string; legalHold:boolean; exceptionNotes?:string; status:string; }

@Injectable({providedIn:'root'})
export class LegalGovernanceService {
 private readonly baseUrl='/api/legal-governance';
 constructor(private http:HttpClient){}
 listRat():Observable<LegalRatActivity[]>{return this.http.get<LegalRatActivity[]>(this.baseUrl+'/rat');}
 createRat(value:Omit<LegalRatActivity,'id'>):Observable<{id:number}>{return this.http.post<{id:number}>(this.baseUrl+'/rat',value);}
 updateRat(id:number,value:Omit<LegalRatActivity,'id'>):Observable<void>{return this.http.put<void>(this.baseUrl+'/rat/'+id,value);}
 deleteRat(id:number):Observable<void>{return this.http.delete<void>(this.baseUrl+'/rat/'+id);}
 listRisks():Observable<LegalRiskAssessment[]>{return this.http.get<LegalRiskAssessment[]>(this.baseUrl+'/risks');}
 createRisk(value:Omit<LegalRiskAssessment,'id'>):Observable<{id:number}>{return this.http.post<{id:number}>(this.baseUrl+'/risks',value);}
 updateRisk(id:number,value:Omit<LegalRiskAssessment,'id'>):Observable<void>{return this.http.put<void>(this.baseUrl+'/risks/'+id,value);}
 listEipd():Observable<LegalEipdDecision[]>{return this.http.get<LegalEipdDecision[]>(this.baseUrl+'/eipd');}
 createEipd(value:Omit<LegalEipdDecision,'id'|'decidedAt'>):Observable<{id:number}>{return this.http.post<{id:number}>(this.baseUrl+'/eipd',value);}
 listRetention():Observable<LegalRetentionPolicy[]>{return this.http.get<LegalRetentionPolicy[]>(this.baseUrl+'/retention');}
 saveRetention(value:SaveLegalRetentionPolicy):Observable<void>{return this.http.put<void>(this.baseUrl+'/retention',value);}
}
