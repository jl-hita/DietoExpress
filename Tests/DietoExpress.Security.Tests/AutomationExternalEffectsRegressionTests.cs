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
        Assert.Contains("Message-ID determinista", source);
    }

    [Fact]
    public void AutomationWorkerMustKeepDatabaseActionsIdempotentBeforeCompletion()
    {
        var source = ReadServerSource("Anguloso.Server/Logica/AutomationWorker.cs");

        // Si el proceso cae después de la acción y antes de CompleteJobAsync, las acciones
        // persistentes deben reconocer el mismo job en lugar de crear un segundo efecto.
        Assert.Contains("\$\"job:{job.Id}\"", source);
        Assert.Contains("\$\"job:{job.Id}:document-provision-failed\"", source);
        Assert.Contains("CreateProfessionalTaskAsync", source);
        Assert.Contains("CreateRequiredDocumentsAsync", source);
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
}
