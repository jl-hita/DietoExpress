import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ProfessionalStatistics, ProfessionalStatisticsService } from '../../servicios/professional-statistics.service';

@Component({
  selector:'app-statistics',
  standalone:true,
  imports:[CommonModule,FormsModule,MatCardModule,MatIconModule,MatProgressSpinnerModule],
  templateUrl:'./statistics.component.html',
  styleUrl:'./statistics.component.css'
})
export class StatisticsComponent implements OnInit {
  data:ProfessionalStatistics|null=null;
  loading=true;
  error:string|null=null;
  from='';
  to='';

  constructor(private service:ProfessionalStatisticsService) {}

  ngOnInit():void {
    const today=new Date();
    this.to=this.isoDate(today);
    const start=new Date(today);
    start.setDate(start.getDate()-29);
    this.from=this.isoDate(start);
    this.reload();
  }

  reload():void {
    if (!this.from || !this.to) return;
    this.loading=true;
    this.error=null;
    this.service.get(this.from,this.to).subscribe({
      next:data=>{this.data=data;this.loading=false;},
      error:err=>{this.error=err?.error?.message||'No hemos podido cargar las estadísticas.';this.loading=false;}
    });
  }

  noShowRate():number {
    const total=(this.data?.completedAppointments||0)+(this.data?.noShowAppointments||0);
    return total ? ((this.data?.noShowAppointments||0)/total)*100 : 0;
  }

  reviewRate():number {
    return this.data?.checkins ? ((this.data.reviewedCheckins/this.data.checkins)*100) : 0;
  }

  maxSeriesValue(key:'newPatients'|'completedAppointments'|'checkins'):number {
    return Math.max(1,...(this.data?.series||[]).map(x=>x[key]));
  }

  barHeight(value:number,key:'newPatients'|'completedAppointments'|'checkins'):string {
    return Math.max(4,(value/this.maxSeriesValue(key))*100)+'%';
  }

  formatMonth(value:string):string {
    return new Date(value).toLocaleDateString('es-ES',{month:'short',year:'2-digit'});
  }

  private isoDate(value:Date):string {
    const y=value.getFullYear();
    const m=String(value.getMonth()+1).padStart(2,'0');
    const d=String(value.getDate()).padStart(2,'0');
    return `${y}-${m}-${d}`;
  }
}
