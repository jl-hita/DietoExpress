import { Component, ChangeDetectionStrategy, Inject, OnDestroy, OnInit } from '@angular/core';
import { CommonModule, DOCUMENT } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { Meta, Title } from '@angular/platform-browser';
import { AuthService } from '../../servicios/auth.service';
import { AdminService } from '../../servicios/admin.service';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { PublicPlan, PublicPlansService, planFeatureLabels, yearlySavingsPercent } from '../../servicios/public-plans.service';
import { LANDING_CONTENT, publishableScreenshots, publishableTestimonials, publishableVideo } from './landing.content';

export interface LandingFaq { q: string; a: string; }
export interface LandingTool { icon: string; title: string; text: string; }

const SEO_TITLE = 'DietoExpress | Software para nutricionistas y entrenadores: dietas, entrenamiento y app para pacientes';
const SEO_DESCRIPTION = 'Genera dietas adaptadas y macrociclos de entrenamiento completos, gestiona agenda, videoconsultas y seguimiento, y ofrece a tus pacientes su propia app. Software para nutricionistas, entrenadores y clínicas.';
const JSON_LD_ID = 'landing-jsonld';

@Component({
  changeDetection: ChangeDetectionStrategy.Eager,
  selector: 'app-landing',
  standalone: true,
  imports: [CommonModule, RouterLink, MatButtonModule, MatIconModule],
  templateUrl: './landing.component.html',
  styleUrls: ['./landing.component.css']
})
// La landing mantiene su estado de presentación independiente de la lógica autenticada para que no pueda alterar el contexto de sesión.
export class LandingComponent implements OnInit, OnDestroy {
  // Planes reales desde el catálogo de cobro. Si no se pueden cargar, la sección vuelve a mostrar solo la invitación a empezar.
  plans: PublicPlan[] = [];
  billingInterval: 'monthly' | 'yearly' = 'yearly';

  // Vídeo, capturas y testimonios: se rellenan en landing.content.ts. Cada sección solo se muestra si tiene contenido publicable.
  readonly video = publishableVideo(LANDING_CONTENT);
  readonly screenshots = publishableScreenshots(LANDING_CONTENT);
  readonly testimonials = publishableTestimonials(LANDING_CONTENT);

  /** Pasos de «cómo funciona». */
  readonly steps: LandingTool[] = [
    { icon: 'person_add', title: 'Da de alta a tu paciente', text: 'Ficha, anamnesis, objetivos, alergias, intolerancias y preferencias en un único expediente. Tu paciente recibe un enlace seguro: sin contraseñas que recordar.' },
    { icon: 'auto_awesome', title: 'Genera su plan en minutos', text: 'Una dieta ajustada a su energía y sus restricciones, y un macrociclo de entrenamiento con todas sus rutinas. Tú lo revisas, lo retocas y lo publicas.' },
    { icon: 'monitoring', title: 'Acompaña y ajusta', text: 'El paciente lo sigue desde su móvil. Tú ves la evolución, recibes avisos cuando algo requiere tu criterio y adaptas el plan con datos reales.' }
  ];

  /** Resto de herramientas que completan la consulta. */
  readonly tools: LandingTool[] = [
    { icon: 'description', title: 'Documentos y consentimientos', text: 'Plantillas de documentación y consentimiento que el paciente lee y acepta desde su portal o su app.' },
    { icon: 'chat', title: 'Mensajería con pacientes', text: 'Conversaciones y avisos en un solo sitio, en lugar de WhatsApp, correo y notas sueltas.' },
    { icon: 'picture_as_pdf', title: 'PDF con tu marca', text: 'Dietas y documentos en PDF con tu logo y los datos de tu consulta.' },
    { icon: 'menu_book', title: 'Recetario y plantillas', text: 'Crea tus recetas y reutiliza dietas y rutinas como plantillas para no empezar de cero.' },
    { icon: 'shopping_cart', title: 'Lista de la compra', text: 'Se deriva de la dieta activa y avisa al paciente de los alérgenos de cada producto.' },
    { icon: 'insights', title: 'Estadísticas de tu consulta', text: 'Indicadores agregados para entender cómo evoluciona tu actividad.' },
    { icon: 'event_available', title: 'Google Calendar', text: 'Sincroniza tus citas con tu calendario habitual.' },
    { icon: 'travel_explore', title: 'Perfil público y reservas', text: 'Aparece en el directorio por ciudad, especialidad o consulta online y recibe solicitudes de cita.' }
  ];

  /** Preguntas frecuentes. La respuesta sobre la prueba se calcula con el catálogo real (ver faqs()). */
  private readonly baseFaqs: LandingFaq[] = [
    { q: '¿Tengo permanencia?', a: 'No. Puedes cancelar la renovación cuando quieras. Los precios se muestran en euros con el IVA incluido y el pago se gestiona de forma segura con Stripe.' },
    { q: '¿El generador decide por mí?', a: 'No. El generador propone una dieta o un macrociclo a partir de los datos del paciente, con sus avisos de calidad, y tú revisas, corriges y publicas. Las alergias o intolerancias desconocidas nunca se interpretan como ausencia.' },
    { q: '¿Mis pacientes tienen que instalar algo?', a: 'No es obligatorio. Acceden desde el navegador con un enlace seguro, en el móvil o en el ordenador. Además hay una app Android propia, ahora en prueba cerrada y pendiente de publicarse en Google Play.' },
    { q: '¿Puedo usarlo solo para nutrición o solo para entrenamiento?', a: 'Sí. El entrenamiento es un módulo opcional que se activa paciente a paciente. Si solo haces nutrición, el expediente funciona igual sin él.' },
    { q: '¿Las videoconsultas están incluidas?', a: 'Sí. Se abren desde la propia cita, con audio y vídeo, y el acceso se habilita 15 minutos antes de la hora. El paciente entra desde su portal o desde la app.' },
    { q: '¿Qué seguridad tienen los datos de mis pacientes?', a: 'Los datos están aislados por organización, los profesionales pueden proteger su cuenta con verificación en dos pasos, las operaciones sensibles quedan registradas y las copias de seguridad se almacenan cifradas. El producto está pensado para el RGPD, con consentimientos y documentación integrados.' },
    { q: '¿Sirve para una clínica con varios profesionales?', a: 'Sí. Puedes incorporar a tu equipo, asignar pacientes a cada profesional, compartir recetas y dietas y consultar el registro de accesos, manteniendo separada la información de cada paciente.' },
    { q: '¿Funciona también en iPhone?', a: 'La app nativa es hoy solo para Android. En iPhone, tus pacientes pueden usar el portal web desde el navegador.' }
  ];

  private previousTitle = '';
  private previousDescription: string | null = null;

  private getPostLoginRoute(role: string | null): string[] {
    switch (role) {
      case "superadmin": return ["/admin"];
      case "clinic_admin": return ["/clinic"];
      case "nutritionist": return ["/dashboard"];
      default: return ["/login"];
    }
  }
  constructor(
    private router: Router,
    private authService: AuthService,
    private adminService: AdminService,
    private publicPlans: PublicPlansService,
    private titleService: Title,
    private meta: Meta,
    @Inject(DOCUMENT) private document: Document
  ) {}

  ngOnInit(): void {
    if (this.authService.isLoggedIn()) {
      this.router.navigate(this.getPostLoginRoute(this.authService.getRole()));
      return;
    }

    this.applySeo();

    this.publicPlans.getPlans().subscribe({
      next: res => { this.plans = res.plans ?? []; this.applySeo(); },
      error: () => { this.plans = []; }
    });

    // En una instalación nueva, llevar directamente al asistente inicial.
    this.adminService.getSetupStatus().subscribe({
      next: (res) => {
        if (!res.isConfigured) {
          this.router.navigate(['/setup']);
        }
      },
      error: (err) => {
        console.error('No se pudo comprobar el estado de configuración', err);
      }
    });
  }

  ngOnDestroy(): void {
    this.document.getElementById(JSON_LD_ID)?.remove();
    if (this.previousDescription !== null) this.meta.updateTag({ name: 'description', content: this.previousDescription });
    if (this.previousTitle) this.meta.updateTag({ property: 'og:title', content: this.previousTitle });
  }

  /** Título, descripción, Open Graph y datos estructurados (aplicación + preguntas frecuentes). */
  private applySeo(): void {
    if (this.previousDescription === null) {
      this.previousDescription = this.meta.getTag('name="description"')?.content ?? '';
      this.previousTitle = this.meta.getTag('property="og:title"')?.content ?? '';
    }
    this.titleService.setTitle(SEO_TITLE);
    this.meta.updateTag({ name: 'description', content: SEO_DESCRIPTION });
    this.meta.updateTag({ property: 'og:title', content: SEO_TITLE });
    this.meta.updateTag({ property: 'og:description', content: SEO_DESCRIPTION });
    this.meta.updateTag({ property: 'og:type', content: 'website' });
    this.meta.updateTag({ property: 'og:locale', content: 'es_ES' });

    const data = {
      '@context': 'https://schema.org',
      '@graph': [
        {
          '@type': 'SoftwareApplication',
          name: 'DietoExpress',
          applicationCategory: 'HealthApplication',
          operatingSystem: 'Web, Android',
          description: SEO_DESCRIPTION,
          inLanguage: 'es'
        },
        {
          '@type': 'FAQPage',
          mainEntity: this.faqs().map(f => ({ '@type': 'Question', name: f.q, acceptedAnswer: { '@type': 'Answer', text: f.a } }))
        }
      ]
    };
    let script = this.document.getElementById(JSON_LD_ID) as HTMLScriptElement | null;
    if (!script) {
      script = this.document.createElement('script');
      script.id = JSON_LD_ID;
      script.type = 'application/ld+json';
      this.document.head.appendChild(script);
    }
    script.textContent = JSON.stringify(data).replace(/</g, '\\u003c');
  }

  crearCuenta(): void {
    this.router.navigate(['/crear-usuario']);
  }

  iniciarSesion(): void {
    this.router.navigate(['/login']);
  }

  // Con facturación anual el precio principal es la media mensual, que es lo que el profesional compara.
  displayedPrice(plan: PublicPlan): number {
    return this.billingInterval === 'yearly' ? plan.yearlyPrice / 12 : plan.monthlyPrice;
  }

  savings(plan: PublicPlan): number { return yearlySavingsPercent(plan); }

  hasYearlySavings(): boolean { return this.plans.some(plan => yearlySavingsPercent(plan) > 0); }

  maxSavings(): number { return Math.max(0, ...this.plans.map(plan => yearlySavingsPercent(plan))); }

  featureLabels(plan: PublicPlan): string[] { return planFeatureLabels(plan); }

  // Los días de prueba de los planes que la ofrecen (hoy solo el individual). Ninguno => no se menciona la prueba.
  trialPlan(): PublicPlan | undefined { return this.plans.find(plan => !!plan.trialDays && plan.trialDays > 0); }

  /** Texto único para la llamada a la acción principal: usa los días reales de prueba del catálogo cuando se conocen. */
  ctaLabel(): string {
    const days = this.trialPlan()?.trialDays;
    return days ? `Probar ${days} días gratis` : 'Probar gratis';
  }

  /** Línea de confianza bajo el botón principal. */
  trustLine(): string {
    return this.trialPlan() ? 'Sin tarjeta para empezar · Sin permanencia' : 'Sin permanencia · Cancela cuando quieras';
  }

  faqs(): LandingFaq[] {
    const trial = this.trialPlan();
    const first: LandingFaq = trial
      ? { q: '¿Cómo funciona la prueba gratuita?', a: `Tienes ${trial.trialDays} días para probar todas las funciones del plan ${trial.name}, sin introducir tarjeta. Cuando decidas continuar, activas el pago; si no, no se te cobra nada.` }
      : { q: '¿Puedo probarlo antes de pagar?', a: 'Puedes crear tu cuenta y explorar DietoExpress sin compromiso. Consulta en la sección de planes las condiciones vigentes de cada uno.' };
    return [first, ...this.baseFaqs];
  }

  isFeatured(plan: PublicPlan): boolean { return this.plans.length > 1 && plan.code === 'nutri_full'; }

  verPlanes(): void {
    this.router.navigate(['/crear-usuario']);
  }
}
