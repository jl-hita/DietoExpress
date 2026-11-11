using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Anguloso.Server.Logica;
using Anguloso.Server.Logica.Utils;
using Anguloso.Server.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Anguloso.Server.Controllers;

public sealed record ReplaceFutureTrainingSplitRequest(
    DateOnly ReplaceFromDate,
    DateOnly ReplaceThroughDate,
    int SplitType,
    int[] TrainingDays,
    IReadOnlyList<ReplaceFutureTrainingSessionRequest> Sessions);
public sealed record ReplaceFutureTrainingSessionRequest(
    DateOnly ScheduledDate, string Title, string SessionType, int? DurationMinutes,
    string? IntensityTarget, string? Instructions, string? ExercisePrescription,
    JsonElement? ExercisePrescriptionData = null);

public sealed record ReorganizeTrainingSplitRequest(
    int SplitType,
    int[] TrainingDays,
    IReadOnlyList<TrainingSessionMoveRequest> Moves);
public sealed record TrainingSessionMoveRequest(int SessionId, DateOnly ScheduledDate);

public sealed record ReviewTrainingAdaptationProposalRequest(
    string Decision,
    string? Notes = null,
    IReadOnlyList<string>? TargetExerciseIds = null);
public sealed record TrainingProfileDto(int Id, int ClientId, bool Enabled, string PrimaryGoal, string ExperienceLevel, string ManagementMode, string? Notes, DateTime CreatedAt, DateTime UpdatedAt, bool CanManage = true);
public sealed record UpdateTrainingProfileRequest(bool Enabled, string PrimaryGoal, string ExperienceLevel, string? Notes, string? ManagementMode = null);
public sealed record CreateTrainingPlanRequest(string Name, string PeriodizationModel, DateOnly StartDate, DateOnly? EndDate, string? Notes);
public sealed record UpdateTrainingPlanRequest(string Name, string PeriodizationModel, DateOnly StartDate, DateOnly? EndDate, string? Notes, int Version);
public sealed record CreateQuickTrainingRoutineRequest(string Name, DateOnly ScheduledDate, string SessionType, int DurationMinutes, string? IntensityTarget, string? Instructions, string? ExercisePrescription);
public sealed record TrainingPlanDto(int Id, string Name, string PeriodizationModel, DateOnly StartDate, DateOnly? EndDate, string Status, int Version, string? Notes);
public sealed record CreateTrainingSessionRequest(DateOnly ScheduledDate, string Title, string SessionType, int? DurationMinutes, string? IntensityTarget, string? Instructions, string? ExercisePrescription = null, JsonElement? ExercisePrescriptionData = null);
public sealed record RecordTrainingSessionRequest(DateTime? CompletedAt, int? ActualDurationMinutes, decimal? SessionRpe, string? Notes);
public sealed record TrainingSessionDto(int Id, int PlanId, DateOnly ScheduledDate, string Title, string SessionType, int? DurationMinutes, string? IntensityTarget, string? Instructions, string Status, DateTime? CompletedAt, int? ActualDurationMinutes, decimal? SessionRpe, string? RecordNotes, string? ExercisePrescription = null, JsonElement? ExercisePrescriptionData = null);
public sealed record InviteTrainingCollaboratorRequest(string Email, string? AccessScope = "training");
public sealed record TrainingCollaborationDto(int Id, int ProfessionalUserId, string ProfessionalName, string Email, string AccessScope, string Status, DateTime CreatedAt, DateTime? AcceptedAt, DateTime? RevokedAt);

public sealed record MacrocyclePhaseDto(long Id, string Name, int SequenceNumber, DateOnly StartDate, DateOnly EndDate, string? Objective, string PhaseType);
public sealed record MacrocycleDto(long Id, int PlanId, string Name, string PeriodizationModel, DateOnly StartDate, DateOnly? EndDate, string? Notes, IReadOnlyList<MacrocyclePhaseDto> Phases);
public sealed record SaveTrainingMacrocycleRequest(string Name, string PeriodizationModel, DateOnly StartDate, DateOnly? EndDate, string? Notes, List<SaveTrainingMacrocyclePhaseRequest> Phases);
public sealed record SaveTrainingMacrocyclePhaseRequest(string Name, int SequenceNumber, DateOnly StartDate, DateOnly EndDate, string? Objective, string PhaseType);

public sealed record CreateTrainingMesocycleRequest(string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate, string? Objective, string? Notes);
public sealed record GenerateTrainingMesocycleRequest(string Name, int SequenceNumber, TrainingProgramGenerationRequest Generation, string? Notes = null, int? ExistingMesocycleId = null);
public sealed record TrainingMesocycleDto(int Id, int PlanId, string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate, string? Objective, string? Notes, string ReviewStatus = "reviewed");
public sealed record SetMesocycleReviewStatusRequest(string Status);
public sealed record CreateTrainingMicrocycleRequest(string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate, string? Focus, string? Notes);
public sealed record ReorderTrainingCyclesRequest(int[] OrderedIds);
public sealed record SaveTrainingTemplateRequest(string Name);
public sealed record SaveTrainingSessionTemplateRequest(string Name, string SessionType, int? DurationMinutes, string? IntensityTarget, string? Instructions, string? ExercisePrescription, JsonElement? ExercisePrescriptionData = null);
public sealed record ApplyTrainingTemplateRequest(string Name, DateOnly StartDate);
public sealed record TrainingTemplateSession(DateOnly ScheduledDate, string Title, string SessionType, int? DurationMinutes,
    string? IntensityTarget, string? Instructions, string? ExercisePrescription, JsonElement? ExercisePrescriptionData = null);
public sealed record TrainingTemplatePayload(string Name, string PeriodizationModel, DateOnly StartDate, DateOnly? EndDate,
    string? Notes, List<TrainingTemplateSession> Sessions, List<TrainingTemplateMesocycle> Mesocycles);
public sealed record TrainingTemplateMesocycle(int Id, string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate,
    string? Objective, string? Notes, List<TrainingTemplateMicrocycle> Microcycles);
public sealed record TrainingTemplateMicrocycle(string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate,
    string? Focus, string? Notes);

public sealed record TrainingMicrocycleDto(int Id, int MesocycleId, string Name, int SequenceNumber, DateOnly StartDate, DateOnly? EndDate, string? Focus, string? Notes);

public sealed record ResolveTrainingReviewRequest(string Decision, string? Notes);
