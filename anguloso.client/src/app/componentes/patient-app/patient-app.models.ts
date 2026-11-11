// Contratos de la API del portal del paciente (`/api/portal/*`, `/api/appointments/*`). Son los mismos que consume la app Android
// (ver DietoExpress.Android/docs/API_COMPATIBILITY.md): cualquier cambio en ellos debe mantenerse compatible con ambos clientes.

export interface WeightEntry {
  date: string;
  weight: number;
}

export interface PatientProfile {
  clientId: number;
  fullName: string;
  age?: number | null;
  gender?: string | null;
  currentWeight?: number | null;
  currentHeight?: number | null;
  weightHistory?: WeightEntry[];
  hasActiveDiet?: boolean;
  clinicName?: string | null;
  clinicPhone?: string | null;
  nutritionistName?: string | null;
}

export interface PatientAppointment {
  id: number;
  startsAt: string;
  endsAt: string;
  status: string;
  nutritionistName?: string | null;
  modality?: string | null;
  videoRoomUrl?: string | null;
}

export interface TrainingSession {
  id: number;
  planId: number;
  scheduledDate: string;
  title: string;
  sessionType: string;
  durationMinutes?: number | null;
  status?: string | null;
  outcome?: string | null;
  completedAt?: string | null;
}

export interface TrainingEnvelope {
  enabled: boolean;
  sessions?: TrainingSession[];
}
