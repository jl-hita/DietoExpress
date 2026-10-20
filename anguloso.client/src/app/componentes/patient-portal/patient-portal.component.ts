import { Component, OnDestroy, OnInit, ChangeDetectionStrategy, signal} from '@angular/core';
import { Subscription, interval, startWith, switchMap, catchError, of } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router } from '@angular/router';
import { CommonModule, DatePipe, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { PatientPortalService } from '../../servicios/patient-portal.service';
import { FoodService } from '../../servicios/food.service';
import { SumPipe } from '../../shared/pipes/sum.pipe';
import { PatientChatComponent } from '../patient-chat/patient-chat.component';
import { AppointmentSlot, PatientAppointment, PatientNotification, PatientDocument } from '../../servicios/patient-portal.service';
import { PatientCheckinComponent } from './patient-checkin/patient-checkin.component';
import { PatientAppointmentsComponent } from './patient-appointments/patient-appointments.component';
import { PatientProgressComponent } from './patient-progress/patient-progress.component';
import { AccessLinkRecoveryComponent } from './access-link-recovery/access-link-recovery.component';
import { PatientShoppingCategory, PatientShoppingComponent } from './patient-shopping/patient-shopping.component';
import { PatientHeaderComponent } from './patient-header/patient-header.component';
import { PatientDocumentsComponent } from './patient-documents/patient-documents.component';

type ActiveTab = 'today' | 'shopping' | 'appointments' | 'progress' | 'messages' | 'documents';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-patient-portal',
  standalone: true,
  imports: [CommonModule, FormsModule, DatePipe, DecimalPipe, MatIconModule, MatProgressSpinnerModule, SumPipe, PatientChatComponent, PatientCheckinComponent, PatientAppointmentsComponent, PatientProgressComponent, AccessLinkRecoveryComponent, PatientShoppingComponent, PatientHeaderComponent, PatientDocumentsComponent],
  templateUrl: './patient-portal.component.html',
  styleUrls: ['./patient-portal.component.css']
})
export class PatientPortalComponent implements OnInit, OnDestroy {
  // Los estados que dependen de respuestas HTTP, APIs del navegador o callbacks asíncronos
  // se respaldan en signals; los getters/setters conservan los nombres de la plantilla.

  private readonly clientIdState = signal<number | undefined>(undefined);
  get clientId(): number | undefined { return this.clientIdState(); }
  set clientId(value: number | undefined) { this.clientIdState.set(value); }

  private readonly isPreviewState = signal(false);
  get isPreview(): boolean { return this.isPreviewState(); }
  set isPreview(value: boolean) { this.isPreviewState.set(value); }

  private readonly profileState = signal<any>(null);
  get profile(): any { return this.profileState(); }
  set profile(value: any) { this.profileState.set(value); }

  private readonly activeDietState = signal<any>(null);
  get activeDiet(): any { return this.activeDietState(); }
  set activeDiet(value: any) { this.activeDietState.set(value); }

  private readonly shoppingListState = signal<PatientShoppingCategory[]>([]);
  get shoppingList(): PatientShoppingCategory[] { return this.shoppingListState(); }
  set shoppingList(value: PatientShoppingCategory[]) { this.shoppingListState.set(value); }

  private readonly shoppingItemCountState = signal(0);
  get shoppingItemCount(): number { return this.shoppingItemCountState(); }
  set shoppingItemCount(value: number) { this.shoppingItemCountState.set(value); }

  private readonly checkedShoppingItemCountState = signal(0);
  get checkedShoppingItemCount(): number { return this.checkedShoppingItemCountState(); }
  set checkedShoppingItemCount(value: number) { this.checkedShoppingItemCountState.set(value); }

  private readonly loadingState = signal(true);
  get loading(): boolean { return this.loadingState(); }
  set loading(value: boolean) { this.loadingState.set(value); }

  private readonly authErrorState = signal<string | null>(null);
  get authError(): string | null { return this.authErrorState(); }
  set authError(value: string | null) { this.authErrorState.set(value); }

  private readonly showLoginState = signal(false);
  get showLogin(): boolean { return this.showLoginState(); }
  set showLogin(value: boolean) { this.showLoginState.set(value); }

  private readonly portalDataErrorState = signal<string | null>(null);
  get portalDataError(): string | null { return this.portalDataErrorState(); }
  set portalDataError(value: string | null) { this.portalDataErrorState.set(value); }

  private readonly dietDataErrorState = signal<string | null>(null);
  get dietDataError(): string | null { return this.dietDataErrorState(); }
  set dietDataError(value: string | null) { this.dietDataErrorState.set(value); }

  private readonly shoppingDataErrorState = signal<string | null>(null);
  get shoppingDataError(): string | null { return this.shoppingDataErrorState(); }
  set shoppingDataError(value: string | null) { this.shoppingDataErrorState.set(value); }

  private readonly dietLoadingState = signal(false);
  get dietLoading(): boolean { return this.dietLoadingState(); }
  set dietLoading(value: boolean) { this.dietLoadingState.set(value); }

  private readonly shoppingLoadingState = signal(false);
  get shoppingLoading(): boolean { return this.shoppingLoadingState(); }
  set shoppingLoading(value: boolean) { this.shoppingLoadingState.set(value); }

  private readonly documentsState = signal<PatientDocument[]>([]);
  get documents(): PatientDocument[] { return this.documentsState(); }
  set documents(value: PatientDocument[]) { this.documentsState.set(value); }

  private readonly documentsLoadingState = signal(false);
  get documentsLoading(): boolean { return this.documentsLoadingState(); }
  set documentsLoading(value: boolean) { this.documentsLoadingState.set(value); }

  private readonly documentsErrorState = signal<string | null>(null);
  get documentsError(): string | null { return this.documentsErrorState(); }
  set documentsError(value: string | null) { this.documentsErrorState.set(value); }

  // Login form models stay as ordinary fields because they are edited directly with ngModel.
  emailOrPhone = '';
  passcode = '';

  private readonly submittingLoginState = signal(false);
  get submittingLogin(): boolean { return this.submittingLoginState(); }
  set submittingLogin(value: boolean) { this.submittingLoginState.set(value); }

  private readonly requestingAccessLinkState = signal(false);
  get requestingAccessLink(): boolean { return this.requestingAccessLinkState(); }
  set requestingAccessLink(value: boolean) { this.requestingAccessLinkState.set(value); }

  private readonly accessLinkMessageState = signal<string | null>(null);
  get accessLinkMessage(): string | null { return this.accessLinkMessageState(); }
  set accessLinkMessage(value: string | null) { this.accessLinkMessageState.set(value); }

  private readonly accessLinkErrorState = signal<string | null>(null);
  get accessLinkError(): string | null { return this.accessLinkErrorState(); }
  set accessLinkError(value: string | null) { this.accessLinkErrorState.set(value); }

  private readonly appointmentsState = signal<PatientAppointment[]>([]);
  get appointments(): PatientAppointment[] { return this.appointmentsState(); }
  set appointments(value: PatientAppointment[]) { this.appointmentsState.set(value); }

  private readonly appointmentSlotsState = signal<AppointmentSlot[]>([]);
  get appointmentSlots(): AppointmentSlot[] { return this.appointmentSlotsState(); }
  set appointmentSlots(value: AppointmentSlot[]) { this.appointmentSlotsState.set(value); }

  private readonly appointmentsLoadingState = signal(false);
  get appointmentsLoading(): boolean { return this.appointmentsLoadingState(); }
  set appointmentsLoading(value: boolean) { this.appointmentsLoadingState.set(value); }

  private readonly appointmentSlotsLoadingState = signal(false);
  get appointmentSlotsLoading(): boolean { return this.appointmentSlotsLoadingState(); }
  set appointmentSlotsLoading(value: boolean) { this.appointmentSlotsLoadingState.set(value); }

  private readonly appointmentErrorState = signal<string | null>(null);
  get appointmentError(): string | null { return this.appointmentErrorState(); }
  set appointmentError(value: string | null) { this.appointmentErrorState.set(value); }

  private readonly appointmentBookingState = signal(false);
  get appointmentBooking(): boolean { return this.appointmentBookingState(); }
  set appointmentBooking(value: boolean) { this.appointmentBookingState.set(value); }

  private readonly appointmentSuccessState = signal<string | null>(null);
  get appointmentSuccess(): string | null { return this.appointmentSuccessState(); }
  set appointmentSuccess(value: string | null) { this.appointmentSuccessState.set(value); }

  private readonly notificationsState = signal<PatientNotification[]>([]);
  get notifications(): PatientNotification[] { return this.notificationsState(); }
  set notifications(value: PatientNotification[]) { this.notificationsState.set(value); }

  private readonly broadcastMessagesState = signal<PatientBroadcastMessage[]>([]);
  get broadcastMessages(): PatientBroadcastMessage[] { return this.broadcastMessagesState(); }
  set broadcastMessages(value: PatientBroadcastMessage[]) { this.broadcastMessagesState.set(value); }

  private readonly broadcastMessagesLoadingState = signal(false);
  get broadcastMessagesLoading(): boolean { return this.broadcastMessagesLoadingState(); }
  set broadcastMessagesLoading(value: boolean) { this.broadcastMessagesLoadingState.set(value); }

  private readonly broadcastMessagesErrorState = signal<string | null>(null);
  get broadcastMessagesError(): string | null { return this.broadcastMessagesErrorState(); }
  set broadcastMessagesError(value: string | null) { this.broadcastMessagesErrorState.set(value); }

  private routeQuerySubscription?: Subscription;
  private broadcastRefreshSubscription?: Subscription;
  private patientUnreadRefreshSubscription?: Subscription;
  private patientUnreadMessagesCountState = signal(0);
  get patientUnreadMessagesCount(): number { return this.patientUnreadMessagesCountState(); }
  set patientUnreadMessagesCount(value: number) { this.patientUnreadMessagesCountState.set(value); }

  private readonly notificationsOpenState = signal(false);
  get notificationsOpen(): boolean { return this.notificationsOpenState(); }
  set notificationsOpen(value: boolean) { this.notificationsOpenState.set(value); }

  private readonly notificationsLoadingState = signal(false);
  get notificationsLoading(): boolean { return this.notificationsLoadingState(); }
  set notificationsLoading(value: boolean) { this.notificationsLoadingState.set(value); }

  private readonly pushSupportedState = signal(false);
  get pushSupported(): boolean { return this.pushSupportedState(); }
  set pushSupported(value: boolean) { this.pushSupportedState.set(value); }

  private readonly pushEnabledState = signal(false);
  get pushEnabled(): boolean { return this.pushEnabledState(); }
  set pushEnabled(value: boolean) { this.pushEnabledState.set(value); }

  private readonly pushBusyState = signal(false);
  get pushBusy(): boolean { return this.pushBusyState(); }
  set pushBusy(value: boolean) { this.pushBusyState.set(value); }

  private readonly commercialEmailEnabledState = signal(false);
  get commercialEmailEnabled(): boolean { return this.commercialEmailEnabledState(); }
  set commercialEmailEnabled(value: boolean) { this.commercialEmailEnabledState.set(value); }

  private readonly commercialPreferenceLoadedState = signal(false);
  get commercialPreferenceLoaded(): boolean { return this.commercialPreferenceLoadedState(); }
  set commercialPreferenceLoaded(value: boolean) { this.commercialPreferenceLoadedState.set(value); }

  private readonly commercialPreferenceSavingState = signal(false);
  get commercialPreferenceSaving(): boolean { return this.commercialPreferenceSavingState(); }
  set commercialPreferenceSaving(value: boolean) { this.commercialPreferenceSavingState.set(value); }

  private readonly commercialPreferenceMessageState = signal<string | null>(null);
  get commercialPreferenceMessage(): string | null { return this.commercialPreferenceMessageState(); }
  set commercialPreferenceMessage(value: string | null) { this.commercialPreferenceMessageState.set(value); }

  private readonly pushMessageState = signal<string | null>(null);
  get pushMessage(): string | null { return this.pushMessageState(); }
  set pushMessage(value: string | null) { this.pushMessageState.set(value); }

  activeTab: ActiveTab = 'today';
  today = new Date();

  private readonly portalClockState = signal(Date.now());
  private readonly portalClockSubscription: Subscription;
  get portalClockNow(): number { return this.portalClockState(); }

  // Equivalencias de intercambios expandidas
  expandedExchangeId: string | null = null;

  private readonly exchangeFoodsCacheState = signal<Record<number, any[]>>({});
  get exchangeFoodsCache(): Record<number, any[]> { return this.exchangeFoodsCacheState(); }
  set exchangeFoodsCache(value: Record<number, any[]>) { this.exchangeFoodsCacheState.set(value); }

  private readonly exchangeLoadingState = signal(false);
  get exchangeLoading(): boolean { return this.exchangeLoadingState(); }
  set exchangeLoading(value: boolean) { this.exchangeLoadingState.set(value); }

  private readonly completedMealsState = signal<Record<string, boolean>>({});
  get completedMeals(): Record<string, boolean> { return this.completedMealsState(); }
  set completedMeals(value: Record<string, boolean>) { this.completedMealsState.set(value); }

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private portalService: PatientPortalService,
    private foodService: FoodService,
    private http: HttpClient
  ) {
    this.portalClockSubscription = interval(15_000).subscribe(() => this.portalClockState.set(Date.now()));
  }

  ngOnDestroy(): void {
    this.routeQuerySubscription?.unsubscribe();
    this.broadcastRefreshSubscription?.unsubscribe();
    this.patientUnreadRefreshSubscription?.unsubscribe();
    this.portalClockSubscription.unsubscribe();
  }

  // El portal puede entrar mediante sesión previa o token de acceso; después carga los datos clínicos y habilita las funciones del paciente.
  ngOnInit(): void {
    this.routeQuerySubscription = this.route.queryParams.subscribe(params => {
      const token = params['token'];
      const clientIdParam = params['clientId'] ? +params['clientId'] : undefined;

      if (token) {
        this.authenticateWithToken(token);
      } else if (clientIdParam) {
        // Modo vista previa nutricionista si hay sesión iniciada
        this.clientId = clientIdParam;
        this.loadData(clientIdParam);
      } else if (!this.clientId) {
        // Al consumir un enlace mágico se elimina el token de la URL.
        // La navegación actualiza queryParams; no debe volver a mostrar el login
        // si la autenticación ya ha establecido clientId.
        this.loading = false;
        this.showLogin = true;
      }
    });

  }

  authenticateWithToken(token: string): void {
    this.loading = true;
    this.authError = null;
    this.portalService.authenticate({ token }).subscribe({
      next: (res) => {
        this.clientId = res.clientId;
        this.showLogin = false;
        // El token mágico no debe permanecer en el historial del navegador.
        this.router.navigate([], { queryParams: {}, replaceUrl: true });
        this.loadData();
      },
      error: (err) => {
        this.loading = false;
        this.authError = err?.error?.message || 'El enlace de acceso ha expirado o no es válido.';
        this.showLogin = true;
      }
    });
  }

  requestNewAccessLink(email: string): void {
    const emailToUse = email.trim().toLowerCase();
    this.accessLinkMessage = null;
    this.accessLinkError = null;
    if (!emailToUse || !emailToUse.includes('@')) {
      this.accessLinkError = 'Introduce el email con el que estás registrado.';
      return;
    }

    this.requestingAccessLink = true;
    this.portalService.requestAccessLink(emailToUse).subscribe({
      next: (response) => {
        this.requestingAccessLink = false;
        this.accessLinkMessage = response?.message || 'Si el email corresponde a un paciente, recibirás un nuevo enlace de acceso.';
      },
      error: (err) => {
        this.requestingAccessLink = false;
        this.accessLinkError = err?.error?.message || 'No hemos podido enviar el enlace. Inténtalo de nuevo más tarde.';
      }
    });
  }

  submitPinLogin(): void {
    if (!this.emailOrPhone || !this.passcode) {
      this.authError = 'Por favor, introduce tu email/teléfono y tu PIN.';
      return;
    }

    this.submittingLogin = true;
    this.authError = null;
    this.portalService.authenticate({
      emailOrPhone: this.emailOrPhone,
      passcode: this.passcode
    }).subscribe({
      next: (res) => {
        this.clientId = res.clientId;
        this.submittingLogin = false;
        this.showLogin = false;
        this.loadData();
      },
      error: (err) => {
        this.submittingLogin = false;
        this.authError = err?.error?.message || 'Credenciales incorrectas.';
      }
    });
  }

  logoutPatient(): void {
    const clientId = this.clientId;
    this.portalService.clearPatientSession().subscribe({
      next: () => {
        this.clearLocalPatientState(clientId);
        this.resetPatientView();
      },
      error: () => {
        this.clearLocalPatientState(clientId);
        this.resetPatientView();
      }
    });
  }

  /**
   * Limpia los estados funcionales del paciente guardados en localStorage
   * (comidas completadas y checks de la lista de la compra) al cerrar sesión,
   * para evitar que persistan en un dispositivo compartido.
   */
  private clearLocalPatientState(clientId?: number): void {
    if (!clientId) return;
    const completedMealsPrefix = `completed_meals_${clientId}_`;
    const shoppingChecksKey = `shopping_${clientId}`;
    try {
      localStorage.removeItem(shoppingChecksKey);
      for (let index = localStorage.length - 1; index >= 0; index--) {
        const key = localStorage.key(index);
        if (key?.startsWith(completedMealsPrefix)) localStorage.removeItem(key);
      }
    } catch {
      // El portal sigue funcionando aunque el navegador no permita modificar localStorage.
    }
  }

  private resetPatientView(): void {
    this.broadcastRefreshSubscription?.unsubscribe();
    this.broadcastRefreshSubscription = undefined;
    this.patientUnreadRefreshSubscription?.unsubscribe();
    this.patientUnreadRefreshSubscription = undefined;
    this.patientUnreadMessagesCount = 0;
    this.profile = null;
    this.activeDiet = null;
    this.shoppingList = [];
    this.dietDataError = null;
    this.shoppingDataError = null;
    this.dietLoading = false;
    this.shoppingLoading = false;
    this.portalDataError = null;
    this.appointments = [];
    this.appointmentSlots = [];
    this.appointmentsLoading = false;
    this.appointmentSlotsLoading = false;
    this.appointmentError = null;
    this.appointmentBooking = false;
    this.appointmentSuccess = null;
    this.documents = [];
    this.documentsLoading = false;
    this.documentsError = null;
    this.notifications = [];
    this.broadcastMessages = [];
    this.broadcastMessagesLoading = false;
    this.broadcastMessagesError = null;
    this.notificationsOpen = false;
    this.notificationsLoading = false;
    this.completedMeals = {};
    this.isPreview = false;
    this.clientId = undefined;
    this.showLogin = true;
  }

  loadData(clientIdParam?: number): void {
    this.isPreview = !!clientIdParam;
    this.loading = true;
    this.portalService.getMyProfile(clientIdParam).subscribe({
      next: (p) => {
        this.profile = p;
        this.portalDataError = null;
        if (p.id) this.clientId = p.id;
        this.loadCompletedMeals();
        // La vista previa del profesional usa clientId y no tiene sesión de paciente.
        if (!clientIdParam) {
          this.loadAppointments();
          this.loadNotifications();
          this.startBroadcastPolling();
          this.startPatientUnreadMessagesPolling();
          this.loadCommercialPreference();
          this.loadDocuments();
          this.preparePushSupport();
        }
        if (p.hasActiveDiet) {
          this.loadActiveDiet(clientIdParam);
          this.loadShoppingList(clientIdParam);
        } else {
          this.activeDiet = null;
          this.shoppingList = [];
          this.loading = false;
        }
      },
      error: (err) => {
        this.loading = false;
        this.portalDataError = err?.error?.message || 'No hemos podido cargar tus datos. Inténtalo de nuevo.';
        this.showLogin = false;
      }
    });
  }

  retryLoadData(): void {
    this.portalDataError = null;
    this.loadData(this.isPreview ? this.clientId : undefined);
  }

  loadActiveDiet(clientIdParam?: number): void {
    this.dietLoading = true;
    this.dietDataError = null;
    this.portalService.getMyActiveDiet(clientIdParam).subscribe({
      next: (d) => {
        this.activeDiet = d;
        this.dietLoading = false;
        this.loading = false;
      },
      error: (err) => {
        this.activeDiet = null;
        this.dietLoading = false;
        this.loading = false;
        this.dietDataError = err?.error?.message || 'No hemos podido cargar tu plan alimentario.';
      }
    });
  }

  loadShoppingList(clientIdParam?: number): void {
    this.shoppingLoading = true;
    this.shoppingDataError = null;
    this.portalService.getMyShoppingList(clientIdParam).subscribe({
      next: (s) => {
        this.shoppingList = s || [];
        this.shoppingLoading = false;
      },
      error: (err) => {
        this.shoppingList = [];
        this.shoppingLoading = false;
        this.shoppingDataError = err?.error?.message || 'No hemos podido cargar tu lista de la compra.';
      }
    });
  }

  loadAppointments(): void {
    this.appointmentsLoading = true;
    this.appointmentError = null;
    this.portalService.getMyAppointments().subscribe({
      next: (items) => {
        this.appointments = items || [];
        this.appointmentsLoading = false;
      },
      error: (err) => {
        this.appointments = [];
        this.appointmentsLoading = false;
        this.appointmentError = err?.error?.message || 'No hemos podido cargar tus citas.';
      }
    });
  }

  loadAppointmentSlots(): void {
    this.appointmentSlotsLoading = true;
    this.appointmentError = null;
    this.portalService.getAppointmentSlots(30).subscribe({
      next: (slots) => {
        this.appointmentSlots = slots || [];
        this.appointmentSlotsLoading = false;
      },
      error: (err) => {
        this.appointmentSlots = [];
        this.appointmentSlotsLoading = false;
        this.appointmentError = err?.error?.message || 'No hemos podido cargar los horarios disponibles.';
      }
    });
  }

  openAppointments(): void {
    this.activeTab = 'appointments';
    this.appointmentSuccess = null;
    if (!this.appointmentSlots.length && !this.appointmentSlotsLoading) this.loadAppointmentSlots();
    if (!this.appointments.length && !this.appointmentsLoading) this.loadAppointments();
  }

  requestAppointment(selection: { slot: AppointmentSlot; modality: 'in_person' | 'online' }): void {
    const slot = selection.slot;
    if (this.appointmentBooking) return;
    this.appointmentBooking = true;
    this.appointmentError = null;
    this.appointmentSuccess = null;
    this.portalService.requestAppointment(slot.startsAt, 30, null, selection.modality).subscribe({
      next: (appointment) => {
        this.appointmentBooking = false;
        this.appointmentSlots = this.appointmentSlots.filter(s => s.startsAt !== slot.startsAt);
        this.appointments = [appointment, ...this.appointments];
        this.appointmentSuccess = 'Solicitud de cita enviada. Tu nutricionista deberá confirmarla.';
      },
      error: (err) => {
        this.appointmentBooking = false;
        this.appointmentError = err?.error?.message || 'No hemos podido solicitar la cita. Actualiza los horarios e inténtalo de nuevo.';
        this.loadAppointmentSlots();
      }
    });
  }

  joinVideoAppointment(appointment: PatientAppointment): void {
    this.portalService.openVideoAccess(appointment.id, err => {
      this.appointmentError = err?.error?.message || 'No hemos podido abrir la consulta online. Comprueba que la cita esté dentro de su ventana de acceso.';
    });
  }

  cancelAppointment(appointment: PatientAppointment): void {
    if (this.appointmentBooking) return;
    if (!confirm(`¿Cancelar la cita del ${new Intl.DateTimeFormat('es-ES', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(appointment.startsAt))}?`)) return;
    this.appointmentBooking = true;
    this.appointmentError = null;
    this.appointmentSuccess = null;
    this.portalService.cancelAppointment(appointment.id).subscribe({
      next: () => {
        this.appointmentBooking = false;
        appointment.status = 'cancelled';
        this.appointmentSuccess = 'Cita cancelada.';
      },
      error: (err) => {
        this.appointmentBooking = false;
        this.appointmentError = err?.error?.message || 'No hemos podido cancelar la cita.';
      }
    });
  }

  get nextOnlineAppointment(): PatientAppointment | null {
    const now = this.portalClockNow;
    return this.appointments
      .filter(a => a.status === 'confirmed' && a.modality === 'online' &&
        new Date(a.endsAt).getTime() + 15 * 60_000 > now)
      .sort((a, b) => new Date(a.startsAt).getTime() - new Date(b.startsAt).getTime())[0] ?? null;
  }

  homeVideoAccessState(appointment: PatientAppointment): 'waiting' | 'available' | 'expired' {
    const now = this.portalClockNow;
    const availableFrom = new Date(appointment.startsAt).getTime() - 15 * 60_000;
    const expiresAt = appointment.videoExpiresAt
      ? new Date(appointment.videoExpiresAt).getTime()
      : new Date(appointment.endsAt).getTime() + 15 * 60_000;
    if (now < availableFrom) return 'waiting';
    if (!Number.isFinite(expiresAt) || now >= expiresAt) return 'expired';
    return 'available';
  }

  homeVideoAccessCountdown(appointment: PatientAppointment): string {
    const availableFrom = new Date(appointment.startsAt).getTime() - 15 * 60_000;
    let totalMinutes = Math.max(0, Math.ceil((availableFrom - this.portalClockNow) / 60_000));
    const days = Math.floor(totalMinutes / 1440);
    totalMinutes -= days * 1440;
    const hours = Math.floor(totalMinutes / 60);
    const minutes = totalMinutes - hours * 60;
    const parts: string[] = [];
    if (days) parts.push(`${days} ${days === 1 ? 'día' : 'días'}`);
    if (hours) parts.push(`${hours} ${hours === 1 ? 'hora' : 'horas'}`);
    if (minutes || parts.length === 0) parts.push(`${minutes} ${minutes === 1 ? 'minuto' : 'minutos'}`);
    return parts.join(', ').replace(/, ([^,]*)$/, ' y $1');
  }

  get upcomingAppointments(): PatientAppointment[] {
    return this.appointments.filter(a => a.status === 'requested' || a.status === 'confirmed')
      .sort((a, b) => new Date(a.startsAt).getTime() - new Date(b.startsAt).getTime());
  }

  get pastAppointments(): PatientAppointment[] {
    return this.appointments.filter(a => a.status !== 'requested' && a.status !== 'confirmed')
      .sort((a, b) => new Date(b.startsAt).getTime() - new Date(a.startsAt).getTime());
  }

  retryDietLoad(): void {
    this.loadActiveDiet(this.isPreview ? this.clientId : undefined);
  }

  retryShoppingLoad(): void {
    this.loadShoppingList(this.isPreview ? this.clientId : undefined);
  }

  loadDocuments(): void {
    this.documentsLoading = true;
    this.documentsError = null;
    this.portalService.getMyDocuments().subscribe({
      next: docs => { this.documents = docs || []; this.documentsLoading = false; },
      error: err => { this.documents = []; this.documentsLoading = false; this.documentsError = err?.error?.message || 'No hemos podido cargar tus documentos.'; }
    });
  }

  openDocuments(): void {
    this.activeTab = 'documents';
    if (!this.documents.length && !this.documentsLoading) this.loadDocuments();
  }

  downloadDocument(doc: PatientDocument): void {
    // Abrir la pestaña de forma síncrona dentro del clic del paciente evita el bloqueo
    // de ventanas emergentes mientras esperamos la respuesta HTTP.
    const previewWindow = window.open('about:blank', '_blank');
    if (previewWindow) previewWindow.opener = null;

    this.portalService.downloadMyDocument(doc.id).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        if (previewWindow && !previewWindow.closed) {
          previewWindow.location.href = url;
        } else {
          // Alternativa accesible cuando el navegador bloquea la pestaña nueva.
          const link = globalThis.document.createElement('a');
          link.href = url;
          link.download = doc.originalFileName || doc.name + '.pdf';
          link.click();
        }
        // El endpoint solo responde tras registrar la visualización en auditoría.
        doc.viewedAt = new Date().toISOString();
        window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
      },
      error: err => {
        previewWindow?.close();
        this.documentsError = err?.error?.message || 'No hemos podido abrir el documento.';
      }
    });
  }

  acceptDocument(doc: PatientDocument): void {
    if (!doc.requiresSignature || doc.status === 'signed') return;
    this.portalService.acceptDocument(doc.id).subscribe({
      next: () => {
        doc.status = 'signed';
        doc.signedAt = new Date().toISOString();
      },
      error: err => this.documentsError = err?.error?.message || 'No hemos podido registrar la aceptación.'
    });
  }

  loadCommercialPreference(): void {
    if (this.isPreview || this.commercialPreferenceLoaded) return;
    this.portalService.getCommercialCommunicationPreference().subscribe({
      next: value => {
        this.commercialEmailEnabled = !!value?.emailEnabled;
        this.commercialPreferenceLoaded = true;
      },
      error: () => this.commercialPreferenceMessage = 'No hemos podido cargar tus preferencias comerciales.'
    });
  }

  updateCommercialPreference(): void {
    if (this.isPreview || this.commercialPreferenceSaving) return;
    this.commercialPreferenceSaving = true;
    this.commercialPreferenceMessage = null;
    this.portalService.updateCommercialCommunicationPreference(this.commercialEmailEnabled).subscribe({
      next: () => {
        this.commercialPreferenceSaving = false;
        this.commercialPreferenceLoaded = true;
        this.commercialPreferenceMessage = this.commercialEmailEnabled
          ? 'Has activado las comunicaciones comerciales por email.'
          : 'Has desactivado las comunicaciones comerciales por email.';
      },
      error: err => {
        this.commercialPreferenceSaving = false;
        this.commercialPreferenceMessage = err?.error?.message || 'No hemos podido guardar la preferencia.';
      }
    });
  }

  loadNotifications(): void {
    this.notificationsLoading = true;
    this.portalService.getNotifications().subscribe({
      next: items => { this.notifications = items || []; this.notificationsLoading = false; },
      error: () => { this.notifications = []; this.notificationsLoading = false; }
    });
  }

  toggleNotifications(): void {
    this.notificationsOpen = !this.notificationsOpen;
    if (this.notificationsOpen) this.loadNotifications();
  }

  get unreadNotificationCount(): number {
    return this.notifications.filter(n => !n.readAt).length;
  }

  markNotificationRead(notification: PatientNotification): void {
    if (notification.readAt) return;
    this.portalService.markNotificationRead(notification.id).subscribe({
      next: () => notification.readAt = new Date().toISOString()
    });
  }

  preparePushSupport(): void {
    this.pushSupported = typeof window !== 'undefined' &&
      'Notification' in window &&
      'serviceWorker' in navigator &&
      'PushManager' in window;
    if (!this.pushSupported) return;
    navigator.serviceWorker.getRegistration().then(async registration => {
      this.pushEnabled = !!(await registration?.pushManager.getSubscription());
    }).catch(() => this.pushEnabled = false);
  }

  // La suscripción Web Push se crea en el navegador y se registra en backend junto con sus claves públicas.
  async enablePushNotifications(): Promise<void> {
    if (!this.pushSupported || this.pushBusy) return;
    this.pushBusy = true;
    this.pushMessage = null;
    try {
      const permission = await Notification.requestPermission();
      if (permission !== 'granted') {
        this.pushMessage = 'Las notificaciones del navegador no están permitidas.';
        return;
      }

      const keyResponse = await this.portalService.getVapidPublicKey().toPromise();
      if (!keyResponse?.publicKey) {
        this.pushMessage = 'Las notificaciones push todavía no están configuradas en la clínica.';
        return;
      }

      // El registro persistente solo se crea tras la acción explícita y con permiso concedido.
      await navigator.serviceWorker.register('/sw.js');
      const registration = await navigator.serviceWorker.ready;
      let subscription = await registration.pushManager.getSubscription();
      if (!subscription) {
        subscription = await registration.pushManager.subscribe({
          userVisibleOnly: true,
          applicationServerKey: this.urlBase64ToUint8Array(keyResponse.publicKey)
        });
      }

      const json = subscription.toJSON();
      if (!json.endpoint || !json.keys?.['p256dh'] || !json.keys?.['auth']) {
        this.pushMessage = 'No se ha podido completar la suscripción del navegador.';
        return;
      }

      await this.portalService.registerPushSubscription({
        endpoint: json.endpoint,
        p256dh: json.keys['p256dh'],
        auth: json.keys['auth']
      }).toPromise();

      this.pushEnabled = true;
      this.pushMessage = 'Notificaciones activadas.';
    } catch {
      this.pushMessage = 'No se han podido activar las notificaciones en este navegador.';
    } finally {
      this.pushBusy = false;
    }
  }

  private urlBase64ToUint8Array(value: string): Uint8Array<ArrayBuffer> {
    const padding = '='.repeat((4 - value.length % 4) % 4);
    const base64 = (value + padding).replace(/-/g, '+').replace(/_/g, '/');
    const raw = window.atob(base64);
    const bytes = new Uint8Array(raw.length);
    for (let index = 0; index < raw.length; index++) {
      bytes[index] = raw.charCodeAt(index);
    }
    return bytes;
  }

  /** Carga el buzón de comunicados globales, separado del chat privado. */
  get unreadBroadcastMessagesCount(): number {
    return this.broadcastMessages.filter(message => !message.readAt).length;
  }

  get unreadMessagesCount(): number {
    return this.unreadBroadcastMessagesCount + this.patientUnreadMessagesCount;
  }

  private startPatientUnreadMessagesPolling(): void {
    if (this.patientUnreadRefreshSubscription || this.isPreview) return;
    this.patientUnreadRefreshSubscription = interval(15000).pipe(
      startWith(0),
      switchMap(() => this.http.get<number>('/api/messages/patient/unread-count').pipe(
        catchError(() => of(0))
      ))
    ).subscribe(count => this.patientUnreadMessagesCount = Math.max(0, Number(count) || 0));
  }

  // Mantiene actualizado el contador del menú del paciente sin exigir que entre manualmente en la pestaña Mensajes.
  private startBroadcastPolling(): void {
    if (this.broadcastRefreshSubscription) return;
    this.broadcastRefreshSubscription = interval(15000).pipe(
      startWith(0),
      switchMap(() => this.http.get<PatientBroadcastMessage[]>('/api/broadcast-messages/inbox').pipe(
        catchError(() => {
          this.broadcastMessagesError = 'No se han podido actualizar las comunicaciones.';
          return of(null);
        })
      ))
    ).subscribe(messages => {
      if (messages === null) return;
      this.broadcastMessages = messages || [];
      this.broadcastMessagesLoading = false;
      this.broadcastMessagesError = null;
    });
  }

  loadBroadcastMessages(): void {
    if (this.isPreview || this.showLogin) return;
    this.broadcastMessagesLoading = true;
    this.broadcastMessagesError = null;
    this.http.get<PatientBroadcastMessage[]>('/api/broadcast-messages/inbox').subscribe({
      next: messages => {
        this.broadcastMessages = messages || [];
        this.broadcastMessagesLoading = false;
      },
      error: () => {
        this.broadcastMessagesLoading = false;
        this.broadcastMessagesError = 'No se han podido cargar las comunicaciones. Inténtalo de nuevo.';
      }
    });
  }

  markBroadcastMessageRead(message: PatientBroadcastMessage): void {
    if (message.readAt) return;
    this.http.patch('/api/broadcast-messages/' + message.id + '/read', {}).subscribe({
      next: () => message.readAt = new Date().toISOString(),
      error: () => this.broadcastMessagesError = 'No se ha podido actualizar el estado de lectura.'
    });
  }

  setTab(tab: ActiveTab): void {
    this.activeTab = tab;
    if (tab === 'messages' && !this.isPreview) this.loadBroadcastMessages();
  }

  /** Lleva al paciente al directorio público cuando ya no tiene un nutricionista asignado. */
  findNutritionist(): void {
    this.router.navigate(['/nutricionistas']);
  }


  // Devuelve el día de la dieta que corresponde al día de la semana actual
  get todayDayData(): any | null {
    if (!this.activeDiet?.days?.length) return null;
    const dayOfWeek = this.today.getDay(); // 0=Dom, 1=Lun...
    const idx = dayOfWeek === 0 ? 6 : dayOfWeek - 1; // Ajustar a Lunes=0
    const day = this.activeDiet.days.find((d: any) => d.dayIndex === idx);
    return day ?? this.activeDiet.days[0]; // fallback al primer día
  }

  get dayName(): string {
    const names = ['Lunes','Martes','Miércoles','Jueves','Viernes','Sábado','Domingo'];
    const dow = this.today.getDay();
    return names[dow === 0 ? 6 : dow - 1];
  }

  getMealIcon(mealName: string): string {
    const n = mealName.toLowerCase();
    if (n.includes('desayuno')) return 'free_breakfast';
    if (n.includes('almuerzo') || n.includes('media')) return 'lunch_dining';
    if (n.includes('comida')) return 'restaurant';
    if (n.includes('merienda')) return 'cookie';
    if (n.includes('cena')) return 'dinner_dining';
    return 'food_bank';
  }

  mealKey(meal: any): string {
    return `${this.clientId ?? 'preview'}_${this.today.toISOString().slice(0, 10)}_${meal.mealIndex}`;
  }

  isMealCompleted(meal: any): boolean {
    return !!this.completedMeals[this.mealKey(meal)];
  }

  toggleMealCompleted(meal: any): void {
    const key = this.mealKey(meal);
    this.completedMeals = { ...this.completedMeals, [key]: !this.completedMeals[key] };
    this.saveCompletedMeals();
  }

  get completedMealCount(): number {
    return this.todayDayData?.meals?.filter((meal: any) => this.isMealCompleted(meal)).length ?? 0;
  }

  get todayMealCount(): number {
    return this.todayDayData?.meals?.length ?? 0;
  }

  get todayProgressPercent(): number {
    return this.todayMealCount ? Math.round((this.completedMealCount / this.todayMealCount) * 100) : 0;
  }

  get todayHasMeals(): boolean {
    return (this.todayDayData?.meals?.length ?? 0) > 0;
  }

  get todayKcal(): number {
    return this.todayDayData?.meals?.reduce((total: number, meal: any) =>
      total + (meal.items?.reduce((mealTotal: number, item: any) => mealTotal + Number(item.kcal || 0), 0) || 0), 0) ?? 0;
  }

  private loadCompletedMeals(): void {
    const saved = localStorage.getItem(`completed_meals_${this.clientId}_${this.today.toISOString().slice(0, 10)}`);
    if (saved) {
      try { this.completedMeals = JSON.parse(saved); } catch { this.completedMeals = {}; }
    }
  }

  private saveCompletedMeals(): void {
    localStorage.setItem(
      `completed_meals_${this.clientId}_${this.today.toISOString().slice(0, 10)}`,
      JSON.stringify(this.completedMeals)
    );
  }

  get bmi(): string | null {
    if (!this.profile?.currentWeight || !this.profile?.currentHeight) return null;
    const bmi = this.profile.currentWeight / Math.pow(this.profile.currentHeight / 100, 2);
    return bmi.toFixed(1);
  }

  get bmiCategory(): string {
    const b = parseFloat(this.bmi ?? '0');
    if (b < 18.5) return 'Bajo peso';
    if (b < 25) return 'Normopeso';
    if (b < 30) return 'Sobrepeso';
    return 'Obesidad';
  }

  get bmiColor(): string {
    const b = parseFloat(this.bmi ?? '0');
    if (b < 18.5) return '#3B82F6';
    if (b < 25) return '#22C55E';
    if (b < 30) return '#F59E0B';
    return '#EF4444';
  }

  get weightHistory(): any[] {
    return this.profile?.weightHistory ?? [];
  }

  get firstRecordedWeight(): number | null {
    return this.weightHistory.length ? Number(this.weightHistory[0].weight) : null;
  }

  get latestRecordedWeight(): number | null {
    return this.weightHistory.length ? Number(this.weightHistory[this.weightHistory.length - 1].weight) : null;
  }

  get weightChange(): number | null {
    if (this.firstRecordedWeight === null || this.latestRecordedWeight === null || this.weightHistory.length < 2) return null;
    return Number((this.latestRecordedWeight - this.firstRecordedWeight).toFixed(1));
  }

  get weightChangeLabel(): string {
    return this.weightChange === null ? 'Sin historial suficiente' : 'Desde el primer registro';
  }

  get weightChangeIcon(): string {
    if (this.weightChange === null || this.weightChange === 0) return 'horizontal_rule';
    return this.weightChange < 0 ? 'south_east' : 'north_east';
  }

  get weightChangeText(): string {
    if (this.weightChange === null) return '—';
    if (this.weightChange === 0) return '0 kg';
    return Math.abs(this.weightChange).toFixed(1) + ' kg';
  }

  formatWeightDate(dateValue: string | Date): string {
    const date = new Date(dateValue);
    if (Number.isNaN(date.getTime())) return '';
    return new Intl.DateTimeFormat('es-ES', { day: '2-digit', month: 'short' }).format(date);
  }

  getWeightBarHeight(weight: number, history: any[]): number {
    if (!history?.length) return 0;
    const min = Math.min(...history.map((h: any) => h.weight));
    const max = Math.max(...history.map((h: any) => h.weight));
    if (max === min) return 60;
    return Math.round(((weight - min) / (max - min)) * 85 + 15);
  }

  get hasShoppingItems(): boolean {
    return this.shoppingItemCount > 0;
  }

  /** Actualiza el único dato de compra que necesita la navegación inferior. */
  onShoppingCountsChange(counts: { total: number; checked: number }): void {
    this.shoppingItemCount = counts.total;
    this.checkedShoppingItemCount = counts.checked;
  }

  toggleExchangeEquivalencies(uniqueKey: string, groupId?: number): void {
    if (this.expandedExchangeId === uniqueKey) {
      this.expandedExchangeId = null;
      return;
    }

    this.expandedExchangeId = uniqueKey;

    if (groupId && !this.exchangeFoodsCache[groupId]) {
      this.exchangeLoading = true;
      this.foodService.getFoodsInExchangeGroup(groupId).subscribe({
        next: (foods) => {
          this.exchangeFoodsCache = { ...this.exchangeFoodsCache, [groupId]: foods || [] };
          this.exchangeLoading = false;
        },
        error: () => {
          this.exchangeLoading = false;
        }
      });
    }
  }

  getExchangeFoods(groupId?: number): any[] {
    return groupId ? (this.exchangeFoodsCache[groupId] || []) : [];
  }
}


/** Comunicación masiva asignada individualmente al paciente autenticado. */
export interface PatientBroadcastMessage {
  id: number;
  title: string;
  body: string;
  audience: string;
  createdAt: string;
  readAt?: string | null;
}
