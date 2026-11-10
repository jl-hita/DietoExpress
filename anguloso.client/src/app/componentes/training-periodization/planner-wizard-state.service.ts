import { Injectable } from '@angular/core';

export interface PlannerWizardDraft {
  schemaVersion: 1;
  currentStep: number;
  goal: number;
  experience: number;
  trainingDays: number[];
  sessionMinutes: number;
  split: number;
  availableEquipmentCsv: string;
  planName: string;
  startDate: string;
  durationWeeks: number;
  includeDeload: boolean;
  deloadEveryWeeks: number;
  phases: PlannerWizardPhaseDraft[];
  macrocycleName: string;
  serverPlanId: number | null;
  serverMacrocycleSaved: boolean;
  serverSplitSaved: boolean;
  creationInProgress: boolean;
  creationUncertain: boolean;
  generatedPhaseKeys: string[];
}

export interface PlannerWizardPhaseDraft {
  name: string; sequenceNumber: number; startDate: string; endDate: string; objective: string;
  phaseType: 'accumulation' | 'intensification' | 'deload' | 'realization' | 'general';
}

@Injectable({ providedIn: 'root' })
export class PlannerWizardStateService {
  private readonly schemaVersion = 1;

  createDefault(): PlannerWizardDraft {
    const now = new Date();
    const localDate = new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
    return {
      schemaVersion: this.schemaVersion,
      currentStep: 0,
      goal: 1,
      experience: 0,
      trainingDays: [1, 3, 5],
      sessionMinutes: 60,
      split: 0,
      availableEquipmentCsv: '',
      planName: '',
      startDate: localDate,
      durationWeeks: 12,
      includeDeload: true,
      deloadEveryWeeks: 4, phases: [], macrocycleName: 'Macrociclo principal',
      serverPlanId: null, serverMacrocycleSaved: false, serverSplitSaved: false,
      creationInProgress: false, creationUncertain: false, generatedPhaseKeys: []
    };
  }

  load(clientId: number): PlannerWizardDraft {
    const fallback = this.createDefault();
    if (typeof sessionStorage === 'undefined') return fallback;
    try {
      const raw = sessionStorage.getItem(this.storageKey(clientId));
      if (!raw) return fallback;
      const parsed = JSON.parse(raw) as Partial<PlannerWizardDraft>;
      if (parsed.schemaVersion !== this.schemaVersion) return fallback;
      return {
        ...fallback,
        ...parsed,
        currentStep: Math.min(4, Math.max(0, Number(parsed.currentStep) || 0)),
        trainingDays: Array.isArray(parsed.trainingDays)
          ? [...new Set(parsed.trainingDays.filter(day => Number.isInteger(day) && day >= 0 && day <= 6))]
          : fallback.trainingDays,
        phases: Array.isArray(parsed.phases) ? parsed.phases.filter(phase => phase && typeof phase.name === 'string' && typeof phase.startDate === 'string' && typeof phase.endDate === 'string') : [],
        serverPlanId: Number.isInteger(parsed.serverPlanId) && Number(parsed.serverPlanId) > 0 ? Number(parsed.serverPlanId) : null,
        generatedPhaseKeys: Array.isArray(parsed.generatedPhaseKeys) ? parsed.generatedPhaseKeys.filter(key => typeof key === 'string') : []
      };
    } catch {
      return fallback;
    }
  }

  save(clientId: number, draft: PlannerWizardDraft): void {
    if (typeof sessionStorage === 'undefined') return;
    try {
      sessionStorage.setItem(this.storageKey(clientId), JSON.stringify({
        ...draft,
        schemaVersion: this.schemaVersion
      }));
    } catch {
      // La imposibilidad de guardar almacenamiento de sesión no debe bloquear el asistente.
    }
  }

  clear(clientId: number): void {
    if (typeof sessionStorage === 'undefined') return;
    try { sessionStorage.removeItem(this.storageKey(clientId)); } catch { /* El borrador de sesión es opcional. */ }
  }

  reset(clientId: number): PlannerWizardDraft {
    this.clear(clientId);
    const draft = this.createDefault();
    this.save(clientId, draft);
    return draft;
  }

  clearIfPlanMatches(clientId: number, planId: number): void {
    if (typeof sessionStorage === 'undefined' || !Number.isInteger(planId) || planId <= 0) return;
    try {
      const raw = sessionStorage.getItem(this.storageKey(clientId));
      if (!raw) return;
      const draft = JSON.parse(raw) as Partial<PlannerWizardDraft>;
      if (draft.serverPlanId === planId) sessionStorage.removeItem(this.storageKey(clientId));
    } catch {
      // A malformed optional session draft must not interfere with deleting a plan.
    }
  }

  private storageKey(clientId: number): string {
    return `dietoexpress:training-planner:v1:client:${clientId}`;
  }
}
