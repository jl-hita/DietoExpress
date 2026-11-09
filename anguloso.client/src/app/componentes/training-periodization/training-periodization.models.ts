// Modelos de datos de la planificación deportiva (contratos con /api/training). Extraídos del componente principal.

export interface TrainingProfile {
  id: number; clientId: number; enabled: boolean; primaryGoal: string; experienceLevel: string; managementMode: 'none' | 'self' | 'collaboration'; notes?: string | null; canManage?: boolean;
}
export interface TrainingPlan {
  id: number; name: string; periodizationModel: string; startDate: string; endDate?: string | null; status: string; version: number; notes?: string | null;
}
export interface GeneratedExercise {
  exerciseId: string; catalogVersion: string; exerciseNameSnapshot: string; order: number;
  sets: number; minRepetitions: number; maxRepetitions: number; targetRepetitionsInReserve?: number | null;
  restSeconds: number; progressionRule: string | number; selectionRationale: string;
  primaryMuscleGroups?: Array<string | number>; secondaryMuscleGroups?: Array<string | number>;
  smallestLoadIncrementKg?: number | null; maximumLoadIncreasePercent?: number | null;
  warmupSets?: number | null; warmupNote?: string | null; targetLoadKg?: number | null; loadBasis?: string | null; adaptationOriginalSets?: number | null;
}
export interface GeneratedSession {
  weekNumber: number; day: number; title: string; estimatedDurationMinutes: number; exercises: GeneratedExercise[];
}
export interface TrainingExerciseCatalogItem {
  id: string; name: string; muscles: string; goal: string; equipment: string; equipmentLabel: string;
  instructions: string; prescription: string; imageUrl?: string; source?: string;
}
export interface GeneratedProgram {
  engineVersion: string; goal: string; startDate: string; durationWeeks: number; sessions: GeneratedSession[];
  history?: { previousBlockExerciseCount: number; continuesPreviousBlock: boolean; exercises: Array<{ exerciseId: string; name: string; status: 'kept' | 'new' | 'dropped' }> } | null;
  qualityReport?: {
    methodology: string;
    weeklyMuscleVolumes: Array<{ weekNumber: number; muscles: Array<{ muscleGroup: string; primarySets: number; secondarySets: number; primaryFrequencySessions: number; secondaryFrequencySessions: number; estimatedEffectiveSets: number }> }>;
    warnings: string[];
    sessionVolumes?: Array<{ weekNumber: number; day: number; title: string; muscles: Array<{ muscleGroup: string; estimatedEffectiveSets: number }> }>;
  } | null;
}
export interface GenerationRequest {
  goal: number; experience: number; startDate: string; durationWeeks: number; trainingDays: number[]; split: number;
  maximumSessionDurationMinutes: number; availableEquipment: string[]; excludedExerciseIds: string[];
  muscleVolumeTargets?: Array<{ muscleGroup: string; priority: number; targetWeeklyPrimarySets?: number | null }>;
  isDeload?: boolean; rotationIndex?: number;
}
export interface ManualVolumePreview {
  qualityReport: NonNullable<GeneratedProgram['qualityReport']>;
  unclassifiedExerciseNames: string[];
  sessionReports: Array<{
    weekNumber: number; day: number; title: string;
    qualityReport: NonNullable<GeneratedProgram['qualityReport']>;
    unclassifiedExerciseNames: string[];
  }>;
}
export interface MuscleVolumePreferenceDraft {
  muscleGroup: string; label: string; enabled: boolean; priority: number; targetWeeklyPrimarySets: number | null;
}
export interface TrainingExerciseVolumeRecommendation {
  exerciseId: string; action: string; explanation: string; suggestedWorkingSets?: number | null; exposuresAnalyzed: number;
}
export interface TrainingAdaptationProposal {
  id: number; clientId: number; planId: number; sourceSessionId: number; sourceRecordId: number;
  action: string; explanation: string; status: string; createdAt: string; reviewNotes?: string | null;
  appliedFutureSessions: number; sessionDate: string; sessionTitle: string;
  exerciseVolumeRecommendations?: TrainingExerciseVolumeRecommendation[];
}
export interface TrainingSession {
  id: number; planId: number; scheduledDate: string; title: string; sessionType: string; durationMinutes?: number | null;
  intensityTarget?: string | null; instructions?: string | null; status: string; completedAt?: string | null;
  actualDurationMinutes?: number | null; sessionRpe?: number | null; recordNotes?: string | null; exercisePrescription?: string | null; exercisePrescriptionData?: Record<string, unknown> | null;
}
export interface StructuredExerciseDraft {
  exerciseName: string; originalExerciseName?: string; sets: number; repetitions: string; rir: number | null; restSeconds: number; notes: string;
  exerciseId?: string; catalogVersion?: string; progressionRule?: string | number;
  primaryMuscleGroups?: Array<string | number>; secondaryMuscleGroups?: Array<string | number>;
  smallestLoadIncrementKg?: number | null; maximumLoadIncreasePercent?: number | null; selectionRationale?: string;
  warmupSets?: number | null; warmupNote?: string | null; targetLoadKg?: number | null; loadBasis?: string | null; adaptationOriginalSets?: number | null;
}
export interface WeeklySessionDraft {
  date: string; title: string; type: string; duration: number | null; intensity: string; instructions: string;
  exercises: StructuredExerciseDraft[];
}
export interface SplitReorganizationMove { sessionId: number; title: string; oldDate: string; scheduledDate: string; }
export interface SplitReorganizationDraft { splitType: number; trainingDays: number[]; moves: SplitReorganizationMove[] | null; error: string; }
export interface SplitReplacementSession {
  scheduledDate: string; title: string; sessionType: string; durationMinutes: number;
  intensityTarget: string | null; instructions: string | null; exercisePrescription: string;
  exercisePrescriptionData: Record<string, unknown> | null;
}
export interface SplitReplacementPreview {
  replaceFromDate: string; replaceThroughDate: string; sessions: SplitReplacementSession[];
  volumePreview: ManualVolumePreview;
  preservedManualEdits: number; retainedCustomExercises: number; warnings: string[];
}
export interface TrainingLoadWeek { weekStart: string; completedSessions: number; plannedMinutes: number; actualMinutes: number; loadScore: number; }
export interface TrainingMesocycle { id: number; planId: number; name: string; sequenceNumber: number; startDate: string; endDate?: string | null; objective?: string | null; notes?: string | null; reviewStatus?: 'draft' | 'reviewed'; }
export interface MacrocyclePhase { id: number; name: string; sequenceNumber: number; startDate: string; endDate: string; objective?: string | null; phaseType: 'accumulation' | 'intensification' | 'deload' | 'realization' | 'general'; }
export interface MacroPhaseLoadWeek { weekNumber: number; startDate: string; endDate: string; sessions: number; completedSessions: number; plannedMinutes: number; sets: number | null; intensityLabel: string; intensityPct: number; volumePct: number; frequencyPct: number; isDeload: boolean; }
export interface MacrocyclePlan { id: number; planId: number; name: string; periodizationModel: string; startDate: string; endDate?: string | null; notes?: string | null; phases: MacrocyclePhase[]; }
export interface TrainingMicrocycle { id: number; mesocycleId: number; name: string; sequenceNumber: number; startDate: string; endDate?: string | null; focus?: string | null; notes?: string | null; }
export interface TrainingCollaboration {
  id: number; professionalUserId?: number; professionalName?: string; email?: string; accessScope: string;
  status: string; createdAt: string; acceptedAt?: string | null; revokedAt?: string | null;
}
export interface TrainingInvitation {
  id: number; clientId: number; clientName: string; accessScope: string; createdAt: string; invitedBy: string;
}
export interface TrainingTemplate {
  id: number; name: string; periodizationModel: string; notes?: string | null; createdAt: string; sessionCount: number;
}
export interface TrainingSessionTemplate {
  id: number; name: string; sessionType: string; durationMinutes?: number | null; intensityTarget?: string | null;
  instructions?: string | null; exercisePrescription?: string | null; exercisePrescriptionData?: Record<string, unknown> | null; createdAt?: string;
}
export interface RepDbExercise {
  id: string;
  nameEs?: string;
  nameEn: string;
  equipment?: string;
  bodyPart?: string;
  category?: string;
  goals?: string[];
  primaryMuscles?: string[];
  secondaryMuscles?: string[];
  instructionsEs?: string[];
  descriptionEs?: string;
  images?: { flat?: { start?: string; main?: string; peak?: string } };
}
