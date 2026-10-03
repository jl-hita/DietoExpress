using Npgsql;

namespace Anguloso.Server.Logica;

// Estas acciones son contratos serializables entre el motor de automatizaciones, el worker y los endpoints de gestión.
// Las acciones se almacenan como JSON y se ejecutan de forma asíncrona; los campos opcionales permiten ampliar
// el contrato sin obligar a reescribir jobs ya persistidos.
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

// DTO de observabilidad: expone cada intento registrado sin mezclarlo con el estado actual del job.
public sealed class AutomationExecutionDto
{
    public long Id { get; set; }
    public long JobId { get; set; }
    public string Result { get; set; } = "";
    public string? Error { get; set; }
    public long DurationMs { get; set; }
    public DateTime ExecutedAt { get; set; }
}
