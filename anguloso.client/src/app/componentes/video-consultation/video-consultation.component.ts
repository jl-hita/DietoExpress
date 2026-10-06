import { AfterViewInit, Component, ElementRef, OnDestroy, ViewChild } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import {
  createLocalAudioTrack,
  createLocalVideoTrack,
  LocalAudioTrack,
  LocalVideoTrack,
  RemoteTrack,
  RemoteTrackPublication,
  RemoteParticipant,
  Room,
  RoomEvent,
  Track
} from 'livekit-client';
import { PatientPortalService } from '../../servicios/patient-portal.service';

@Component({
  selector: 'app-video-consultation',
  standalone: true,
  templateUrl: './video-consultation.component.html',
  styleUrls: ['./video-consultation.component.css']
})
export class VideoConsultationComponent implements AfterViewInit, OnDestroy {
  @ViewChild('localVideo', { static: true }) localVideoElementRef!: ElementRef<HTMLVideoElement>;
  @ViewChild('remoteContainer', { static: true }) remoteContainer!: ElementRef<HTMLDivElement>;

  loading = true;
  connecting = false;
  connected = false;
  error = '';
  microphoneEnabled = true;
  cameraEnabled = true;
  quotaWarning = '';
  callTimeWarning = '';
  remainingSeconds = 0;

  private callTimer?: ReturnType<typeof setInterval>;

  private room?: Room;
  localAudio?: LocalAudioTrack;
  localVideoTrack?: LocalVideoTrack;
  private readonly remoteElements = new Map<string, HTMLElement>();
  private appointmentId = 0;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly portalService: PatientPortalService
  ) {}

  ngAfterViewInit(): void {
    const rawId = this.route.snapshot.paramMap.get('appointmentId');
    this.appointmentId = Number(rawId);
    if (!Number.isInteger(this.appointmentId) || this.appointmentId <= 0) {
      this.loading = false;
      this.error = 'La cita de consulta online no es válida.';
      return;
    }
    void this.connect();
  }

  async connect(): Promise<void> {
    this.loading = true;
    this.connecting = true;
    this.error = '';

    this.portalService.getVideoAccess(this.appointmentId).subscribe({
      next: access => {
        if (access.quota?.critical) {
          this.quotaWarning = 'La cuota de videollamadas está en nivel crítico. La consulta actual está autorizada, pero nuevas consultas podrían quedar bloqueadas.';
        } else if (access.quota?.warning) {
          this.quotaWarning = 'La cuota de videollamadas se está acercando a su límite mensual.';
        } else {
          this.quotaWarning = '';
        }
        this.startCallTimer(access.expiresAt);
        void this.connectToRoom(access.roomUrl, access.token);
      },
      error: err => {
        this.connecting = false;
        this.loading = false;
        this.error = err?.error?.message || 'No hemos podido autorizar la consulta online.';
      }
    });
  }

  private async connectToRoom(serverUrl: string, token: string): Promise<void> {
    try {
      if (!serverUrl.startsWith('wss://') && !serverUrl.startsWith('ws://')) {
        throw new Error('El servidor de videollamada no tiene una URL segura válida.');
      }

      this.room = new Room({
        adaptiveStream: true,
        dynacast: true
      });

      this.room
        .on(RoomEvent.TrackSubscribed, (track, publication, participant) => {
          this.attachRemoteTrack(track, publication, participant);
        })
        .on(RoomEvent.TrackUnsubscribed, track => {
          this.detachRemoteTrack(track);
        })
        .on(RoomEvent.Disconnected, () => {
          this.connected = false;
        });

      await this.room.connect(serverUrl, token);

      try {
        this.localVideoTrack = await createLocalVideoTrack();
        await this.room.localParticipant.publishTrack(this.localVideoTrack);
        this.localVideoTrack.attach(this.localVideoElementRef.nativeElement);
      } catch {
        this.cameraEnabled = false;
      }

      try {
        this.localAudio = await createLocalAudioTrack();
        await this.room.localParticipant.publishTrack(this.localAudio);
      } catch {
        this.microphoneEnabled = false;
      }

      this.connected = true;
    } catch (err: any) {
      this.error = err?.message || 'No hemos podido conectar con la consulta online.';
      this.connected = false;
      await this.room?.disconnect();
    } finally {
      this.connecting = false;
      this.loading = false;
    }
  }

  async toggleMicrophone(): Promise<void> {
    if (!this.room || !this.localAudio) return;

    const enabled = !this.microphoneEnabled;
    try {
      await this.room.localParticipant.setMicrophoneEnabled(enabled);
      this.microphoneEnabled = enabled;
    } catch {
      // Conservamos el estado visual anterior si LiveKit no puede cambiar el dispositivo.
    }
  }

  async toggleCamera(): Promise<void> {
    if (!this.room || !this.localVideoTrack) return;

    const enabled = !this.cameraEnabled;
    try {
      await this.room.localParticipant.setCameraEnabled(enabled);
      this.cameraEnabled = enabled;
    } catch {
      // Conservamos el estado visual anterior si LiveKit no puede cambiar el dispositivo.
    }
  }

  private startCallTimer(expiresAt: string): void {
    if (this.callTimer) clearInterval(this.callTimer);
    const deadline = new Date(expiresAt).getTime();

    const update = () => {
      const remaining = Math.max(0, deadline - Date.now());
      this.remainingSeconds = Math.ceil(remaining / 1000);

      if (this.remainingSeconds <= 0) {
        this.callTimeWarning = 'Se ha alcanzado el límite máximo de 60 minutos. La consulta ha terminado.';
        if (this.callTimer) clearInterval(this.callTimer);
        void this.room?.disconnect();
        this.connected = false;
        return;
      }

      if (this.remainingSeconds <= 60) {
        this.callTimeWarning = 'Queda menos de 1 minuto para alcanzar el límite máximo de la consulta.';
      } else if (this.remainingSeconds <= 5 * 60) {
        this.callTimeWarning = 'Quedan menos de 5 minutos para alcanzar el límite máximo de la consulta.';
      } else {
        this.callTimeWarning = '';
      }
    };

    update();
    this.callTimer = setInterval(update, 1000);
  }

  leave(): void {
    if (this.callTimer) clearInterval(this.callTimer);
    void this.room?.disconnect();
    window.close();
  }

  private attachRemoteTrack(
    track: RemoteTrack,
    publication: RemoteTrackPublication,
    participant: RemoteParticipant
  ): void {
    if (track.kind !== Track.Kind.Video && track.kind !== Track.Kind.Audio) return;

    const element = track.attach();
    element.autoplay = true;
    element.setAttribute('playsinline', 'true');

    if (track.kind === Track.Kind.Video) {
      element.classList.add('remote-video');
    } else {
      element.classList.add('remote-audio');
    }

    const wrapper = document.createElement('div');
    wrapper.className = 'remote-participant';
    wrapper.dataset['participant'] = participant.identity;
    wrapper.dataset['track'] = publication.trackSid || '';
    wrapper.appendChild(element);

    this.remoteContainer.nativeElement.appendChild(wrapper);
    this.remoteElements.set(publication.trackSid || participant.identity, wrapper);
  }

  private detachRemoteTrack(track: RemoteTrack): void {
    track.detach().forEach(element => element.remove());
  }

  ngOnDestroy(): void {
    if (this.callTimer) clearInterval(this.callTimer);
    this.localAudio?.stop();
    this.localVideoTrack?.stop();
    void this.room?.disconnect();
    this.remoteElements.clear();
  }
}
