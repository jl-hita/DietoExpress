import { AfterViewInit, Component, ElementRef, EventEmitter, Input, OnDestroy, Output, ViewChild, ChangeDetectionStrategy, signal} from '@angular/core';
import { CommonModule, Location } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { ActivatedRoute, Router } from '@angular/router';
import { createLocalAudioTrack, createLocalVideoTrack, LocalAudioTrack, LocalVideoTrack, RemoteTrack, RemoteTrackPublication, RemoteParticipant, Room, RoomEvent, Track } from 'livekit-client';
import { PatientPortalService } from '../../servicios/patient-portal.service';

type ConnectionState = 'scheduled' | 'connecting' | 'waiting' | 'connected' | 'reconnecting' | 'disconnected' | 'finalized' | 'error';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-video-consultation',
  standalone: true,
  imports: [CommonModule, MatIconModule],
  templateUrl: './video-consultation.component.html',
  styleUrls: ['./video-consultation.component.css'],
  host: { '[class.embedded]': 'embedded' }
})
export class VideoConsultationComponent implements AfterViewInit, OnDestroy {
  @Input() embedded = false;
  @Input() appointmentIdInput: number | null = null;
  @Input() appointmentStartsAtInput: string | null = null;
  @Output() closeEmbedded = new EventEmitter<void>();
  compactVideo = false;
  @ViewChild('localVideo') localVideoElementRef?: ElementRef<HTMLVideoElement>;
  @ViewChild('remoteContainer') remoteContainer?: ElementRef<HTMLDivElement>;
  // LiveKit y el temporizador pueden notificar cambios desde callbacks externos a Angular.
  // Los signals mantienen la vista reactiva sin alterar los nombres usados por la plantilla.
  private readonly loadingState = signal(true);
  get loading(): boolean { return this.loadingState(); }
  set loading(value: boolean) { this.loadingState.set(value); }

  private readonly connectingState = signal(false);
  get connecting(): boolean { return this.connectingState(); }
  set connecting(value: boolean) { this.connectingState.set(value); }

  private readonly connectedState = signal(false);
  get connected(): boolean { return this.connectedState(); }
  set connected(value: boolean) { this.connectedState.set(value); }

  private readonly errorState = signal('');
  get error(): string { return this.errorState(); }
  set error(value: string) { this.errorState.set(value); }

  private readonly microphoneEnabledState = signal(true);
  get microphoneEnabled(): boolean { return this.microphoneEnabledState(); }
  set microphoneEnabled(value: boolean) { this.microphoneEnabledState.set(value); }

  private readonly cameraEnabledState = signal(true);
  get cameraEnabled(): boolean { return this.cameraEnabledState(); }
  set cameraEnabled(value: boolean) { this.cameraEnabledState.set(value); }

  private readonly quotaWarningState = signal('');
  get quotaWarning(): string { return this.quotaWarningState(); }
  set quotaWarning(value: string) { this.quotaWarningState.set(value); }

  private readonly callTimeWarningState = signal('');
  get callTimeWarning(): string { return this.callTimeWarningState(); }
  set callTimeWarning(value: string) { this.callTimeWarningState.set(value); }

  private readonly remainingSecondsState = signal(0);
  get remainingSeconds(): number { return this.remainingSecondsState(); }
  set remainingSeconds(value: number) { this.remainingSecondsState.set(value); }

  private readonly connectionStateState = signal<ConnectionState>('connecting');
  get connectionState(): ConnectionState { return this.connectionStateState(); }
  set connectionState(value: ConnectionState) { this.connectionStateState.set(value); }

  private readonly remoteParticipantCountState = signal(0);
  get remoteParticipantCount(): number { return this.remoteParticipantCountState(); }
  set remoteParticipantCount(value: number) { this.remoteParticipantCountState.set(value); }

  private readonly canFinalizeState = signal(false);
  get canFinalize(): boolean { return this.canFinalizeState(); }
  set canFinalize(value: boolean) { this.canFinalizeState.set(value); }

  private readonly finalizingState = signal(false);
  get finalizing(): boolean { return this.finalizingState(); }
  set finalizing(value: boolean) { this.finalizingState.set(value); }

  private readonly maxCallDurationMinutesState = signal(60);
  get maxCallDurationMinutes(): number { return this.maxCallDurationMinutesState(); }
  set maxCallDurationMinutes(value: number) { this.maxCallDurationMinutesState.set(value); }
  private callTimer?: ReturnType<typeof setInterval>; private accessWindowTimer?: ReturnType<typeof setInterval>; private manualDisconnect = false; private room?: Room; private appointmentId = 0;
  private readonly localAudioState = signal<LocalAudioTrack | undefined>(undefined);
  get localAudio(): LocalAudioTrack | undefined { return this.localAudioState(); }
  set localAudio(value: LocalAudioTrack | undefined) { this.localAudioState.set(value); }

  private readonly localVideoTrackState = signal<LocalVideoTrack | undefined>(undefined);
  get localVideoTrack(): LocalVideoTrack | undefined { return this.localVideoTrackState(); }
  set localVideoTrack(value: LocalVideoTrack | undefined) { this.localVideoTrackState.set(value); }

  private readonly remoteElements = new Map<string, HTMLElement>();

  constructor(private readonly route: ActivatedRoute, private readonly router: Router, private readonly portalService: PatientPortalService, private readonly location: Location) {}

  ngAfterViewInit(): void {
    const rawId = this.appointmentIdInput ?? this.route.snapshot.paramMap.get('appointmentId'); this.appointmentId = Number(rawId);
    if (!Number.isInteger(this.appointmentId) || this.appointmentId <= 0) { this.loading = false; this.connectionState = 'error'; this.error = 'La cita de consulta online no es válida.'; return; }
    const startsAt = this.appointmentStartsAtInput ? new Date(this.appointmentStartsAtInput).getTime() : Number.NaN;
    const opensAt = startsAt - 15 * 60 * 1000;
    if (this.embedded && Number.isFinite(startsAt) && Date.now() < opensAt) {
      this.loading = false;
      this.connectionState = 'scheduled';
      this.accessWindowTimer = setInterval(() => {
        if (Date.now() >= opensAt) {
          if (this.accessWindowTimer) clearInterval(this.accessWindowTimer);
          this.accessWindowTimer = undefined;
          void this.connect();
        }
      }, 15000);
      return;
    }
    void this.connect();
  }

  get connectionStateLabel(): string {
    return ({ scheduled:'Acceso aún no disponible', connecting:'Conectando', waiting:'Esperando al otro participante', connected:'Conectado', reconnecting:'Reconectando', disconnected:'Conexión perdida', finalized:'Consulta finalizada', error:'Error de conexión' } as Record<ConnectionState,string>)[this.connectionState];
  }

  connect(): void {
    this.loading = true; this.connecting = true; this.error = ''; this.connectionState = 'connecting'; this.manualDisconnect = false;
    if (this.room) { this.manualDisconnect = true; void this.room.disconnect(); this.room = undefined; this.manualDisconnect = false; }
    this.portalService.getVideoAccess(this.appointmentId).subscribe({
      next: access => {
        this.canFinalize = !!access.canFinalize; this.maxCallDurationMinutes = access.maxCallDurationMinutes || 60;
        if (access.quota?.critical) this.quotaWarning = 'La cuota de videollamadas está en nivel crítico. La consulta actual está autorizada, pero nuevas consultas podrían quedar bloqueadas.';
        else if (access.quota?.warning) this.quotaWarning = 'La cuota de videollamadas se está acercando a su límite mensual.';
        else this.quotaWarning = '';
        this.startCallTimer(access.expiresAt); void this.connectToRoom(access.roomUrl, access.token);
      },
      error: err => { this.connecting = false; this.loading = false; this.connectionState = 'error'; this.error = err?.error?.message || 'No hemos podido autorizar la consulta online.'; }
    });
  }

  private async connectToRoom(serverUrl: string, token: string): Promise<void> {
    try {
      if (!serverUrl.startsWith('wss://') && !serverUrl.startsWith('ws://')) throw new Error('El servidor de videollamada no tiene una URL segura válida.');
      this.room = new Room({ adaptiveStream: true, dynacast: true });
      this.room
        .on(RoomEvent.TrackSubscribed, (track, publication, participant) => this.attachRemoteTrack(track, publication, participant))
        .on(RoomEvent.TrackUnsubscribed, track => this.detachRemoteTrack(track))
        .on(RoomEvent.Reconnecting, () => { this.connectionState='reconnecting'; this.connected=false; this.recordVideoEvent('reconnecting'); })
        .on(RoomEvent.Reconnected, () => { this.connected=true; this.connectionState=this.remoteParticipantCount>0?'connected':'waiting'; this.recordVideoEvent('reconnected'); })
        .on(RoomEvent.ParticipantConnected, () => { this.remoteParticipantCount=this.room?.remoteParticipants.size??0; this.connectionState='connected'; this.connected=true; })
        .on(RoomEvent.ParticipantDisconnected, () => { this.remoteParticipantCount=this.room?.remoteParticipants.size??0; if(this.connectionState!=='finalized') this.connectionState=this.remoteParticipantCount>0?'connected':'waiting'; })
        .on(RoomEvent.Disconnected, () => { if(this.manualDisconnect||this.connectionState==='finalized') return; this.connected=false; this.connectionState='disconnected'; this.recordVideoEvent('disconnected'); });
      await this.room.connect(serverUrl, token);
      this.remoteParticipantCount=this.room.remoteParticipants.size;
      try { this.localVideoTrack=await createLocalVideoTrack(); await this.room.localParticipant.publishTrack(this.localVideoTrack); const localVideoElement = this.localVideoElementRef?.nativeElement;
        if (!localVideoElement) throw new Error('No se ha podido preparar el vídeo local. Vuelve a intentarlo.');
        this.localVideoTrack.attach(localVideoElement); } catch { this.cameraEnabled=false; }
      try { this.localAudio=await createLocalAudioTrack(); await this.room.localParticipant.publishTrack(this.localAudio); } catch { this.microphoneEnabled=false; }
      this.connected=true; this.connectionState=this.remoteParticipantCount>0?'connected':'waiting'; this.recordVideoEvent('connected');
    } catch(err:any) {
      this.error=err?.message||'No hemos podido conectar con la consulta online.'; this.connectionState='error'; this.connected=false; this.recordVideoEvent('failed'); await this.room?.disconnect();
    } finally { this.connecting=false; this.loading=false; }
  }

  async toggleMicrophone(): Promise<void> { if(!this.room||!this.localAudio)return; const enabled=!this.microphoneEnabled; try{await this.room.localParticipant.setMicrophoneEnabled(enabled);this.microphoneEnabled=enabled;}catch{/* Conservamos el estado visual anterior si LiveKit no puede cambiar el dispositivo. */} }
  async toggleCamera(): Promise<void> { if(!this.room||!this.localVideoTrack)return; const enabled=!this.cameraEnabled; try{await this.room.localParticipant.setCameraEnabled(enabled);this.cameraEnabled=enabled;}catch{/* Conservamos el estado visual anterior si LiveKit no puede cambiar el dispositivo. */} }

  private startCallTimer(expiresAt:string):void {
    if(this.callTimer)clearInterval(this.callTimer); const deadline=new Date(expiresAt).getTime();
    const update=()=>{const remaining=Math.max(0,deadline-Date.now());this.remainingSeconds=Math.ceil(remaining/1000);
      if(this.remainingSeconds<=0){this.callTimeWarning='Se ha alcanzado el límite máximo de '+this.maxCallDurationMinutes+' minutos. La consulta ha terminado.';if(this.callTimer)clearInterval(this.callTimer);this.connectionState='finalized';this.connected=false;this.manualDisconnect=true;void this.room?.disconnect();return;}
      if(this.remainingSeconds<=60)this.callTimeWarning='Queda menos de 1 minuto para alcanzar el límite máximo de la consulta.';else if(this.remainingSeconds<=300)this.callTimeWarning='Quedan menos de 5 minutos para alcanzar el límite máximo de la consulta.';else this.callTimeWarning='';
    }; update(); this.callTimer=setInterval(update,1000);
  }

  retry():void{void this.connect();}

  private getReturnUrl(): string | null {
    const value = this.route.snapshot.queryParamMap.get('returnUrl');
    return value && value.startsWith('/appointments/') && value.includes('/consultation') ? value : null;
  }
  private returnToConsultation(): void {
    if (this.embedded) return;
    const returnUrl = this.getReturnUrl();
    if (returnUrl) { void this.router.navigateByUrl(returnUrl); return; }
    this.location.back();
  }

  finalize():void {
    if(!this.canFinalize||this.finalizing||this.connectionState==='finalized')return;
    if(!confirm('¿Finalizar la consulta online y marcar la cita como completada?'))return;
    this.finalizing=true;
    this.portalService.finishVideoConsultation(this.appointmentId).subscribe({
      next:()=>{this.connectionState='finalized';this.connected=false;this.manualDisconnect=true;if(this.callTimer)clearInterval(this.callTimer);void this.room?.disconnect();this.localAudio?.stop();this.localVideoTrack?.stop();this.finalizing=false;this.returnToConsultation();},
      error:err=>{this.error=err?.error?.message||'No hemos podido finalizar la consulta.';this.finalizing=false;}
    });
  }

  leave():void{if(this.callTimer)clearInterval(this.callTimer);if(this.accessWindowTimer)clearInterval(this.accessWindowTimer);this.accessWindowTimer=undefined;this.manualDisconnect=true;void this.room?.disconnect();this.localAudio?.stop();this.localVideoTrack?.stop();this.localAudio=undefined;this.localVideoTrack=undefined;this.connectionState=this.connectionState==='finalized'?'finalized':'disconnected';if(this.embedded){this.closeEmbedded.emit();return;}this.returnToConsultation();}
  private recordVideoEvent(event:'connected'|'reconnecting'|'reconnected'|'disconnected'|'failed'):void{this.portalService.recordVideoEvent(this.appointmentId,event).subscribe({error:()=>undefined});}
  private attachRemoteTrack(track:RemoteTrack,publication:RemoteTrackPublication,participant:RemoteParticipant):void{
    if(track.kind!==Track.Kind.Video&&track.kind!==Track.Kind.Audio)return;const element=track.attach();element.autoplay=true;element.setAttribute('playsinline','true');element.classList.add(track.kind===Track.Kind.Video?'remote-video':'remote-audio');
    const container = this.remoteContainer?.nativeElement;
    if (!container) {
      // La pista puede llegar mientras Angular está cambiando la vista (p. ej. al mostrar un error).
      // Evitamos que un ViewChild aún no disponible rompa toda la consulta.
      track.detach().forEach(node => node.remove());
      return;
    }
    const wrapper=document.createElement('div');wrapper.className='remote-participant';wrapper.dataset['participant']=participant.identity;wrapper.dataset['track']=publication.trackSid||'';wrapper.appendChild(element);container.appendChild(wrapper);this.remoteElements.set(publication.trackSid||participant.identity,wrapper);this.remoteParticipantCount=this.room?.remoteParticipants.size??this.remoteParticipantCount;this.connectionState='connected';
  }
  private detachRemoteTrack(track:RemoteTrack):void{track.detach().forEach(element=>element.remove());}
  ngOnDestroy():void{if(this.callTimer)clearInterval(this.callTimer);if(this.accessWindowTimer)clearInterval(this.accessWindowTimer);this.manualDisconnect=true;this.localAudio?.stop();this.localVideoTrack?.stop();void this.room?.disconnect();this.remoteElements.clear();}
}
