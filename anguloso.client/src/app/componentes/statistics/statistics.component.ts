import { CommonModule } from '@angular/common';
import { Component, OnInit, signal } from '@angular/core';
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
  readonly data = signal<ProfessionalStatistics | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly from = signal('');
  readonly to = signal('');

  constructor(private service:ProfessionalStatisticsService) {}

  ngOnInit():void {
    const today=new Date();
    this.to.set(this.isoDate(today));
    const start=new Date(today);
    start.setDate(start.getDate()-29);
    this.from.set(this.isoDate(start));
    this.reload();
  }

  reload():void {
    const from = this.from();
    const to = this.to();
    if (!from || !to) return;
    this.loading.set(true);
    this.error.set(null);
    this.service.get(from, to).subscribe({
      next: data => { this.data.set(data); this.loading.set(false); },
      error: err => {
        this.error.set(err?.error?.message || 'No hemos podido cargar las estadísticas.');
        this.loading.set(false);
      }
    });
  }

  noShowRate():number {
    const data = this.data();
    const total = (data?.completedAppointments || 0) + (data?.noShowAppointments || 0);
    return total ? ((data?.noShowAppointments || 0) / total) * 100 : 0;
  }

  reviewRate():number {
    const data = this.data();
    return data?.checkins ? (data.reviewedCheckins / data.checkins) * 100 : 0;
  }

  maxSeriesValue(key:'newPatients'|'completedAppointments'|'checkins'):number {
    return Math.max(1, ...(this.data()?.series || []).map(x => x[key]));
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
