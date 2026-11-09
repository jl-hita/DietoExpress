using System.IO;
using Xunit;

namespace DietoExpress.Security.Tests;

/// <summary>Regresión de la solicitud de revisión manual de rutina del cliente (portal/Android).</summary>
public sealed class TrainingReviewRequestTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Anguloso.sln"))) dir = dir.Parent;
            return dir!.FullName;
        }
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([RepoRoot, .. parts])).Replace("\r\n", "\n");

    [Fact]
    public void PatientEndpoint_IsScopedToOwnPlanAndEligibleTrainingProfile()
    {
        var portal = Read("Anguloso.Server", "Controllers", "TrainingPortalController.cs");
        var start = portal.IndexOf("public async Task<IActionResult> RequestPlanReview", StringComparison.Ordinal);
        Assert.True(start > 0);
        var body = portal[start..portal.IndexOf("private async Task<bool> PortalFeatureAllowedAsync", start, StringComparison.Ordinal)];

        Assert.Contains("[Authorize(Roles = \"patient\")]", portal);
        Assert.Contains("GetValidatedClientIdAsync", body);
        Assert.Contains("PortalFeatureAllowedAsync", body);
        Assert.Contains("f.client_id = @clientId", body);
        Assert.Contains("f.enabled = TRUE", body);
        Assert.Contains("f.management_mode <> 'none'", body);
        Assert.Contains("p.id = @planId", body);
        Assert.Contains("ON CONFLICT (plan_id) WHERE status = 'pending' DO NOTHING", body);
        Assert.Contains("Conflict(", body);
        Assert.Contains("Note?.Trim()", body);
        Assert.Contains("1000", body);
        // Never touches sessions or plans.
        Assert.DoesNotContain("UPDATE training_sessions", body);
        Assert.DoesNotContain("UPDATE training_plans", body);
    }

    [Fact]
    public void Schema_AllowsOnlyOnePendingRequestPerPlan()
    {
        var bootstrap = Read("Anguloso.Server", "Logica", "DatabaseBootstrap.cs");
        Assert.Contains("CREATE TABLE IF NOT EXISTS training_review_requests", bootstrap);
        Assert.Contains("CHECK (status IN ('pending','attended','rejected'))", bootstrap);
        Assert.Contains("CREATE UNIQUE INDEX IF NOT EXISTS uq_training_review_requests_one_pending", bootstrap);
        Assert.Contains("ON training_review_requests(plan_id) WHERE status = 'pending'", bootstrap);
        Assert.Contains("UpgradeTrainingReviewRequestsSchema(context, logger);", bootstrap);
        Assert.Contains("'training_review_requests'", bootstrap);
    }

    [Fact]
    public void ProfessionalEndpoints_RequireManageAccessAndNeverModifyThePlan()
    {
        var controller = Read("Anguloso.Server", "Controllers", "TrainingPeriodizationController.cs");
        var start = controller.IndexOf("clients/{clientId:int}/review-requests", StringComparison.Ordinal);
        var end = controller.IndexOf("plans/{planId:int}/load-trend", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        var body = controller[start..end];

        Assert.Equal(2, body.Split("CanManageClient(").Length - 1);
        Assert.Contains("request.Decision is not (\"attended\" or \"rejected\")", body);
        Assert.Contains("AND status = 'pending'", body);
        Assert.DoesNotContain("training_sessions", body);
        Assert.DoesNotContain("UPDATE training_plans", body);
    }

    [Fact]
    public void ProfessionalIsNotifiedInBellDashboardTaskPushAndEmail()
    {
        var portal = Read("Anguloso.Server", "Controllers", "TrainingPortalController.cs");
        var service = Read("Anguloso.Server", "Logica", "ProfessionalNotificationService.cs");
        var dashboard = Read("Anguloso.Server", "Controllers", "ProfessionalDashboardController.cs");
        var bootstrap = Read("Anguloso.Server", "Logica", "DatabaseBootstrap.cs");
        var layout = Read("anguloso.client", "src", "app", "componentes", "layout", "layout.component.html");

        var worker = Read("Anguloso.Server", "Logica", "TransactionalOutboxWorker.cs");
        // El email se entrega por la outbox transaccional: reintentos con backoff y errores trazables (al menos una vez).
        Assert.Contains("TransactionalOutbox.EnqueueAsync", portal);
        Assert.Contains("training_review_request_created", portal);
        Assert.Contains("BeginTransactionAsync", portal);
        Assert.Contains("case \"training_review_request_created\"", worker);
        Assert.Contains("DeliverTrainingReviewRequestAsync", worker);
        Assert.Contains("if (!result.Exito) emailFailures.Add", service);
        Assert.Contains("throw new InvalidOperationException(\"No se pudo enviar el email", service);
        Assert.Contains("INSERT INTO professional_notifications", service);
        Assert.Contains("INSERT INTO professional_tasks", service);
        Assert.Contains("ON CONFLICT (user_id, idempotency_key)", service);
        Assert.Contains("training-review-request:{requestId}:{recipient.UserId}", service);
        Assert.Contains("_push.SendNotificationAsync", service);
        Assert.True(service.IndexOf("_email.SendEmailAsync", StringComparison.Ordinal) < service.IndexOf("_push.SendNotificationAsync", StringComparison.Ordinal), "el push va después del email para no duplicarse en los reintentos");
        Assert.Contains("_email.SendEmailAsync", service);
        Assert.Contains("HtmlEncode", service); // la nota del cliente nunca se inyecta sin escapar en el email
        Assert.Contains("client_nutritionist_assignments", service);
        Assert.Contains("TrainingReviewRequests", dashboard);
        Assert.Contains("CREATE TABLE IF NOT EXISTS professional_notifications", bootstrap);
        Assert.Contains("<app-professional-notification-bell>", layout);
    }

    [Fact]
    public void NotificationsEndpointsAreScopedToTheAuthenticatedUser()
    {
        var controller = Read("Anguloso.Server", "Controllers", "ProfessionalNotificationsController.cs");
        var service = Read("Anguloso.Server", "Logica", "ProfessionalNotificationService.cs");
        Assert.Contains("[Authorize(Policy = \"Professional\")]", controller);
        Assert.Equal(4, controller.Split("AuthHelpers.GetUserId(User)").Length - 1);
        Assert.Contains("WHERE id = {0} AND user_id = {1}", service);
        Assert.Contains("WHERE user_id = {0}", service);
    }

    [Fact]
    public void Frontends_ShowRequestButtonAndPendingReviewList()
    {
        var portalTs = Read("anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.ts");
        var portalHtml = Read("anguloso.client", "src", "app", "componentes", "patient-portal", "patient-portal.component.html");
        var trainingTs = Read("anguloso.client", "src", "app", "componentes", "training-periodization", "training-periodization.component.ts");

        Assert.Contains("/api/portal/training/review-requests", portalTs);
        Assert.Contains("/review-request'", portalTs);
        Assert.Contains("Solicitar revisión manual", portalHtml);
        Assert.Contains("plan.reviewRequest?.status !== 'pending'", portalHtml);
        Assert.Contains("role=\"alert\"", portalHtml);
        var panel = Read("anguloso.client", "src", "app", "componentes", "training-periodization", "training-review-requests-panel.component.ts");
        Assert.Contains("<app-training-review-requests-panel", trainingTs);
        Assert.Contains("/review-requests'", panel);
        Assert.Contains("Solicitudes de revisión manual", panel);
        Assert.Contains("Marcar como atendida", panel);
        Assert.Contains("Abrir rutina", panel);
    }

    [Fact]
    public void TrainingWorkspaceImprovements_AreWiredEndToEnd()
    {
        var controller = Read("Anguloso.Server", "Controllers", "TrainingPeriodizationController.cs");
        var bootstrap = Read("Anguloso.Server", "Logica", "DatabaseBootstrap.cs");
        var routes = Read("anguloso.client", "src", "app", "app.routes.ts");
        var sidebar = Read("anguloso.client", "src", "app", "componentes", "sidebar", "sidebar.component.html");
        var ui = Read("anguloso.client", "src", "app", "componentes", "training-periodization", "training-periodization.component.ts");

        // Estado de revisión de bloques: los generados nacen como borrador.
        Assert.Contains("review_status VARCHAR(20) NOT NULL DEFAULT 'reviewed'", bootstrap);
        Assert.Contains("VALUES (@planId, @name, @sequence, @startDate, @endDate, @objective, @notes, 'draft')", controller);
        Assert.Contains("mesocycles/{mesocycleId:int}/review-status", controller);

        // Regeneración de futuros: solo borradores intactos, nunca revisados/registrados/editados.
        var start = controller.IndexOf("ClearFutureDraftMesocycles", StringComparison.Ordinal);
        var body = controller[start..controller.IndexOf("GetPendingReviews", start, StringComparison.Ordinal)];
        Assert.Contains("m.start_date > CURRENT_DATE", body);
        Assert.Contains("training_session_records", body);
        Assert.Contains("s.updated_at > s.created_at + INTERVAL '2 seconds'", body);
        Assert.Contains("isDraft && protectedSessions == 0", body);
        Assert.Contains("if (!dryRun && clearIds.Count > 0)", body);

        // Bandeja global acotada a los clientes del profesional.
        var inbox = controller[controller.IndexOf("GetPendingReviews", StringComparison.Ordinal)..];
        Assert.Contains("cl.user_id = @userId", inbox);
        Assert.Contains("client_nutritionist_assignments", inbox);
        Assert.Contains("path: 'training-inbox'", routes);
        Assert.Contains("routerLink=\"/training-inbox\"", sidebar);

        // Vista previa con historial y deep link.
        Assert.Contains("BuildHistory(", controller);
        Assert.Contains("regenerateFutureDrafts", ui);
        Assert.Contains("applyDeepLink", ui);
        Assert.Contains("Cambios respecto al bloque anterior", ui);
    }
}
