// ────────────────────────────────────────────────────────────────────────────
// Client models — coinciden con los DTOs del ClientsController del backend
// ────────────────────────────────────────────────────────────────────────────

/** GET /api/clients  → array de este tipo (equivale a ClientListDto) */
export interface ClientListItem {
  id: number;
  fullName: string;
  email?: string;
  phone?: string;
  gender?: string;
  birthDate?: string; // yyyy-MM-dd
  createdAt?: string; // ISO date
}

/** GET /api/clients/{id}  → incluye biometría (equivale a ClientDetailDto) */
export interface ClientDetail extends ClientListItem {
  notes?: string;
  biometrics: Biometric[];
}

/** POST /api/clients  (equivale a CreateClientDto) */
export interface CreateClientRequest {
  fullName: string;
  email?: string;
  phone?: string;
  gender?: string;
  birthDate?: string; // yyyy-MM-dd
  notes?: string;
}

/** PUT /api/clients/{id}  (equivale a UpdateClientDto) */
export interface UpdateClientRequest {
  fullName: string;
  email?: string;
  phone?: string;
  gender?: string;
  birthDate?: string; // yyyy-MM-dd
  notes?: string;
}

// ─── Alias de compatibilidad (usado en componentes anteriores) ───────────────
/** @deprecated Usa ClientDetail en su lugar */
export type Client = ClientDetail;

// ────────────────────────────────────────────────────────────────────────────
// Biometric models — coinciden con BiometricsDto.cs
// ────────────────────────────────────────────────────────────────────────────

// ────────────────────────────────────────────────────────────────────────────
// Anthropometry Analysis — coincide con AnthropometryAnalysisDto.cs
// ────────────────────────────────────────────────────────────────────────────

export interface HeathCarterSomatotype {
  endomorphy: number;
  mesomorphy: number;
  ectomorphy: number;
  x: number;
  y: number;
  coordinatesAvailable: boolean;
}

export interface AnthropometryAnalysis {
  // % grasa por protocolo
  bodyFatPercentageJacksonPollock3?: number;
  bodyFatPercentageJacksonPollock4?: number;
  bodyFatPercentageJacksonPollock7?: number;
  bodyFatPercentageFaulkner?: number;
  bodyFatPercentageDurninWomersley?: number;
  bodyFatPercentageCarter?: number;
  // 4 componentes (kg y %)
  fatMassKg?: number;
  fatMassPercentage?: number;
  muscleMassKg?: number;
  muscleMassPercentage?: number;
  boneMassKg?: number;
  boneMassPercentage?: number;
  residualMassKg?: number;
  residualMassPercentage?: number;
  // Somatotipo
  somatotype?: HeathCarterSomatotype;
}

/** GET /api/clients/{clientId}/biometrics y GET /api/clients/{clientId}/biometrics/{id} */
export interface Biometric {
  id?: number;
  measurementDate: string; // yyyy-MM-dd
  weight?: number;
  height?: number;
  bodyFat?: number;
  muscleMass?: number;
  visceralFat?: number;
  waist?: number;
  hip?: number;
  neck?: number;
  // Pliegues cutáneos básicos (mm)
  triceps?: number;
  abdomen?: number;
  thigh?: number;
  subscapular?: number;
  suprailiac?: number;
  // Pliegues cutáneos avanzados (mm)
  biceps?: number;
  chest?: number;
  axilla?: number;
  calfSkinfold?: number;
  // Perímetros (cm)
  armPerimeter?: number;
  calfPerimeter?: number;
  // Diámetros óseos (cm)
  wristDiameter?: number;
  femurDiameter?: number;
  humerusDiameter?: number;
  bmi?: number;
  notes?: string;
  // Análisis calculado por el backend
  analysis?: AnthropometryAnalysis;
}

/** POST /api/clients/{clientId}/biometrics  (equivale a CreateBiometricDto) */
export interface CreateBiometricRequest {
  measurementDate: string; // yyyy-MM-dd, required
  weight?: number;
  height?: number;
  bodyFat?: number;
  muscleMass?: number;
  visceralFat?: number;
  waist?: number;
  hip?: number;
  neck?: number;
  triceps?: number;
  abdomen?: number;
  thigh?: number;
  subscapular?: number;
  suprailiac?: number;
  biceps?: number;
  chest?: number;
  axilla?: number;
  calfSkinfold?: number;
  armPerimeter?: number;
  calfPerimeter?: number;
  wristDiameter?: number;
  femurDiameter?: number;
  humerusDiameter?: number;
  notes?: string;
}

/** PUT /api/clients/{clientId}/biometrics/{id}  (equivale a UpdateBiometricDto) */
export type UpdateBiometricRequest = CreateBiometricRequest;

// ────────────────────────────────────────────────────────────────────────────
// ClientDiet models — coinciden con ClientDietDto.cs
// ────────────────────────────────────────────────────────────────────────────

export interface ClientDiet {
  id?: number;
  clientId: number;
  dietId: number;
  dietName: string;
  assignedAt?: string;
  startDate: string; // yyyy-MM-dd
  endDate?: string;  // yyyy-MM-dd
  isActive: boolean;
  notes?: string;
  // Optional: populated on the frontend when active diet details are loaded
  diet?: any;
}

export interface AssignDietPayload {
  dietId: number;
  startDate: string; // yyyy-MM-dd
  notes?: string;
}

export interface UpdateClientDietPayload {
  startDate: string; // yyyy-MM-dd
  endDate?: string;  // yyyy-MM-dd
  isActive: boolean;
  notes?: string;
}

