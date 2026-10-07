import { Component, OnInit } from '@angular/core';
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
  selector: 'app-patient-portal',
  standalone: true,
  imports: [CommonModule, FormsModule, DatePipe, DecimalPipe, MatIconModule, MatProgressSpinnerModule, SumPipe, PatientChatComponent, PatientCheckinComponent, PatientAppointmentsComponent, PatientProgressComponent, AccessLinkRecoveryComponent, PatientShoppingComponent, PatientHeaderComponent, PatientDocumentsComponent],
  templateUrl: './patient-portal.component.html',
  styleUrls: ['./patient-portal.component.css']
})
export class PatientPortalComponent implements OnInit {
  clientId?: number;
  /** True when the professional is previewing the portal instead of using the patient session. */
  isPreview = false;
  profile: any = null;
  activeDiet: any = null;
  shoppingList: PatientShoppingCategory[] = [];
  shoppingItemCount = 0;
  checkedShoppingItemCount = 0;
  loading = true;
  authError: string | null = null;
  showLogin = false;
  portalDataError: string | null = null;
  dietDataError: string | null = null;
  shoppingDataError: string | null = null;
  dietLoading = false;
  shoppingLoading = false;
  documents: PatientDocument[] = [];
  documentsLoading = false;
  documentsError: string | null = null;

  // Login form model (PIN/phone)
  emailOrPhone = '';
  passcode = '';
  submittingLogin = false;
  requestingAccessLink = false;
  accessLinkMessage: string | null = null;
  accessLinkError: string | null = null;

  appointments: PatientAppointment[] = [];
  appointmentSlots: AppointmentSlot[] = [];
  appointmentsLoading = false;
  appointmentSlotsLoading = false;
  appointmentError: string | null = null;
  appointmentBooking = false;
  appointmentSuccess: string | null = null;

  notifications: PatientNotification[] = [];
  notificationsOpen = false;
  notificationsLoading = false;
  pushSupported = false;
  pushEnabled = false;
  pushBusy = false;
  pushMessage: string | null = null;

  activeTab: ActiveTab = 'today';
  today = new Date();

  // Equivalencias de intercambios expandidas
  expandedExchangeId: string | null = null;
  exchangeFoodsCache: Record<number, any[]> = {};
  exchangeLoading = false;

  completedMeals: Record<string, boolean> = {};

  constructor(
    private route: ActivatedRoute,
    private router: Router,
    private portalService: PatientPortalService,
    private foodService: FoodService
  ) {}

  // El portal puede entrar mediante sesión previa o token de acceso; después carga los datos clínicos y habilita las funciones del paciente.
  ngOnInit(): void {
    this.route.queryParams.subscribe(params => {
      const token = params['token'];
      const clientIdParam = params['clientId'] ? +params['clientId'] : undefined;

      if (token) {
        this.authenticateWithToken(token);
      } else if (clientIdParam) {
        // Modo vista previa nutricionista si hay sesión iniciada
        this.clientId = clientIdParam;
        this.loadData(clientIdParam);
      } else {
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
    this.portalService.clearPatientSession().subscribe({
      next: () => this.resetPatientView(),
      error: () => this.resetPatientView()
    });
  }

  private resetPatientView(): void {
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
    this.portalService.downloadMyDocument(doc.id).subscribe({
      next: blob => {
        const url = URL.createObjectURL(blob);
        const link = globalThis.document.createElement('a');
        link.href = url;
        link.download = doc.originalFileName || doc.name + '.pdf';
        link.click();
        URL.revokeObjectURL(url);
      },
      error: err => this.documentsError = err?.error?.message || 'No hemos podido descargar el documento.'
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
    navigator.serviceWorker.register('/sw.js').then(registration => {
      this.pushEnabled = !!registration.pushManager;
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

  private urlBase64ToUint8Array(value: string): Uint8Array {
    const padding = '='.repeat((4 - value.length % 4) % 4);
    const base64 = (value + padding).replace(/-/g, '+').replace(/_/g, '/');
    const raw = window.atob(base64);
    return Uint8Array.from([...raw].map(char => char.charCodeAt(0)));
  }

  setTab(tab: ActiveTab): void {
    this.activeTab = tab;
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
    this.completedMeals[key] = !this.completedMeals[key];
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
          this.exchangeFoodsCache[groupId] = foods || [];
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
