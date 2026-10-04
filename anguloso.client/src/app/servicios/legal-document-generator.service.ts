import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
export interface LegalGeneratedDocument { id:number; templateKey:string; version:number; title:string; content?:string; status:string; sha256:string; generatedAt:string; hasUnresolvedPlaceholders?:boolean; unresolved?:string[]; }
@Injectable({providedIn:'root'})
export class LegalDocumentGeneratorService {
 private readonly baseUrl='/api/legal-documents/generator';
 constructor(private http:HttpClient){}
 getTemplates():Observable<{key:string;file:string}[]>{return this.http.get<{key:string;file:string}[]>(this.baseUrl+'/templates');}
 generate(key:string):Observable<LegalGeneratedDocument>{return this.http.post<LegalGeneratedDocument>(this.baseUrl+'/'+key,{});}
 list():Observable<LegalGeneratedDocument[]>{return this.http.get<LegalGeneratedDocument[]>(this.baseUrl);}
 get(id:number):Observable<LegalGeneratedDocument>{return this.http.get<LegalGeneratedDocument>(this.baseUrl+'/'+id);}
 update(id:number,content:string):Observable<LegalGeneratedDocument>{return this.http.put<LegalGeneratedDocument>(this.baseUrl+'/'+id,{content});}
 publish(id:number):Observable<unknown>{return this.http.post(this.baseUrl+'/'+id+'/publish',{});}
}