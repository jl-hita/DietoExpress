import { Component, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { NutritionistDeactivationDialogComponent } from './nutritionist-deactivation-dialog.component';
import { ClinicService, ClinicDashboard } from '../../servicios/clinic.service';
@Component({selector:'app-clinic-dashboard',standalone:true,imports:[CommonModule,FormsModule,MatCardModule,MatButtonModule,MatIconModule,MatTableModule,MatFormFieldModule,MatSelectModule,MatSnackBarModule,MatDialogModule],templateUrl:'./clinic-dashboard.component.html',styleUrls:['./clinic-dashboard.component.css']})
export class ClinicDashboardComponent implements OnInit {
 data?:ClinicDashboard; showCreate=false; newNutri:any={username:'',fullName:'',email:'',password:''};
 constructor(private clinic:ClinicService,private snack:MatSnackBar,private dialog:MatDialog){}
 ngOnInit(){this.load();}
 load(){this.clinic.getDashboard().subscribe({next:d=>this.data=d,error:e=>this.snack.open(e?.error||'No se puede cargar el panel de clínica','Cerrar',{duration:4000})});}
 createNutri(){this.clinic.createNutritionist(this.newNutri).subscribe({next:()=>{this.snack.open('Nutricionista creado','OK',{duration:2500});this.newNutri={username:'',fullName:'',email:'',password:''};this.showCreate=false;this.load()},error:e=>this.snack.open(e?.error||'No se pudo crear','Cerrar',{duration:4000})});}
 disableNutri(n:any){
   this.clinic.getDeactivationPreview(n.id).subscribe({
     next:preview=>{
       if(!preview.clients.length){
         this.clinic.disableNutritionist(n.id,{assignments:[]}).subscribe({next:()=>{this.snack.open('Nutricionista archivado','OK',{duration:2500});this.load()},error:e=>this.snack.open(e?.error?.message||e?.error||'No se pudo archivar','Cerrar',{duration:4000})});
         return;
       }
       const ref=this.dialog.open(NutritionistDeactivationDialogComponent,{width:'760px',maxWidth:'95vw',data:preview});
       ref.afterClosed().subscribe(result=>{
         if(!result)return;
         this.clinic.disableNutritionist(n.id,result).subscribe({next:()=>{this.snack.open('Nutricionista archivado y pacientes reasignados','OK',{duration:3000});this.load()},error:e=>this.snack.open(e?.error?.message||e?.error||'No se pudo archivar','Cerrar',{duration:5000})});
       });
     },
     error:e=>this.snack.open(e?.error?.message||e?.error||'No se pudo preparar el archivado','Cerrar',{duration:4000})
   });
 }
 assign(client:any,id:number|null){this.clinic.assignClient(client.id,id).subscribe({next:()=>this.load(),error:e=>{this.snack.open(e?.error||'No se pudo reasignar','Cerrar',{duration:4000});this.load()}});}
}