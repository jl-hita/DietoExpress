using System.Text.Json;

namespace Anguloso.Server.Logica;

// Estos modelos representan el contrato persistente del motor: evento, trabajo programado y tarea profesional.
public sealed record AutomationEvent(
    long Id,
    int TenantId,
    string EventType,
    string AggregateType,
    string? AggregateId,
    string Payload,
    DateTime OccurredAt);

// Attempts se incrementa al reclamar el job; MaxAttempts limita los reintentos incluso si el worker se reinicia
// durante una llamada externa y recupera posteriormente un job que quedó en processing.
public sealed record AutomationJob(
    long Id,
    int TenantId,
    long? EventId,
    string ActionType,
    string Payload,
    DateTime ScheduledAt,
    int Attempts,
    int MaxAttempts);

public sealed class ProfessionalTaskCreateRequest
{
    public int? ClientId { get; set; }
    public int? AssignedUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DueAt { get; set; }
    public string Priority { get; set; } = "normal";
}

public sealed class ProfessionalTaskDto
{
    public long Id { get; set; }
    public int TenantId { get; set; }
    public int? ClientId { get; set; }
    public int? AssignedUserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DueAt { get; set; }
    public string Priority { get; set; } = "normal";
    public string Status { get; set; } = "open";
    public string Source { get; set; } = "manual";
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

// Estos payloads se serializan en automation_jobs, por lo que sus nombres y significado forman parte del contrato
// entre el productor y el worker y deben evolucionar de forma compatible con jobs ya persistidos.
public sealed record CreateTaskAction(
    int? ClientId,
    int? AssignedUserId,
    string Title,
    string? Description,
    DateTime? DueAt,
    string Priority,
    string Source);

public static class AutomationJson
{
    public static string Serialize(object value) => JsonSerializer.Serialize(value);
    public static T? Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value);
}
