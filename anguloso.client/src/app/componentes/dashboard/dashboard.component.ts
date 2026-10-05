import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ProfessionalDashboard, ProfessionalDashboardService, DashboardAppointment, DashboardTask } from '../../servicios/professional-dashboard.service';

@Component({
  selector:'app-dashboard',
  standalone:true,
  imports:[CommonModule,MatCardModule,MatIconModule,MatButtonModule,MatProgressSpinnerModule],
  templateUrl:'./dashboard.component.html',
  styleUrl:'./dashboard.component.css'
})
export class DashboardComponent implements OnInit {
  data:ProfessionalDashboard|null=null;
  loading=true;
  error:string|null=null;
  constructor(private service:ProfessionalDashboardService, private router:Router){}

  ngOnInit():void { this.reload(); }

  reload():void {
    this.loading=true;
    this.error=null;
    this.service.get().subscribe({
      next:d=>{this.data=d;this.loading=false;},
      error:e=>{this.error=e?.error?.message||'No hemos podido cargar tu dashboard.';this.loading=false;}
    });
  }

  openClient(id:number):void{this.router.navigate(['/clients',id]);}
  openAppointments():void{this.router.navigate(['/appointments']);}
  openAppointment(appointment:DashboardAppointment):void {
    if (appointment.status === 'confirmed') {
      this.router.navigate(['/appointments',appointment.id,'consultation']);
      return;
    }
    this.openAppointments();
  }
  openMessages():void{this.router.navigate(['/messages']);}
  openTasks():void{this.router.navigate(['/automations']);}
  openCheckins():void{this.router.navigate(['/clients']);}
  openCheckin(checkin:{clientId:number}):void{this.openClient(checkin.clientId);}
  openTask(task:DashboardTask):void {
    if (task.clientId) this.openClient(task.clientId);
    else this.openTasks();
  }

  priorityLabel(priority:string):string {
    const labels:Record<string,string>={high:'Alta',medium:'Media',low:'Baja'};
    return labels[priority] ?? priority;
  }

  isOverdue(task:DashboardTask):boolean {
    return !!task.dueAt && new Date(task.dueAt).getTime() < Date.now();
  }
}
