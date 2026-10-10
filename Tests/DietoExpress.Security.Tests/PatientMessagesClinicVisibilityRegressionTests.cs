using Xunit;

namespace DietoExpress.Security.Tests;

public sealed class PatientMessagesClinicVisibilityRegressionTests
{
    [Fact]
    public void ProfessionalConversationList_AllowsClinicAdminsToSeeTenantConversations()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "Anguloso.Server", "Program.cs")))
            root = root.Parent;

        Assert.NotNull(root);
        var source = File.ReadAllText(Path.Combine(root!.FullName, "Anguloso.Server", "Controllers", "PatientMessagesController.cs"));

        Assert.Contains("GetProfessionalConversationsAsync(userId.Value, tenantId.Value, User.IsInRole(\"clinic_admin\"))", source);
        Assert.Contains("int tenantId, bool isClinicAdmin)", source);
        Assert.Contains("AND ({2} OR EXISTS (SELECT 1 FROM client_nutritionist_assignments", source);
        Assert.Contains("tenantId, userId, isClinicAdmin).ToListAsync()", source);
    }
}
