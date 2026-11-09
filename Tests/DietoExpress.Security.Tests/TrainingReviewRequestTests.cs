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
        var body = portal[start..portal.IndexOf("private async Task NotifyProfessionalAsync", start, StringComparison.Ordinal)];

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
    public void ProfessionalIsNotifiedWithIdempotentTaskAndBestEffortPush()
    {
        var portal = Read("Anguloso.Server", "Controllers", "TrainingPortalController.cs");
        Assert.Contains("'training_review_request'", portal);
        Assert.Contains("training-review-request:{requestId}", portal);
        Assert.Contains("ON CONFLICT (tenant_id, idempotency_key)", portal);
        Assert.Contains("ProfessionalPushService", portal);
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
        Assert.Contains("/review-requests'", trainingTs);
        Assert.Contains("Solicitudes de revisión manual", trainingTs);
        Assert.Contains("Marcar como atendida", trainingTs);
    }
}
