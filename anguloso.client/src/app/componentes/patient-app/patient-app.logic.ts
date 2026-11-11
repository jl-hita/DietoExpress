import type { PatientAppointment, TrainingEnvelope, TrainingSession } from './patient-app.models';

// Reglas de presentación del portal del paciente. Son funciones puras y replican las de la app Android (MainActivity.HomeContent)
// para que ambos clientes muestren lo mismo como «próxima cita» y «próximo entrenamiento».

const INACTIVE_TRAINING = new Set(['completed', 'cancelled', 'not_completed']);

/** Primer nombre para el saludo; cadena vacía si no hay nombre. */
export function firstName(fullName: string | null | undefined): string {
  return (fullName ?? '').trim().split(/\s+/)[0] ?? '';
}

/** Próxima cita futura, solicitada o confirmada, la más cercana. */
export function pickNextAppointment(appointments: readonly PatientAppointment[] | null | undefined, now: Date = new Date()): PatientAppointment | null {
  const upcoming = (appointments ?? []).filter(appointment => {
    const status = appointment.status?.toLowerCase();
    const startsAt = Date.parse(appointment.startsAt);
    return (status === 'confirmed' || status === 'requested') && Number.isFinite(startsAt) && startsAt > now.getTime();
  });
  return upcoming.sort((a, b) => Date.parse(a.startsAt) - Date.parse(b.startsAt))[0] ?? null;
}

/** Fecha local en formato yyyy-MM-dd (la que usa el backend para `scheduledDate`). */
export function localIsoDate(date: Date = new Date()): string {
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/** Próximo entrenamiento pendiente (hoy o posterior) si el módulo está activado para el paciente. */
export function pickNextTraining(training: TrainingEnvelope | null | undefined, today: string = localIsoDate()): TrainingSession | null {
  if (!training?.enabled) return null;
  const pending = (training.sessions ?? []).filter(session => {
    const status = session.status?.toLowerCase();
    const outcome = session.outcome?.toLowerCase();
    const isActive = !(status && INACTIVE_TRAINING.has(status)) && !(outcome && INACTIVE_TRAINING.has(outcome));
    return isActive && session.scheduledDate.slice(0, 10) >= today;
  });
  return pending.sort((a, b) => a.scheduledDate.localeCompare(b.scheduledDate))[0] ?? null;
}

export function isToday(isoDate: string, today: string = localIsoDate()): boolean {
  return isoDate.slice(0, 10) === today;
}

/** «lunes, 10 de noviembre» en el idioma de la interfaz. Si el valor no es una fecha válida lo devuelve tal cual. */
export function formatDateLabel(value: string, locale: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})/.exec(value);
  const date = match ? new Date(Number(match[1]), Number(match[2]) - 1, Number(match[3])) : new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long' }).format(date);
}

/** Hora local HH:mm de un instante ISO; cadena vacía si no es válido. */
export function formatTimeLabel(value: string, locale: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', hour12: false }).format(date);
}
