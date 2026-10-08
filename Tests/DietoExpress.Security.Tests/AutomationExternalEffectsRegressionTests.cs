using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>
/// Protege los efectos externos del worker frente a la ventana de caída que existe después
/// de completar un envío externo y antes de marcar el automation job como completed.
/// </summary>
public sealed class AutomationExternalEffectsRegressionTests
{
    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Anguloso.Server", "Program.cs")))
                directory = directory.Parent;

            return directory?.FullName
                ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
        }
    }

    private static string ReadServerSource(string relativePath)
        => File.ReadAllText(Path.Combine(RepoRoot, relativePath));

    [Fact]
    public void SmtpEmailsMustUseADeterministicMessageId()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/EmailServ.cs");

        // SMTP no ofrece exactly-once de forma universal, pero un Message-ID estable permite
        // que proveedores que deduplican mensajes reconozcan un reintento del mismo efecto.
        Assert.Contains("SHA256.HashData", source);
        Assert.Contains("mail.Headers.Add(\"Message-ID\", messageId)", source);
        Assert.Contains("idempotencyKey ?? Guid.NewGuid()", source);
        Assert.Contains("Message-ID determinista", source);
    }

    [Fact]
    public void AutomationWorkerMustKeepDatabaseActionsIdempotentBeforeCompletion()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/AutomationWorker.cs");

        // Si el proceso cae después de la acción y antes de CompleteJobAsync, las acciones
        // persistentes deben reconocer el mismo job en lugar de crear un segundo efecto.
        Assert.Contains("$\"job:{job.Id}\"", source);
        Assert.Contains("$\"job:{job.Id}:document-provision-failed\"", source);
        Assert.Contains("$\"job:{job.Id}:email\"", source);
        Assert.Contains("$\"job:{job.Id}:patient-notification\"", source);
        Assert.Contains("CreateProfessionalTaskAsync", source);
        Assert.Contains("CreateRequiredDocumentsAsync", source);
    }


    [Fact]
    public void StaleAutomationRecoveryMustNotExceedMaxAttempts()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/AutomationWorker.cs");

        Assert.Contains("CASE WHEN attempts >= max_attempts THEN 'failed' ELSE 'pending' END", source);
        Assert.Contains("attempts >= max_attempts", source);
        Assert.Contains("El worker anterior quedó huérfano tras agotar los reintentos permitidos.", source);
    }

    [Fact]
    public void PatientNotificationsMustPersistTheAutomationIdempotencyKey()
    {
        var service = ReadServerSource("Anguloso.Server/Logica/NotificationService.cs");
        var schema = ReadServerSource("Anguloso.Server/Program.cs");

        Assert.Contains("idempotency_key", service);
        Assert.Contains("ON CONFLICT (tenant_id, idempotency_key) DO NOTHING", service);
        Assert.Contains("ADD COLUMN IF NOT EXISTS idempotency_key", schema);
        Assert.Contains("uq_patient_notifications_tenant_idempotency", schema);
    }

    [Fact]
    public void PatientDocumentsMustGuardConcurrentAndRetriedProvisioning()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/PatientDocumentService.cs");

        Assert.Contains("pg_advisory_xact_lock", source);
        Assert.Contains("WHERE NOT EXISTS", source);
        Assert.Contains("document_template_id=@template AND version=@version", source);
        Assert.Contains("File.Delete(destination)", source);
    }

    [Fact]
    public void NotificationDeliveryMustPersistInAppBeforePush()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/NotificationService.cs");

        var persistence = source.IndexOf("RETURNING id;", StringComparison.Ordinal);
        var push = source.IndexOf("SendPushAsync(tenantId, clientId", persistence + 1, StringComparison.Ordinal);

        Assert.True(persistence >= 0, "La notificación in-app debe persistirse antes del canal push.");
        Assert.True(push > persistence, "El push debe ejecutarse después de persistir la notificación durable.");
    }
    [Fact]
    public void PushDeliveryMustUseDurableLedgerWithConcurrentClaimProtection()
    {
        var service = ReadServerSource("Anguloso.Server/Logica/NotificationService.cs");
        var schema = ReadServerSource("Anguloso.Server/Program.cs");

        Assert.Contains("TryClaimPushDeliveryAsync", service);
        Assert.Contains("patient_push_deliveries", service);
        Assert.Contains("WHERE patient_push_deliveries.status = 'failed'", service);
        Assert.Contains("OR (patient_push_deliveries.status = 'processing'", service);
        Assert.Contains("updated_at < NOW() - INTERVAL '10 minutes'", service);
        Assert.Contains("MarkPushDeliveryAsync", service);
        Assert.Contains("uq_patient_push_deliveries_key", schema);
        Assert.Contains("patient_push_deliveries", schema);
        Assert.Contains("No convierte Web Push en exactly-once", service);
    }

    [Fact]
    public void AutomationExecutionHistoryMustCommitAtomicallyWithJobState()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/AutomationWorker.cs");

        Assert.Contains("BeginTransactionAsync(cancellationToken)", source);
        Assert.Contains("WriteExecutionAsync(connection, transaction", source);
        Assert.Contains("await transaction.CommitAsync(cancellationToken)", source);
        Assert.Contains("NpgsqlTransaction transaction", source);
    }

    [Fact]
    public void StripeAndGoogleCalendarExternalWritesMustBeIdempotent()
    {
        var stripe = ReadServerSource("Anguloso.Server/Logica/StripeBillingService.cs");
        var calendar = ReadServerSource("Anguloso.Server/Logica/GoogleCalendarService.cs");

        Assert.Contains("Idempotency-Key", stripe);
        Assert.Contains("SendStripeAsync(HttpMethod.Post, \"/v1/checkout/sessions\", form, idempotencyKey)", stripe);
        Assert.Contains('var eventId = "dieto" + appointment.id', calendar);\n        Assert.Contains('openid email https://www.googleapis.com/auth/calendar', calendar);
        Assert.Contains("SendEventAsync(accessToken, connection.calendar_id, eventId, payload", calendar);
    }

}
