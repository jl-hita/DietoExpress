import { CommonModule } from '@angular/common';
import { Component, OnInit, ChangeDetectionStrategy, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { ProfessionalDashboard, ProfessionalDashboardService, DashboardAppointment, DashboardTask, DashboardFollowupAttention } from '../../servicios/professional-dashboard.service';
import { Profile } from '../../modelos/profile';
import { ProfileService } from '../../servicios/profile.service';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector:'app-dashboard',
  standalone:true,
  imports:[CommonModule,MatCardModule,MatIconModule,MatButtonModule,MatProgressSpinnerModule],
  templateUrl:'./dashboard.component.html',
  styleUrl:'./dashboard.component.css'
})
export class DashboardComponent implements OnInit {
  readonly data = signal<ProfessionalDashboard | null>(null);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);
  readonly profile = signal<Profile | null>(null);
  readonly followupAttention = signal<DashboardFollowupAttention[]>([]);
  readonly followupAttentionLoading = signal(true);
  readonly followupAttentionError = signal(false);
  constructor(private service:ProfessionalDashboardService, private router:Router, private profileService:ProfileService){}

  ngOnInit():void {
    this.reload();
    this.profileService.getProfile().subscribe({ next: p => this.profile.set(p), error: () => this.profile.set(null) });
  }

  get directoryMissingFields(): string[] {
    const p = this.profile();
    if (!p) return [];
    const missing: string[] = [];
    if (!p.fullName?.trim()) missing.push('tu nombre profesional completo');
    if (!p.directoryCity?.trim()) missing.push('la ciudad o municipio');
    if (!p.directoryProvince?.trim()) missing.push('la provincia');
    if (!p.directorySpecialties?.trim()) missing.push('tus especialidades');
    if (!p.directoryBio?.trim()) missing.push('una presentación profesional');
    return missing;
  }

  openDirectorySettings(): void { this.router.navigate(['/settings'], { queryParams: { tab: 'directorio' } }); }

  reload():void {
    this.reloadFollowupAttention();
    this.loading.set(true);
    this.error.set(null);
    this.service.get().subscribe({
      next:d=>{this.data.set(d);this.loading.set(false);},
      error:e=>{this.error.set(e?.error?.message||'No hemos podido cargar tu dashboard.');this.loading.set(false);}
    });
  }

  reloadFollowupAttention(): void {
    this.followupAttentionLoading.set(true);
    this.followupAttentionError.set(false);
    this.service.getFollowupAttention().subscribe({
      next: items => { this.followupAttention.set(items); this.followupAttentionLoading.set(false); },
      error: () => { this.followupAttention.set([]); this.followupAttentionError.set(true); this.followupAttentionLoading.set(false); }
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
