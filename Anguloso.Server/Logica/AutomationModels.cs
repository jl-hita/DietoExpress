using System.Text.Json;

namespace Anguloso.Server.Logica;

public sealed record AutomationEvent(
    long Id,
    int TenantId,
    string EventType,
    string AggregateType,
    string? AggregateId,
    string Payload,
    DateTime OccurredAt);

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
