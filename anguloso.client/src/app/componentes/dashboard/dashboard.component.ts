import { CommonModule } from '@angular/common';
import { Component, OnInit } from '@angular/core';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { ProfessionalDashboard, ProfessionalDashboardService } from '../../servicios/professional-dashboard.service';

@Component({
  selector:'app-dashboard',
  standalone:true,
  imports:[CommonModule,MatCardModule,MatIconModule,MatButtonModule],
  templateUrl:'./dashboard.component.html',
  styleUrl:'./dashboard.component.css'
})
export class DashboardComponent implements OnInit {
  data:ProfessionalDashboard|null=null;
  loading=true;
  error:string|null=null;
  constructor(private service:ProfessionalDashboardService, private router:Router){}
  ngOnInit():void {
    this.service.get().subscribe({
      next:d=>{this.data=d;this.loading=false;},
      error:e=>{this.error=e?.error?.message||'No hemos podido cargar tu dashboard.';this.loading=false;}
    });
  }
  openClient(id:number):void{this.router.navigate(['/clients',id]);}
  openAppointments():void{this.router.navigate(['/appointments']);}
  openMessages():void{this.router.navigate(['/messages']);}
  openTasks():void{this.router.navigate(['/automations']);}
}
