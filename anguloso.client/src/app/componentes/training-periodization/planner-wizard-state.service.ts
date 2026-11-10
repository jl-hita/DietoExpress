import { Injectable } from '@angular/core';

export interface PlannerWizardDraft {
  schemaVersion: 1;
  currentStep: number;
  goal: number;
  experience: number;
  trainingDays: number[];
  sessionMinutes: number;
  split: number;
  planName: string;
  startDate: string;
  durationWeeks: number;
  includeDeload: boolean;
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
      planName: '',
      startDate: localDate,
      durationWeeks: 12,
      includeDeload: true
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
          : fallback.trainingDays
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

  private storageKey(clientId: number): string {
    return `dietoexpress:training-planner:v1:client:${clientId}`;
  }
}
