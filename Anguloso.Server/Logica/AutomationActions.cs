using Npgsql;

namespace Anguloso.Server.Logica;

// Estas acciones son contratos serializables entre el motor de automatizaciones, el worker y los endpoints de gestión.
public sealed record NotifyPatientAction(
    int ClientId,
    string Type,
    string Title,
    string Message,
    string? ActionUrl,
    bool SendPush = true);

public sealed record EmailPatientAction(
    int ClientId,
    string Subject,
    string HtmlBody);

public sealed record CancelAutomationRequest(string? Reason);

public sealed class AutomationExecutionDto
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public string Result { get; set; } = "";
    public string? Error { get; set; }
    public long DurationMs { get; set; }
    public DateTime ExecutedAt { get; set; }
}
