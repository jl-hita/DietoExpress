/**
 * Diccionario base (español). Es la fuente de las claves: `TranslationKey` se deriva de aquí y los demás idiomas están
 * tipados como `Dictionary`, así que olvidar una traducción es un error de compilación y no un texto en blanco en pantalla.
 *
 * Convenciones:
 *  - Claves en minúsculas separadas por puntos: `<zona>.<pantalla>.<texto>`.
 *  - Parámetros con llaves: `Hola, {name}`. Para plurales se pasa `count` y el texto lleva dos formas separadas por `|`
 *    (`{count} día|{count} días`): la primera para 1 y la segunda para el resto.
 */
export const es = {
  // ── Comunes ──────────────────────────────────────────────────────────────
  'common.language': 'Idioma',

  // ── Inicio de sesión (profesionales) ─────────────────────────────────────
  'login.eyebrow': 'BIENVENIDO DE NUEVO',
  'login.title': 'Inicia sesión',
  'login.subtitle': 'Accede a tu espacio profesional de DietoExpress.',
  'login.logoAlt': 'DietoExpress · Tu suite profesional de nutrición',
  'login.userLabel': 'Email o usuario',
  'login.passwordLabel': 'Contraseña',
  'login.submit': 'Iniciar sesión',
  'login.submitting': 'Entrando...',
  'login.orContinueWith': 'o continúa con',
  'login.googleTermsPrefix': 'Acepto las',
  'login.googleTermsLink': 'condiciones de contratación',
  'login.googleTermsSuffix': 'vigentes para crear una cuenta con Google.',
  'login.forgotPassword': '¿Has olvidado tu contraseña?',
  'login.noAccount': '¿Todavía no tienes cuenta?',
  'login.createAccount': 'Crear cuenta gratis',
  'login.backToHome': '← Volver a DietoExpress',
  'login.twoFactorHint': 'Introduce el código de 6 dígitos de tu app autenticadora. Si has perdido el móvil, usa uno de tus códigos de recuperación (formato XXXXX-XXXXX).',
  'login.twoFactorLabel': 'Código de verificación',
  'login.twoFactorSubmit': 'Verificar y entrar',
  'login.twoFactorSubmitting': 'Verificando...',
  'login.twoFactorBack': '← Volver a iniciar sesión',
  'login.welcome': 'Bienvenido {name}',
  'login.archivedAccount': 'Cuenta archivada. Puedes comunicarte con tu clínica desde soporte.',
  'login.googleNotConfigured': 'El acceso con Google no está configurado',
  'login.googleLoadFailed': 'No se pudo cargar el acceso con Google',
  'login.googleConfigFailed': 'No se pudo cargar la configuración de Google',
  'login.googleError': 'Error al autenticar con Google',
  'login.googleCredentialError': 'Error Google login',
  'login.googleTermsRequired': 'Para crear una cuenta con Google debes aceptar las condiciones de contratación vigentes.',
  'login.badCredentials': 'Usuario o contraseña incorrectos',
  'login.invalidCode': 'Código no válido.',

  // ── Portal del paciente: cabecera ────────────────────────────────────────
  'patient.header.clinicLogoAlt': 'Logo Clínica',
  'patient.header.defaultClinic': 'Consulta Nutricional',
  'patient.header.callClinic': 'Llamar a la clínica',
  'patient.header.notifications': 'Notificaciones',
  'patient.header.openNotifications': 'Abrir notificaciones',
  'patient.header.logout': 'Cerrar sesión',
  'patient.header.ageYears': '{age} años',
  'patient.header.activeDiet': 'Dieta activa',
  'patient.header.noActiveDiet': 'Sin dieta activa',

  // ── Portal del paciente: notificaciones ──────────────────────────────────
  'patient.notifications.title': 'Notificaciones',
  'patient.notifications.unread': '{count} sin leer',
  'patient.notifications.pushEnabled': 'Push activadas',
  'patient.notifications.pushEnabling': 'Activando…',
  'patient.notifications.enablePush': 'Activar avisos',
  'patient.notifications.empty': 'No tienes notificaciones.',

  // ── Portal del paciente: documentación ───────────────────────────────────
  'patient.documents.title': 'Documentación',
  'patient.documents.subtitle': 'Documentos compartidos por tu nutricionista',
  'patient.documents.emptyTitle': 'No tienes documentos todavía',
  'patient.documents.emptyText': 'Cuando tu nutricionista comparta documentación contigo aparecerá aquí.',
  'patient.documents.version': 'Versión {version}',
  'patient.documents.pendingSignature': 'Pendiente de firma/aceptación',
  'patient.documents.reviewFirst': 'Abre y revisa el PDF antes de aceptar',
  'patient.documents.accepted': 'Aceptado',
  'patient.documents.available': 'Disponible',
  'patient.documents.reopen': 'Volver a abrir PDF',
  'patient.documents.read': 'Leer documento',
  'patient.documents.accept': 'Aceptar documento',
  'patient.documents.loading': 'Cargando documentación...',
  'patient.documents.loadErrorTitle': 'No hemos podido cargar la documentación',
  'patient.documents.retryLabel': 'Reintentar documentación',

  // ── Portal del paciente: recuperación del enlace de acceso ───────────────
  'patient.accessLink.expiredTitle': '¿Ha caducado tu enlace?',
  'patient.accessLink.expiredText': 'Te enviaremos uno nuevo a tu email.',
  'patient.accessLink.emailPlaceholder': 'tu@email.com',
  'patient.accessLink.send': 'Enviar enlace',
  'patient.accessLink.sending': 'Enviando...',

  // ── Portal del paciente (nueva interfaz, paridad con la app Android) ───────
  'papp.nav.label': 'Navegación principal',
  'papp.nav.home': 'Inicio',
  'papp.nav.diet': 'Mi dieta',
  'papp.nav.training': 'Entrenamiento',
  'papp.nav.progress': 'Progreso',
  'papp.nav.appointments': 'Citas',
  'papp.home.greeting': 'Hola, {name}',
  'papp.home.greetingAnonymous': 'Hola, bienvenido/a',
  'papp.home.trainingToday': 'Entrenamiento de hoy',
  'papp.home.trainingNext': 'Próximo entrenamiento',
  'papp.home.start': 'Empezar →',
  'papp.home.view': 'Ver →',
  'papp.home.nextAppointment': 'Próxima cita',
  'papp.home.consultation': 'Consulta',
  'papp.home.quickAccess': 'Accesos rápidos',
  'papp.home.appointments': 'Mis citas',
  'papp.home.appointmentsHint': 'Próxima cita y seguimiento',
  'papp.home.documents': 'Documentos',
  'papp.home.documentsHint': 'Archivos y consentimientos',
  'papp.home.shopping': 'Lista de compra',
  'papp.home.shoppingHint': 'Ingredientes del plan',
  'papp.home.messages': 'Mensajes',
  'papp.home.messagesHint': 'Habla con tu profesional',
  'papp.home.profile': 'Perfil',
  'papp.home.profileHint': 'Datos y configuración',
  'papp.minutes': '{count} min',
  'papp.loading': 'Cargando…',
  'papp.error': 'No se ha podido cargar tu información.',
  'papp.retry': 'Reintentar',
  'papp.pending.title': 'Esta sección todavía se abre en el portal actual',
  'papp.pending.text': 'Estamos trasladando el portal a la nueva interfaz, igual que la app Android. Mientras tanto puedes usar esta sección en el portal actual.',
  'papp.pending.open': 'Abrir en el portal actual',
  'papp.logout': 'Cerrar sesión'
} as const;

export type TranslationKey = keyof typeof es;
export type Dictionary = Record<TranslationKey, string>;
